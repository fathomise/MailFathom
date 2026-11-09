// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Synchronization.Reconciliation;

/// <summary>Reads and retires the occurrences a delete left on their mail server and took out of MailFathom for good.</summary>
/// <remarks>
/// Such a delete sends the server nothing, so the message stays exactly where it was and a pass walking the folder from
/// an earlier point would store it again. All that is kept of it is its position on the server: an account, a folder, a
/// UIDVALIDITY, and a UID, which is what this reads a batch against.
/// </remarks>
public interface IForgottenOccurrenceStore
{
    /// <summary>Answers which of the UIDs a batch named must not be mirrored.</summary>
    /// <param name="account">The account the folder belongs to.</param>
    /// <param name="folderResolutionId">The alias binding being synchronized.</param>
    /// <param name="uidValidity">The UIDVALIDITY the session reports.</param>
    /// <param name="uids">The UIDs the batch named.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The UIDs among <paramref name="uids" /> that are forgotten.</returns>
    Task<IReadOnlySet<ImapUid>> ReadForgottenUidsAsync(
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        IReadOnlyCollection<ImapUid> uids,
        CancellationToken cancellationToken);

    /// <summary>Removes what the folder remembers under any UIDVALIDITY but the one it reports now.</summary>
    /// <param name="session">The transaction the removal commits in.</param>
    /// <param name="account">The account the folder belongs to.</param>
    /// <param name="folderResolutionId">The alias binding being synchronized.</param>
    /// <param name="uidValidity">The UIDVALIDITY the session reports.</param>
    /// <param name="cancellationToken">Cancels the removal.</param>
    /// <returns>A task that completes when the removal is done.</returns>
    /// <remarks>A UID means nothing under another UIDVALIDITY, so a record naming one could only hide somebody else's mail.</remarks>
    Task RetireOtherUidValiditiesAsync(
        IPersistenceSession session,
        MailAccountId account,
        MailFolderResolutionId folderResolutionId,
        ImapUidValidity uidValidity,
        CancellationToken cancellationToken);
}
