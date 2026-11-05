// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Rules.Sets;

/// <summary>Keeps each mail account's rules, the set's version, and the archive of every committed write.</summary>
/// <remarks>
/// PostgreSQL is the only place a rule lives. A read takes no lock, so a pass never waits on a write; a write takes the
/// set's row lock through <see cref="LockAsync" />, which serializes the writes to one account's rules and nothing else,
/// and commits through <see cref="CommitAsync" /> in the same session.
/// </remarks>
public interface IMailRuleSetStore
{
    /// <summary>Reads one account's rules as they stand, without locking anything.</summary>
    /// <param name="account">The account whose rules are read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored set, which is the empty set at version <c>0</c> for an account nobody has written a rule for.</returns>
    Task<StoredMailRuleSet> ReadAsync(MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Takes the account's set row lock for the session's transaction and reads the set under it.</summary>
    /// <param name="session">The write transaction the lock is held for.</param>
    /// <param name="account">The account whose rules are about to be written.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The stored set as it stands under the lock.</returns>
    /// <remarks>The set row is created first when the account has none, so a first write is serialized exactly like any later one.</remarks>
    Task<StoredMailRuleSet> LockAsync(IPersistenceSession session, MailAccountId account, CancellationToken cancellationToken);

    /// <summary>Writes the set a committed change produced and appends its archive row, inside the session holding the lock.</summary>
    /// <param name="session">The write transaction that took <see cref="LockAsync" />.</param>
    /// <param name="committed">The change: the whole set as it stands after the write, its version, and who wrote it.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task CommitAsync(IPersistenceSession session, MailRuleSetArchiveEntry committed, CancellationToken cancellationToken);

    /// <summary>Reads every enabled rule of every account that walks its mailbox on a schedule of its own.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The scheduled rules, in account and position order.</returns>
    Task<IReadOnlyList<ScheduledMailRule>> ReadScheduledRulesAsync(CancellationToken cancellationToken);

    /// <summary>Finds the archive row a past run resolves its revision to.</summary>
    /// <param name="account">The account the run belongs to.</param>
    /// <param name="revision">The revision the run recorded.</param>
    /// <param name="committedAtOrBefore">When the run read the set, which the row must have committed at or before.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The newest such row, or <see langword="null" /> when the revision was never archived or the row was pruned.</returns>
    Task<MailRuleSetArchiveEntry?> FindArchivedAsync(
        MailAccountId account,
        MailRuleSetRevision revision,
        DateTimeOffset committedAtOrBefore,
        CancellationToken cancellationToken);

    /// <summary>Erases archive rows nothing retained needs any more, up to a bound.</summary>
    /// <param name="account">The account whose archive is pruned.</param>
    /// <param name="committedBefore">The instant a row must have committed before to be erased.</param>
    /// <param name="limit">The most rows one call erases.</param>
    /// <param name="cancellationToken">Cancels the erasure.</param>
    /// <returns>How many rows were erased.</returns>
    /// <remarks>
    /// A row is kept while it is the account's newest, and while a retained execution or evaluation run names its
    /// revision, so what the current set says and what a retained run ran under are never pruned.
    /// </remarks>
    Task<int> EraseExpiredArchiveAsync(
        MailAccountId account,
        DateTimeOffset committedBefore,
        int limit,
        CancellationToken cancellationToken);
}
