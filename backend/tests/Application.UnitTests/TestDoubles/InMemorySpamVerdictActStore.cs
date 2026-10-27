// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Spam;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Spam;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Keeps what verdicts did to each message in memory, written at once rather than when the session commits.</summary>
internal sealed class InMemorySpamVerdictActStore : ISpamVerdictActStore
{
    private readonly Dictionary<StoredEmailId, HashSet<SpamVerdictAct>> acts = [];

    /// <summary>Gets the acts recorded for one message, empty where none was.</summary>
    /// <param name="emailId">The stored email.</param>
    /// <returns>The acts.</returns>
    internal IReadOnlySet<SpamVerdictAct> Of(StoredEmailId emailId) =>
        this.acts.TryGetValue(emailId, out var recorded) ? recorded : new HashSet<SpamVerdictAct>();

    /// <summary>States that a verdict already did something to a message, as an earlier pass would have recorded it.</summary>
    /// <param name="emailId">The stored email.</param>
    /// <param name="recorded">What was done.</param>
    /// <returns>This store.</returns>
    internal InMemorySpamVerdictActStore With(StoredEmailId emailId, params SpamVerdictAct[] recorded)
    {
        foreach (var act in recorded)
        {
            this.Add(emailId, act);
        }

        return this;
    }

    /// <inheritdoc />
    public Task<IReadOnlySet<SpamVerdictAct>> FindAsync(StoredEmailId emailId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<SpamVerdictAct>>(new HashSet<SpamVerdictAct>(this.Of(emailId)));

    /// <inheritdoc />
    public Task RecordAsync(
        IPersistenceSession session,
        StoredEmailId emailId,
        SpamVerdictAct act,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        this.Add(emailId, act);

        return Task.CompletedTask;
    }

    private void Add(StoredEmailId emailId, SpamVerdictAct act)
    {
        if (!this.acts.TryGetValue(emailId, out var recorded))
        {
            recorded = [];
            this.acts[emailId] = recorded;
        }

        recorded.Add(act);
    }
}
