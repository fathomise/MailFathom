// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Application.Rules;
using MailFathom.Application.Rules.Sets;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.UnitTests.TestDoubles;

/// <summary>Each account's rules, their version, and the archive of every committed write, keyed by account as the tables are.</summary>
internal sealed class InMemoryMailRuleSetStore : IMailRuleSetStore
{
    private readonly Dictionary<MailAccountId, StoredMailRuleSet> sets = [];
    private readonly List<MailRuleSetArchiveEntry> archive = [];

    /// <summary>Gets every archive row, oldest first.</summary>
    internal IReadOnlyList<MailRuleSetArchiveEntry> Archive => this.archive;

    /// <summary>Gets how many times a write took the set's lock.</summary>
    internal int LockCount { get; private set; }

    /// <summary>Gets or sets what happens when a write takes the lock, before the set under it is read.</summary>
    /// <remarks>Stands in for a competing write committing just before this one reached the lock.</remarks>
    internal Action? WhenLocked { get; set; }

    /// <summary>Puts a set in front of an account without going through the write path or the archive.</summary>
    /// <param name="set">The set to hold.</param>
    internal void Arrange(StoredMailRuleSet set) => this.sets[set.Account] = set;

    /// <summary>Appends an archive row without going through the write path.</summary>
    /// <param name="entry">The row to append.</param>
    internal void ArrangeArchived(MailRuleSetArchiveEntry entry) => this.archive.Add(entry);

    /// <inheritdoc />
    public Task<StoredMailRuleSet> ReadAsync(MailAccountId account, CancellationToken cancellationToken) =>
        Task.FromResult(this.sets.GetValueOrDefault(account) ?? StoredMailRuleSet.Empty(account));

    /// <inheritdoc />
    public Task<StoredMailRuleSet> LockAsync(
        IPersistenceSession session,
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        this.LockCount++;
        this.WhenLocked?.Invoke();

        return this.ReadAsync(account, cancellationToken);
    }

    /// <inheritdoc />
    public Task CommitAsync(
        IPersistenceSession session,
        MailRuleSetArchiveEntry committed,
        CancellationToken cancellationToken)
    {
        this.sets[committed.Account] = new StoredMailRuleSet(committed.Account, committed.Version, committed.Declarations);
        this.archive.Add(committed);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduledMailRule>> ReadScheduledRulesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ScheduledMailRule>>(
        [
            .. this.sets.Values
                .OrderBy(set => set.Account.Value, StringComparer.Ordinal)
                .SelectMany(set => set.Declarations
                    .Where(declaration => declaration.Enabled && declaration.Schedule is not null)
                    .Select(declaration => new ScheduledMailRule(set.Account, declaration.Id, declaration.Schedule!))),
        ]);

    /// <inheritdoc />
    public Task<MailRuleSetArchiveEntry?> FindArchivedAsync(
        MailAccountId account,
        MailRuleSetRevision revision,
        DateTimeOffset committedAtOrBefore,
        CancellationToken cancellationToken) =>
        Task.FromResult(this.archive
            .Where(entry => entry.Account == account && entry.Revision == revision && entry.CommittedAt <= committedAtOrBefore)
            .MaxBy(entry => entry.Version));

    /// <inheritdoc />
    /// <remarks>Keeps the newest row of the account, as the real store does; which rows a run names is not modelled here.</remarks>
    public Task<int> EraseExpiredArchiveAsync(
        MailAccountId account,
        DateTimeOffset committedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        var newest = this.archive.Where(entry => entry.Account == account).MaxBy(entry => entry.Version);
        var expired = this.archive
            .Where(entry => entry.Account == account && entry.CommittedAt < committedBefore && entry != newest)
            .OrderBy(entry => entry.CommittedAt)
            .Take(limit)
            .ToArray();

        foreach (var entry in expired)
        {
            this.archive.Remove(entry);
        }

        return Task.FromResult(expired.Length);
    }
}
