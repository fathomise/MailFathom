// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Chunking;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Persistence;
using MailFathom.CodeCoverage;
using MailFathom.Domain.Emails;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace MailFathom.Infrastructure.Persistence.Embeddings;

/// <summary>EF Core store of the vectors a message's passages carry under one profile.</summary>
[RequiresIntegrationCoverage]
internal sealed class EmailEmbeddingStore(MailFathomDbContext dbContext, TimeProvider timeProvider) : IEmailEmbeddingStore
{
    /// <inheritdoc />
    /// <remarks>
    /// The outstanding passages are decided by the absence of a vector row for this profile rather than by a column on
    /// the chunk, so nothing has to be reset when a profile is activated and no second place can disagree with the
    /// vectors that actually exist.
    /// </remarks>
    public async Task<IReadOnlyList<EmailChunkAwaitingEmbedding>> GetChunksAwaitingEmbeddingAsync(
        StoredEmailId storedEmailId,
        EmbeddingProfileId profileId,
        int maxCount,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCount);

        var passages = await dbContext.EmailChunks
            .AsNoTracking()
            .Where(chunk => chunk.StoredEmailId == storedEmailId.Value)
            .Where(chunk => !chunk.Embeddings.Any(embedding => embedding.EmbeddingProfileId == profileId.Value))
            .OrderBy(chunk => chunk.Ordinal)
            .Take(maxCount)
            .Select(chunk => new OutstandingPassageRow(chunk.Id, chunk.Text))
            .ToArrayAsync(cancellationToken);

        return [.. passages.Select(passage => new EmailChunkAwaitingEmbedding(
            EmailChunkId.Create(passage.Id),
            passage.Text))];
    }

    /// <inheritdoc />
    /// <remarks>
    /// One statement whose conflict clause replaces a passage's existing vector under this profile, so re-embedding a
    /// passage and losing a race to a competing writer are one case rather than two: neither violates the key, so
    /// neither reaches the retry policy and neither is logged as a failed command.
    /// <see cref="EmailEmbeddingUpsertStatement" /> holds why the read-then-insert it replaced was the wrong shape.
    /// </remarks>
    public async Task SaveEmbeddingsAsync(
        IPersistenceSession session,
        RegisteredEmbeddingProfile profile,
        IReadOnlyList<GeneratedChunkEmbedding> embeddings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(embeddings);

        if (embeddings.Count == 0)
        {
            return;
        }

        var sessionDbContext = await EfCorePersistenceSessionAccessor.JoinAsync(session, cancellationToken);

        // The width is written from the profile rather than inherited through the profile reference: it is half of
        // what the check constraint compares, so the row has to state what this write believed it was storing.
        object[] parameters =
        [
            profile.Id.Value,
            profile.Identity.Dimension,
            timeProvider.GetUtcNow(),
            .. embeddings.SelectMany(embedding => new object[]
            {
                embedding.ChunkId.Value,
                new Vector(embedding.Vector.Components),
            }),
        ];

        await sessionDbContext.Database.ExecuteSqlRawAsync(
            EmailEmbeddingUpsertStatement.Compose(sessionDbContext.Model, embeddings.Count),
            parameters,
            cancellationToken);
    }

    /// <summary>One outstanding passage, as the projection returns it.</summary>
    private sealed record OutstandingPassageRow(Guid Id, string Text);
}
