// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using MailFathom.Application.AiProviders;
using MailFathom.Application.Categories;
using MailFathom.Application.Emails.Census;
using MailFathom.Application.Emails.Embeddings;
using MailFathom.Application.Emails.Extraction.Attachments;
using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Emails.Search.Attachments;
using MailFathom.Application.Emails.SearchEmails;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Observability;
using MailFathom.Application.Retrieval;
using MailFathom.Application.Synchronization.Checkpoints;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Evaluations.AgentConversations;
using MailFathom.Evaluations.Corpus;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using MailFathom.TestSupport;

namespace MailFathom.Evaluations.Answering;

/// <summary>Answers the answering agent's lookups from the synthetic corpus, held in memory as one inbox, through the deployment's own knowledge search.</summary>
/// <remarks>
/// <para>
/// What is measured is the agent — which lookups it writes, which filters it reaches for, and what it does with what
/// comes back — so what it searches has to be what a deployment searches. Every step a deployment takes is its own
/// class here: <see cref="MailboxKnowledgeSearch" /> over <see cref="MailboxSearchReader" />, which validates the query
/// and the filters, decides how the words match through <see cref="EmailSearchQueryText.MatchedUnder" />, bounds the
/// window, and cuts the passages, and the selection a deployment's PostgreSQL query narrows by is
/// <see cref="StoredEmailSelectionPredicate" /> itself, run over the corpus as rows. A copy of any of that would let the
/// harness and a deployment disagree about which mail a lookup finds.
/// </para>
/// <para>
/// What stands in for the database is the full-text index alone, which has no form outside PostgreSQL: any word or
/// quoted phrase of the matched text admits a message, an excluded one leaves it out, a message carrying more of the
/// words ranks higher with the newest first on a tie, and each match is cut into the extracts
/// <see cref="EmailSearchSnippetBounds.Default" /> allows. Semantic retrieval is off, as on a deployment that embeds
/// nothing, so every search ranks lexically.
/// </para>
/// <para>
/// The corpus carries no server state, so every message reads as unread, unflagged, and carrying no keyword.
/// </para>
/// <para>
/// The same rows answer <see cref="Timeline" />, which is what the Agent's listing reads: narrowed by the same predicate
/// and ordered by <see cref="EmailTimelinePosition.ComparerFor" />, the total order a deployment's query reproduces.
/// </para>
/// </remarks>
/// <param name="corpus">The messages the inbox holds.</param>
internal sealed partial class CorpusKnowledgeSearch(IReadOnlyList<CorpusMessage> corpus) : IEmailKnowledgeSearch
{
    /// <summary>The account the corpus is delivered to.</summary>
    public static readonly MailAccountId Account = MailAccountId.Create("owner");

    /// <summary>The folder the corpus is delivered to.</summary>
    public static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    /// <summary>The folder every corpus message is delivered to.</summary>
    public static readonly StoredEmailFolder InboxFolder = new(
        MailFolderId.Create(Guid.Parse("0199c1a2-0000-7000-8000-0000000000e1")),
        "INBOX",
        Inbox);

    private static readonly MailFolderIdentity InboxIdentity = new(Account, Inbox);

    private static readonly MailFolderEntity InboxRow = new()
    {
        Id = InboxFolder.Id.Value,
        MailboxAccountId = Account.Value,
        Name = Inbox.Value,
        NameKey = Inbox.Value,
        Alias = Inbox.Value,
    };

    private readonly ConcurrentQueue<string> queries = new();

    private readonly ConcurrentQueue<string> narrowedBy = new();

    private readonly MailboxKnowledgeSearch search = SearchOver(corpus);

    private readonly CorpusSearchIndex index = new(corpus);

    /// <summary>Gets the corpus as the stored timeline a listing reads, newest or oldest first, a page at a time.</summary>
    public IStoredEmailTimelineReader Timeline => this.index;

    /// <summary>Gets the corpus as the stored mail a census counts, through the deployment's own composition of the count.</summary>
    public IStoredEmailCensusReader Census => this.index;

