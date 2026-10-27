// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Settlement;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Holds settling messages in memory and settles each one once, as the conditional statement does.</summary>
internal sealed class InMemoryMailSettlementStore : IMailSettlementStore
{
    private readonly List<SettlingEmail> settling = [];
    private readonly HashSet<StoredEmailId> settledElsewhere = [];

    /// <summary>Gets the messages settled through this store, in the order they settled.</summary>
    public List<StoredEmailId> Settled { get; } = [];

    /// <summary>Adds messages that are still settling.</summary>
    /// <param name="emails">The messages.</param>
    /// <returns>This store.</returns>
    public InMemoryMailSettlementStore Holding(params SettlingEmail[] emails)
    {
        this.settling.AddRange(emails);

        return this;
    }

    /// <summary>Settles a message as a competing run would, between this run's read and its write.</summary>
    /// <param name="email">The message the other run settled.</param>
    public void SettledElsewhere(StoredEmailId email) => this.settledElsewhere.Add(email);

    /// <inheritdoc />
    public Task<IReadOnlyList<SettlingEmail>> ReadSettlingAsync(
        MailAccountId account,
        StoredEmailId? resumeAfter,
        int batchSize,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SettlingEmail>>(
        [
            .. this.settling
                .Where(email => email.AccountId == account
                    && !this.Settled.Contains(email.Id)
                    && (resumeAfter is null || email.Id.Value.CompareTo(resumeAfter.Value.Value) > 0))
                .OrderBy(static email => email.Id.Value)
                .Take(batchSize),
        ]);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredEmailId>> SettleAsync(
        IReadOnlyList<StoredEmailId> emails,
        DateTimeOffset settledAt,
        CancellationToken cancellationToken)
    {
        StoredEmailId[] settledHere = [.. emails.Where(email => !this.Settled.Contains(email) && !this.settledElsewhere.Contains(email))];

        this.Settled.AddRange(settledHere);

        return Task.FromResult<IReadOnlyList<StoredEmailId>>(settledHere);
    }
}
