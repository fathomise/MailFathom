// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Mail.Retention;

/// <summary>Erases the stored rows that have been tombstones for long enough.</summary>
public interface IStoredTombstoneEraser
{
    /// <summary>Erases one batch of an account's tombstones, with the raw MIME and everything derived from each.</summary>
    /// <param name="session">The transaction the erasure commits in.</param>
    /// <param name="account">The account whose tombstones are erased.</param>
    /// <param name="retention">The folders left alone, read from <see cref="MailTombstoneRetention.ExcludedFolders" />.</param>
    /// <param name="tombstonedBefore">The moment a row must have become a tombstone before to be erased.</param>
    /// <param name="isSourceDrained">Whether the account's source is being emptied, so an occurrence it still holds is recorded for the drain.</param>
    /// <param name="limit">The most rows this call erases.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <returns>How many rows were staged for erasure, which is below the limit when no more could be taken now.</returns>
    /// <remarks>
    /// A row whose delete has not been observed to settle is not a tombstone yet, and neither is one with a change still
    /// outstanding. Erasure is conditional on the row not having moved since it was read, so a row another writer
    /// changed meanwhile surfaces as a concurrency conflict at commit and nothing the batch staged takes effect.
    /// </remarks>
    Task<int> EraseAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailTombstoneRetention retention,
        DateTimeOffset tombstonedBefore,
        bool isSourceDrained,
        int limit,
        CancellationToken cancellationToken);
}