    /// <summary>Counts the whole corpus by the deployment's own statement, with every group rather than one reading's leaders.</summary>
    /// <param name="dimension">What the mail is grouped by.</param>
    /// <param name="zone">The clock a period is read on.</param>
    /// <returns>The census of every message in <see cref="Scope" />.</returns>
    /// <remarks>Synchronous because the corpus is in memory, so a case states the figure it expects from the statement a count runs rather than from a second reading of the rule.</remarks>
    public MailCensus CountWhole(MailCensusDimension dimension, UserTimeZone zone) =>
        this.index.Count(
            MailboxEmailSelection.Create(Scope, null, null, null, null, null, null, null, null, null),
            new MailCensusGrouping(dimension, zone, new Dictionary<MailAccountId, IReadOnlyList<string>>()),
            int.MaxValue);

    /// <summary>Gets the scope every lookup of a run is answered from: the one inbox, admitted as a deployment's folder mapping admits it.</summary>
    public static MailboxScope Scope { get; } = MailboxScope.Create([Account], [InboxFolder.Id]).RestrictedTo([InboxIdentity]);

    /// <summary>Gets the words of every lookup this search answered, in the order they arrived.</summary>
    public IReadOnlyList<string> Queries => [.. this.queries];

    /// <summary>Gets the category of every lookup that named one, in the order they arrived.</summary>
    public IReadOnlyList<string> NarrowedBy => [.. this.narrowedBy];

    /// <inheritdoc />
    public Task<EmailKnowledgeLookup> FindPassagesAsync(
        MailboxScope scope,
        EmailKnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);

        this.queries.Enqueue(query.QueryText);

        if (query.Category is { } category)
        {
            this.narrowedBy.Enqueue(category);
        }

        return this.search.FindPassagesAsync(scope, query, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EmailKnowledgeLookup> FindPassagesWithinAsync(
        MailboxScope scope,
        StoredEmailId emailId,
        string queryText,
        CancellationToken cancellationToken)
    {
        this.queries.Enqueue(queryText);

        return this.search.FindPassagesWithinAsync(scope, emailId, queryText, cancellationToken);
    }

    private static MailboxKnowledgeSearch SearchOver(IReadOnlyList<CorpusMessage> corpus)
    {
        // The window an answering run reads asks for no grant of its own: the run asked for the assistant's at its entry.
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.AssistantUse);
        var participation = StubMailFolderParticipation.Mapping(InboxIdentity);
        var nothingEmbedded = new NothingEmbedded();
        var unobserved = new Unobserved();

        return new MailboxKnowledgeSearch(
            new MailboxSearchReader(
                new CorpusSearchIndex(corpus),
                new CorpusAttachmentText(corpus),
                new SemanticEmailSearch(nothingEmbedded, nothingEmbedded, nothingEmbedded, TimeProvider.System, textEmbeddingGenerator: null),
                unobserved,
                new MailboxScopeResolver(
                    AssignedMailAccountCatalogs.For(authorization, SyntheticServedAccount.Of(Account)),
                    participation,
                    StubDeploymentMailFolders.Of(participation),
                    StubMailFolderMappings.ResolvingNothing),
                EmailSearchSnippetBounds.Default,
                SensitiveContentEgressGuards.Inactive(),
                unobserved,
                authorization,
                StubMailCategories.Offering(new StubMailCategories(
                    new Dictionary<MailAccountId, MailCategoryOffer> { [Account] = MailCategoryOffer.Of([], []) },
                    corpus.ToDictionary(static message => message.Id, static message => message.Provenance.Categories)))),
            StubEmailProvenanceReader.Answering(corpus.ToDictionary(static message => message.Id, static message => message.Provenance)),
            EmailKnowledgeBounds.Default);
    }

    [GeneratedRegex("-?\"[^\"]+\"|\\S+")]
    private static partial Regex QueryToken();

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();

    /// <summary>The full-text index a deployment keeps in PostgreSQL, over the corpus held as the rows it would have stored.</summary>
    private sealed class CorpusSearchIndex(IReadOnlyList<CorpusMessage> corpus) : IEmailSearchIndexReader, IStoredEmailTimelineReader, IStoredEmailCensusReader
    {
        private readonly IReadOnlyList<StoredEmailEntity> rows = [.. corpus.Select(RowOf)];

