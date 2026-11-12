// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Mailboxes;
using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Domain.Emails;

namespace MailFathom.AI.UnitTests.TestDoubles;

/// <summary>Stands in for this deployment's retrieval, recording the scope and the whole lookup every call arrived with.</summary>
/// <remarks>
/// A recorder rather than a substitute, because what the tests using it assert is the scope and the narrowing each call
/// carried — an argument matcher would state the expectation in the assertion instead of reading what actually reached
/// the port.
/// </remarks>
internal sealed class RecordingEmailKnowledgeSearch : IEmailKnowledgeSearch
{
    private readonly List<KnowledgeSearchCall> calls = [];
    private readonly Dictionary<string, IReadOnlyList<EmailKnowledgePassage>> passagesByQuery =
        new(StringComparer.Ordinal);

    private readonly Dictionary<(StoredEmailId Email, string QueryText), IReadOnlyList<EmailKnowledgePassage>> passagesWithin = [];
    private readonly List<(MailboxScope Scope, StoredEmailId Email, string QueryText)> callsWithin = [];

    private MailboxQueryFilterInvalidException? refusal;
    private EmailSearchRetrievalMode retrievalMode = EmailSearchRetrievalMode.Hybrid;

    /// <summary>Gets what each lookup asked for, in order.</summary>
    public IReadOnlyList<KnowledgeSearchCall> Calls => this.calls;

    /// <summary>Arranges what one query finds.</summary>
    /// <param name="queryText">The query to answer.</param>
    /// <param name="passages">What it finds.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    public RecordingEmailKnowledgeSearch Returning(string queryText, params EmailKnowledgePassage[] passages)
    {
        this.passagesByQuery[queryText] = passages;

        return this;
    }

    /// <summary>Gets what each search inside one message asked for, in order.</summary>
    public IReadOnlyList<(MailboxScope Scope, StoredEmailId Email, string QueryText)> CallsWithin => this.callsWithin;

    /// <summary>Arranges what a search inside one message finds.</summary>
    /// <param name="email">The message searched.</param>
    /// <param name="queryText">The query to answer.</param>
    /// <param name="passages">What it finds.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    public RecordingEmailKnowledgeSearch ReturningWithin(StoredEmailId email, string queryText, params EmailKnowledgePassage[] passages)
    {
        this.passagesWithin[(email, queryText)] = passages;

        return this;
    }

    /// <summary>Arranges how this retrieval reports it ranked.</summary>
    /// <param name="mode">The mode every lookup reports.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    public RecordingEmailKnowledgeSearch RankingBy(EmailSearchRetrievalMode mode)
    {
        this.retrievalMode = mode;

        return this;
    }

    /// <summary>Arranges a retrieval whose use case refuses every lookup, as it does for a filter it cannot accept.</summary>
    /// <param name="filterName">Which filter the refusal names.</param>
    /// <returns>This retrieval, so arrangement reads as one statement.</returns>
    public RecordingEmailKnowledgeSearch Refusing(string filterName)
    {
        this.refusal = MailboxQueryFilterInvalidException.NotAnAddress(filterName);

        return this;
    }

    /// <inheritdoc />
    public Task<EmailKnowledgeLookup> FindPassagesAsync(
        MailboxScope scope,
        EmailKnowledgeQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        this.calls.Add(new KnowledgeSearchCall(scope, query));

        if (this.refusal is { } refused)
        {
            throw refused;
        }

        return Task.FromResult(EmailKnowledgeLookup.Unfiltered(
            this.passagesByQuery.GetValueOrDefault(query.QueryText, []),
            this.retrievalMode));
    }

    /// <inheritdoc />
    public Task<EmailKnowledgeLookup> FindPassagesWithinAsync(
        MailboxScope scope,
        StoredEmailId emailId,
        string queryText,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();

        this.callsWithin.Add((scope, emailId, queryText));

        if (this.refusal is { } refused)
        {
            throw refused;
        }

        return Task.FromResult(EmailKnowledgeLookup.Unfiltered(
            this.passagesWithin.GetValueOrDefault((emailId, queryText), []),
            EmailSearchRetrievalMode.Lexical));
    }

    /// <summary>What one lookup asked for.</summary>
    /// <param name="Scope">The accounts and folders the lookup was restricted to.</param>
    /// <param name="Query">The query and the narrowing the model wrote.</param>
    internal sealed record KnowledgeSearchCall(MailboxScope Scope, EmailKnowledgeQuery Query)
    {
        /// <summary>Gets the text the model wrote, which most assertions are about on its own.</summary>
        public string QueryText => this.Query.QueryText;
    }
}
