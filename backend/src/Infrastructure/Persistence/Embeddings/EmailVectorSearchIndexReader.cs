// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Linq.Expressions;
using MailFathom.Application.Emails.AttachmentText;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Emails;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>Ranks stored mail by pgvector distance from a query vector, under one profile and one set of filters.</summary>
/// <remarks>
/// <para>
/// The distance operator is chosen from the profile's own metric rather than fixed, because the metric is part of what
/// the profile's vectors mean: measuring a space built for inner product by cosine returns a number rather than an
/// error, and the results would be quietly worse instead of visibly wrong. All three operators pgvector offers order
/// the same way — a smaller value is nearer — so the ranking below is written once.
/// </para>
/// <para>
/// The structured filters join the ranking rather than trailing it. That is what the use case requires and it is also
/// what makes the query honest: post-filtering would measure the query against mail the caller may not see in order to
/// decide the order of mail they may, and would return fewer results than asked for exactly when the caller narrowed
/// the scope most.
/// </para>
/// <para>
/// It follows from that join that an approximate index cannot serve this query: an HNSW scan orders the whole table,
/// and a filter on a joined table is not something the index can carry. The ranking is therefore exact, which is the
/// trade this feature is willing to make — an exact ranking is deterministic, and determinism is what the fused order
/// rests on.
/// </para>
/// <para>
/// That trade has been measured rather than assumed, on 100 000 messages and 259 034 vectors at 1536 dimensions, and
/// the exactness stands: a five-hundred-row approximate window comes back as forty rows and keeps a median of four of
/// them once the caller's own folder and date filters are applied, and none at all once they name a sender.
/// A profile-partial HNSW index built over that corpus was never chosen by this query's plan, and it cost 3073 MB
/// beside a 3137 MB table, forty-five minutes to build, and a hundredfold slowdown on every embedding write — so
/// MailFathom builds none. <see href="https://github.com/fathomise/MailFathom/blob/main/docs/architecture/semantic-ranking-cost.md">What
/// a semantic search costs</see> holds the whole measurement, including what the one-pass ranking below costs against
/// the correlated minimum per message it replaced.
/// </para>
/// <para>
/// Two rankings come out of one eligible set, on the partition
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md">ADR 0030</see>
/// requires: what a person wrote — a body, or a document attachment's own words — and what a model said a picture
/// shows. The two are separated by the kind recorded on the attachment row the passage hangs on, so what decides it is
/// a stored column rather than a media type re-read per query, and a passage cut from the body belongs to the written
/// side by carrying no attachment position at all.
/// </para>
/// <para>
/// The query vector reaches PostgreSQL as a parameter, like every other value a request carries. Nothing about a query
/// is composed into the statement text.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class EmailVectorSearchIndexReader(MailFathomDbContext dbContext) : IEmailVectorSearchIndexReader
{
    /// <inheritdoc />
    public async Task<SemanticEmailRankings> ReadNearestCandidatesAsync(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(queryVector);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        // Sequential rather than concurrent: both statements reach the same scoped context, which serves one operation
        // at a time, so starting them together would fault instead of overlapping.
        var written = await this.RankAsync(selection, profile, queryVector, limit, depicted: false, cancellationToken);
        var depicted = await this.RankAsync(selection, profile, queryVector, limit, depicted: true, cancellationToken);

        return new SemanticEmailRankings(written, depicted);
    }

    /// <summary>Composes the query that ranks the eligible emails by their nearest embedded passage of one kind.</summary>
    /// <param name="selection">The validated structural filters.</param>
    /// <param name="profile">The profile whose vectors are searched and whose metric measures the distance.</param>
    /// <param name="queryVector">Where the query lands in that profile's space.</param>
    /// <param name="limit">The greatest number of emails to return.</param>
    /// <param name="depicted">Whether the ranking measures descriptions of pictures rather than words a person wrote.</param>
    /// <returns>The composed query, which nothing has executed yet.</returns>
    /// <remarks>
    /// <para>
    /// Exposed for the reason the lexical ranking query is: that the query vector arrives as a parameter, that the
    /// profile narrows the rows before any distance is measured, and that the two rankings read disjoint passages of one
    /// eligible set are claims about the generated command, and a test asserting them against anything else would pass
    /// whatever this method did.
    /// </para>
    /// <para>
    /// The eligible mail is joined to its passages and their vectors, and the distances are grouped by message, so
    /// PostgreSQL reads each eligible vector once and keeps the nearest one per message. A minimum per message is what
    /// makes each email one row: ranking passages instead would let a single long message fill a window with its own
    /// paragraphs while a shorter message that answers the query better never appeared. Mail this profile has no vector
    /// for falls out of the join rather than being ranked as infinitely distant, and the kind narrows the same way, so a
    /// message whose only vectors are descriptions is absent from the written ranking rather than present in it at some
    /// distance.
    /// </para>
    /// <para>
    /// The kind test is written as an equality against the caller's choice rather than as two predicates, because the
    /// two sides are exactly complementary: a passage hangs on a described picture or it does not, and a body passage —
    /// which hangs on no attachment at all — falls to the written side by the same test. It is an outer join on the
    /// attachment row's primary key, which can match at most one row and so multiplies nothing, rather than an
    /// existence test, because a correlated existence test is a subplan PostgreSQL keeps out of parallel workers and
    /// would hold every join beneath the aggregate to one process. What the test reads is one column of that key
    /// rather than the row: tested as the row, it becomes a conjunction over both key columns, and tested on a column
    /// outside the key, PostgreSQL cannot tell that a missing value means an unmatched row. Either way the planner
    /// estimates that a single row survives and reaches every vector through a nested loop, pinning more than twice the
    /// buffers; on the key, it plans the written side as an anti-join and the depicted side as an inner one.
    /// </para>
    /// </remarks>
    internal IQueryable<StoredEmailVectorHitRow> NearestHitsQuery(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        bool depicted)
    {
        var profileId = profile.Id.Value;
        var target = new Vector(queryVector.Components);

        var passages =
            from email in StoredEmailSelectionPredicate.Matching(dbContext.StoredEmails.AsNoTracking(), selection)
            from chunk in email.Chunks
            from describedEmailId in email.AttachmentTexts
                .Where(text => text.AttachmentPosition == chunk.AttachmentPosition
                    && text.Kind == AttachmentTextKind.ImageDescription)
                .Select(text => (Guid?)text.StoredEmailId)
                .DefaultIfEmpty()
            where (describedEmailId != null) == depicted
            from vector in chunk.Embeddings
            where vector.EmbeddingProfileId == profileId
            select new EmbeddedPassage
            {
                StoredEmailId = email.Id,
                ReceivedAt = email.ReceivedAt,
                Embedding = vector.Embedding,
            };

        var nearestPerEmail = passages
            .GroupBy(
                static passage => new { passage.StoredEmailId, passage.ReceivedAt },
                DistanceFrom(target, profile.Identity.DistanceMetric))
            .Select(static distances => new StoredEmailVectorHitRow
            {
                StoredEmailId = distances.Key.StoredEmailId,
                ReceivedAt = distances.Key.ReceivedAt,
                Distance = distances.Min(),
            });

        return NewestFirstAmongEqual(nearestPerEmail.OrderBy(static hit => hit.Distance)).Take(limit);
    }

    /// <summary>Breaks a tie in distance the same way under every metric: dated mail before undated, newest first, then by identity.</summary>
    /// <param name="ranked">Mail already ordered by its distance from the query.</param>
    /// <returns>The same mail in a total order, so the ranking is the same on every run.</returns>
    internal static IOrderedQueryable<StoredEmailVectorHitRow> NewestFirstAmongEqual(
        IOrderedQueryable<StoredEmailVectorHitRow> ranked) =>
        ranked
            .ThenBy(static hit => hit.ReceivedAt == null)
            .ThenByDescending(static hit => hit.ReceivedAt)
            .ThenByDescending(static hit => hit.StoredEmailId);

    /// <summary>Runs one of the two rankings and reads it into candidates.</summary>
    private async Task<IReadOnlyList<RankedEmailCandidate>> RankAsync(
        MailboxEmailSelection selection,
        RegisteredEmbeddingProfile profile,
        EmbeddingVector queryVector,
        int limit,
        bool depicted,
        CancellationToken cancellationToken)
    {
        var hits = await this.NearestHitsQuery(selection, profile, queryVector, limit, depicted)
            .ToArrayAsync(cancellationToken);

        return
        [
            .. hits.Select(static hit => new RankedEmailCandidate(
                new EmailTimelinePosition(hit.ReceivedAt, StoredEmailId.Create(hit.StoredEmailId)),
                (float)hit.Distance)),
        ];
    }

    /// <summary>Measures a passage against the query with the operator the profile's metric names.</summary>
    /// <remarks>
    /// The choice returns an expression rather than a delegate because the operator has to be part of the tree
    /// PostgreSQL is handed: a delegate would either fail to translate or be evaluated in the process against vectors
    /// that never left the database.
    /// </remarks>
    private static Expression<Func<EmbeddedPassage, double>> DistanceFrom(
        Vector target,
        EmbeddingDistanceMetric distanceMetric) => distanceMetric switch
        {
            EmbeddingDistanceMetric.Cosine => passage => passage.Embedding.CosineDistance(target),
            EmbeddingDistanceMetric.InnerProduct => passage => passage.Embedding.MaxInnerProduct(target),
            EmbeddingDistanceMetric.EuclideanDistance => passage => passage.Embedding.L2Distance(target),
            _ => throw new ArgumentOutOfRangeException(
                nameof(distanceMetric),
                distanceMetric,
                "The distance metric has no pgvector operator."),
        };

    /// <summary>One embedded passage of an eligible email, before its distance from the query is measured.</summary>
    private sealed class EmbeddedPassage
    {
        public required Guid StoredEmailId { get; init; }

        public required DateTimeOffset? ReceivedAt { get; init; }

        public required Vector Embedding { get; init; }
    }
}