        private readonly Dictionary<Guid, CorpusMessage> messages = corpus.ToDictionary(static message => message.Id.Value);

        public Task<IReadOnlyList<RankedEmailCandidate>> ReadRankedCandidatesAsync(
            MailboxEmailSelection selection,
            EmailSearchQueryText queryText,
            int limit,
            CancellationToken cancellationToken)
        {
            var (matched, excluded) = TermsOf(queryText);

            // Whole words over the terms websearch_to_tsquery reads rather than PostgreSQL's own parser, and the
            // words carried counted rather than ts_rank_cd's cover density; run the scenarios over the real index in the
            // retrieval evaluation if the ranking itself is measured. A query that only excludes matches everything else,
            // as websearch_to_tsquery reads one.
            IReadOnlyList<RankedEmailCandidate> ranked =
            [
                .. this.Selected(selection)
                    .Where(message => !excluded.Any(term => Mentions(message, term)))
                    .Select(message => (Message: message, Score: matched.Count(term => Mentions(message, term))))
                    .Where(candidate => candidate.Score > 0 || (matched.Count is 0 && excluded.Count > 0))
                    .Select(static candidate => new RankedEmailCandidate(
                        new EmailTimelinePosition(candidate.Message.ReceivedAt, candidate.Message.Id),
                        candidate.Score))
                    .Order(RankedEmailCandidate.BestFirst)
                    .Take(limit),
            ];

            return Task.FromResult(ranked);
        }

        public Task<IReadOnlyList<EmailSearchMatch>> ReadMatchesAsync(
            MailboxEmailSelection selection,
            EmailSearchQueryText queryText,
            EmailSearchSnippetBounds snippetBounds,
            IReadOnlyList<RankedEmailCandidate> rankedCandidates,
            CancellationToken cancellationToken)
        {
            var eligible = this.Selected(selection).ToDictionary(static message => message.Id);
            var (matched, _) = TermsOf(queryText);

            IReadOnlyList<EmailSearchMatch> matches =
            [
                .. rankedCandidates
                    .Where(candidate => eligible.ContainsKey(candidate.StoredEmailId))
                    .Select(candidate => MatchOf(eligible[candidate.StoredEmailId], candidate.Score, matched, snippetBounds)),
            ];

            return Task.FromResult(matches);
        }

        /// <summary>Reads the terms a query matches and the ones it excludes, from the text a deployment hands <c>websearch_to_tsquery</c>.</summary>
        /// <exception cref="NotSupportedException">Thrown for a query requiring every word, which only the lexical half of a fusion reads.</exception>
        internal static (IReadOnlyList<string> Matched, IReadOnlyList<string> Excluded) TermsOf(EmailSearchQueryText queryText)
        {
            if (queryText.WordMatching is not EmailSearchWordMatching.AnyWord)
            {
                throw new NotSupportedException("The corpus search embeds nothing, so a deployment's lexical ranking stands alone and matches any word.");
            }

            // A query that excludes and matches nothing else travels as written, its exclusions inside the matched text.
            var written = WordsOf(queryText.MatchedText).ToArray();

            return (
                [.. written.Where(static word => !word.StartsWith('-')).Select(Bare).Where(static word => word.Length > 0)],
                [
                    .. written.Where(static word => word.StartsWith('-'))
                        .Concat(WordsOf(queryText.ExcludedText ?? string.Empty))
                        .Select(Bare)
                        .Where(static word => word.Length > 0),
                ]);
        }

        private static IEnumerable<string> WordsOf(string text) =>
            QueryToken().Matches(text)
                .Select(static token => token.Value)
                .Where(static token => !token.Equals("or", StringComparison.OrdinalIgnoreCase));

        /// <summary>Strips what PostgreSQL's parser reads as no part of a word: the exclusion mark, the quotes, and the punctuation around it.</summary>
        private static string Bare(string word) =>
            word.TrimStart('-').Trim('"').Trim('.', ',', ';', ':', '!', '?', '(', ')', '\'');

        /// <summary>Reads whether a message carries a term as whole words, as the <c>simple</c> configuration a deployment indexes under reads it: no stem, no stop word, and a phrase's words one after another.</summary>
        private static bool Mentions(CorpusMessage message, string term)
        {
            var words = Word().Matches(term).Select(static word => Regex.Escape(word.Value)).ToArray();

            return words.Length > 0
                && Regex.IsMatch(
                    SearchedTextOf(message),
                    string.Concat(@"(?<![\p{L}\p{N}])", string.Join(@"[^\p{L}\p{N}]+", words), @"(?![\p{L}\p{N}])"),
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        /// <summary>Reads what a deployment's search document holds for a message: its own text, then the words its files yielded.</summary>
        private static string SearchedTextOf(CorpusMessage message) => string.Join(
            "\n\n",
            [message.GroundingText, .. LongMail.Readings.GetValueOrDefault(message.Id, []).Select(static reading => reading.Text)]);

        /// <summary>Writes a message as the row a deployment would have stored, carrying what the selection predicate reads.</summary>
        /// <remarks>An agent's read never reaches the folder binding, which only the person's own junk exclusion reads.</remarks>
        private static StoredEmailEntity RowOf(CorpusMessage message)
        {
            var row = new StoredEmailEntity
            {
                Id = message.Id.Value,
                MailboxAccountId = Account.Value,
                MailFolderBinding = null!,
                FolderId = InboxFolder.Id.Value,
                Folder = InboxRow,
                Subject = message.Subject,
                SentAt = message.ReceivedAt,
                ReceivedAt = message.ReceivedAt,
                SenderNormalizedAddress = CorpusMailbox.NormalizedAddressOf(message.Sender),
                ToAddresses = [.. message.Recipients.Select(CorpusMailbox.NormalizedAddressOf).OfType<string>()],
                AttachmentCount = message.Attachments.Count,
                SettledAt = message.ReceivedAt,
            };

            foreach (var category in message.Provenance.Categories)
            {
                row.Categories.Add(new EmailCategoryEntity { Category = category, Origin = "corpus", QuestionRevision = "corpus" });
            }

            return row;
        }

        /// <summary>Cuts the message down to the extracts a deployment's <c>ts_headline</c> returns: a few short windows around the matched words, those carrying the most of them first.</summary>
        /// <remarks>
        /// The size is what matters here rather than the exact words: a whole passage per message is several times what a
        /// deployment sends, so twenty of them spend a run's whole retrieval allowance on one lookup, and an extract cut from
        /// the start of a message rather than around the match misses the sentence that carries the answer.
        /// </remarks>
        private static EmailSearchMatch MatchOf(
            CorpusMessage message,
            float score,
            IReadOnlyList<string> matched,
            EmailSearchSnippetBounds snippets)
        {
            // One window per matched word's first occurrence in a passage, scored by the words it carries — not
            // ts_headline's cover density; measure the ranking over the real search if an extract's exact words matter.
            IReadOnlyList<string> fragments =
            [
                .. message.Passages
                    .SelectMany(passage => matched
                        .Select(term => passage.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
                        .Where(static position => position >= 0)
                        .Select(position => WindowAround(passage.Text, position, snippets.WordsPerSnippet)))
                    .Distinct(StringComparer.Ordinal)
                    .OrderByDescending(fragment => matched.Count(term => fragment.Contains(term, StringComparison.OrdinalIgnoreCase)))
                    .Take(snippets.SnippetsPerEmail)
                    .DefaultIfEmpty(message.Passages.Count is 0 ? string.Empty : WindowAround(message.Passages[0].Text, 0, snippets.WordsPerSnippet)),
            ];

            return new EmailSearchMatch(CorpusReaders.SummaryOf(message, thread: null), score, fragments);
        }

        /// <summary>Takes the words around a position, a third before it and the rest from it.</summary>
        /// <remarks>
        /// A matched word usually opens the clause that answers, so a window split evenly cut the fact it was found for
        /// halfway through its sentence and a model was graded for quoting what it was shown.
        /// </remarks>
        internal static string WindowAround(string text, int position, int words)
        {
            var before = text[..position].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).TakeLast(words / 3);
            var after = text[position..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(words - (words / 3));

            return string.Join(' ', before.Concat(after));
        }

        public Task<IReadOnlyList<EmailSummary>> ReadPageAsync(
            EmailTimelineFilter filter,
            EmailTimelinePosition? continueAfter,
            int limit,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(filter);

            var order = EmailTimelinePosition.ComparerFor(filter.Direction);

            IReadOnlyList<EmailSummary> page =
            [
                .. this.Selected(filter.Selection)
                    .Select(static message => CorpusReaders.SummaryOf(message, thread: null))
                    .Order(Comparer<EmailSummary>.Create((first, second) => order.Compare(first.Position, second.Position)))
                    .Where(summary => continueAfter is not { } boundary || order.Compare(summary.Position, boundary) > 0)
                    .Take(limit),
            ];

            return Task.FromResult(page);
        }

        public Task<MailCensus> CountAsync(
            MailboxEmailSelection selection,
            MailCensusGrouping grouping,
            int leadingGroups,
            CancellationToken cancellationToken) => Task.FromResult(this.Count(selection, grouping, leadingGroups));

        /// <summary>Counts the rows a selection admits through the statics the deployment's census statement is composed of.</summary>
        public MailCensus Count(MailboxEmailSelection selection, MailCensusGrouping grouping, int leadingGroups)
        {
            ArgumentNullException.ThrowIfNull(selection);
            ArgumentNullException.ThrowIfNull(grouping);

            var matching = this.SelectedRows(selection).ToArray().AsQueryable();

            return grouping.Dimension switch
            {
                MailCensusDimension.Total => MailCensus.Empty with { Total = matching.Count() },
                MailCensusDimension.Category => StoredEmailCensusReader.CategoryCensusOf(
                    [.. StoredEmailCensusReader.CategoryGroups(matching, grouping)],
                    grouping,
                    leadingGroups,
                    matching.Count()),
                _ => StoredEmailCensusReader.LeadingGroups(matching, grouping, leadingGroups).ToArray() is { Length: > 0 } rows
                    ? StoredEmailCensusReader.CensusOf(rows, grouping.Dimension)
                    : MailCensus.Empty with { Total = matching.Count() },
            };
        }

        /// <summary>Reads the mail a selection admits, through the predicate a deployment's query narrows by.</summary>
        /// <remarks>
        /// The subject fragment is matched here rather than by the predicate, because the predicate matches it with
        /// PostgreSQL's <c>ILIKE</c>, which has no form outside the database; everything else is the predicate's own.
        /// </remarks>
        private IEnumerable<CorpusMessage> Selected(MailboxEmailSelection selection) =>
            this.SelectedRows(selection).Select(row => this.messages[row.Id]);

        /// <summary>Reads the rows a selection admits, as <see cref="Selected" /> reads the messages.</summary>
        private IEnumerable<StoredEmailEntity> SelectedRows(MailboxEmailSelection selection)
        {
            var subjectFragment = selection.SubjectFragment;
            var predicateSelection = subjectFragment is null
                ? selection
                : MailboxEmailSelection.Create(
                    selection.Scope,
                    selection.SenderNormalizedAddress,
                    selection.RecipientNormalizedAddress,
                    subjectFragment: null,
                    selection.ReceivedOnOrAfter,
                    selection.ReceivedBefore,
                    selection.IsMarkedSeen,
                    selection.IsFlagged,
                    selection.Keyword,
                    selection.HasAttachments,
                    selection.Mark);

            return StoredEmailSelectionPredicate.Matching(this.rows.AsQueryable(), predicateSelection)
                .AsEnumerable()
                .Where(row => subjectFragment is null || (row.Subject?.Contains(subjectFragment, StringComparison.OrdinalIgnoreCase) ?? false));
        }
    }

    /// <summary>Reads the words the files of a corpus message yielded, as a deployment's attachment index cuts extracts from the rows the background reading stored.</summary>
    /// <remarks>
    /// The rows are <see cref="LongMail.Readings" />: every other corpus attaches files whose words nothing stored, which
    /// is what a deployment holds for a file its reading has not reached. A picture's description is never matched, as
    /// only a semantic ranking reaches one and the corpus embeds nothing.
    /// </remarks>
    private sealed class CorpusAttachmentText(IReadOnlyList<CorpusMessage> corpus) : IEmailAttachmentMatchReader
    {
        private readonly Dictionary<StoredEmailId, CorpusMessage> messages = corpus.ToDictionary(static message => message.Id);

        public Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadWrittenMatchesAsync(
            MailboxEmailSelection selection,
            EmailSearchQueryText queryText,
            EmailSearchSnippetBounds snippetBounds,
            IReadOnlyList<StoredEmailId> rankedEmailIds,
            CancellationToken cancellationToken)
        {
            var (matched, _) = CorpusSearchIndex.TermsOf(queryText);

            IReadOnlyList<StoredEmailAttachmentMatches> found =
            [
                .. rankedEmailIds
                    .Where(this.messages.ContainsKey)
                    .Distinct()
                    .Select(emailId => new StoredEmailAttachmentMatches(emailId, [.. MatchesIn(this.messages[emailId], matched, snippetBounds)]))
                    .Where(static email => email.Matches.Count > 0),
            ];

            return Task.FromResult(found);
        }

        public Task<IReadOnlyList<StoredEmailAttachmentMatches>> ReadDepictedMatchesAsync(
            MailboxEmailSelection selection,
            EmailSearchSnippetBounds snippetBounds,
            IReadOnlyList<StoredEmailId> depictedEmailIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StoredEmailAttachmentMatches>>([]);

        private static IEnumerable<EmailAttachmentMatch> MatchesIn(CorpusMessage message, IReadOnlyList<string> matched, EmailSearchSnippetBounds snippets) =>
            LongMail.Readings.GetValueOrDefault(message.Id, [])
                .Where(static reading => reading.Reading.WasRead)
                .Select(reading => (
                    Reading: reading,
                    Positions: matched
                        .Select(term => reading.Text.IndexOf(term, StringComparison.OrdinalIgnoreCase))
                        .Where(static position => position >= 0)
                        .Order()
                        .ToArray()))
                .Where(static found => found.Positions.Length > 0)
                .Select(found => new EmailAttachmentMatch(
                    found.Reading.Reading.AttachmentPosition,
                    message.Attachments[found.Reading.Reading.AttachmentPosition].FileName,
                    message.Attachments[found.Reading.Reading.AttachmentPosition].DeclaredMediaType,
                    found.Reading.Reading.Kind,
                    AttachmentTextSegment.At(found.Reading.Segments, found.Positions[0]),
                    [
                        .. found.Positions
                            .Select(position => CorpusSearchIndex.WindowAround(found.Reading.Text, position, snippets.WordsPerSnippet))
                            .Distinct(StringComparer.Ordinal)
                            .Take(snippets.SnippetsPerEmail),
                    ]));
    }

    /// <summary>Stands for a deployment that activated no embedding profile, so semantic retrieval is inactive and nothing past the profile is asked.</summary>
    private sealed class NothingEmbedded : IActiveEmbeddingProfileReader, IEmailVectorSearchIndexReader, IAiProviderHealthReader
    {
        public Task<RegisteredEmbeddingProfile?> FindActiveProfileAsync(CancellationToken cancellationToken) =>
            Task.FromResult<RegisteredEmbeddingProfile?>(null);

        public Task<SemanticEmailRankings> ReadNearestCandidatesAsync(
            MailboxEmailSelection selection,
            RegisteredEmbeddingProfile profile,
            EmbeddingVector queryVector,
            int limit,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException("No profile is active, so no vector is ever searched.");

        public AiProviderHealth Read(AiProviderRole role) =>
            throw new NotSupportedException("No profile is active, so no embedding provider is ever asked about.");
    }

    /// <summary>Reports no synchronization and records no read: the corpus was never synchronized, and a run reports its own span.</summary>
    private sealed class Unobserved : ISynchronizationFreshnessReader, IMailboxReadTelemetry
    {
        public Task<IReadOnlyList<MailboxFolderFreshness>> ReadAsync(MailboxScope scope, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MailboxFolderFreshness>>([]);

        public IMailboxReadScope BeginRead(MailboxReadOperation operation, CancellationToken cancellationToken) => new UnobservedRead();

        public IMailboxReadScope BeginSearchRanking(CancellationToken cancellationToken) => new UnobservedRead();

        private sealed class UnobservedRead : IMailboxReadScope
        {
            public void Completed(int resultCount)
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
