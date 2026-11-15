// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Folders;

/// <summary>Names the folders whose mail a person put away: Trash, Archive, and every folder beneath either.</summary>
/// <remarks>
/// It is the rule the list of conversations leaves a conversation out by, and it is wider than
/// <see cref="StoredTrashedFolders" /> on purpose: archived mail is kept and still counts wherever only deleted mail is
/// set aside, so a reader that asks about deletion alone keeps asking that type.
/// </remarks>
internal static class StoredPutAwayFolders
{
    private static readonly MailFolderSpecialUse[] PutAwayRoles = [MailFolderSpecialUse.Trash, MailFolderSpecialUse.Archive];

    /// <summary>Reads the identifiers of every folder in the Trash or the Archive of any of several accounts.</summary>
    /// <param name="folders">The folder rows to read from.</param>
    /// <param name="mailboxAccountIds">The configured accounts whose folders are read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The identifiers, empty where none of the accounts has a Trash or an Archive.</returns>
    /// <remarks>One read rather than one per account: a folder identifier is unique across the deployment, so the chains of several accounts never meet.</remarks>
    [RequiresIntegrationCoverage]
    internal static async Task<Guid[]> ReadAsync(
        IQueryable<MailFolderEntity> folders,
        string[] mailboxAccountIds,
        CancellationToken cancellationToken)
    {
        var rows = await folders
            .AsNoTracking()
            .Where(folder => mailboxAccountIds.Contains(folder.MailboxAccountId))
            .Select(static folder => new StoredTrashedFolders.StoredFolderRole(folder.Id, folder.ParentId, folder.Role))
            .ToArrayAsync(cancellationToken);

        return Within(rows);
    }

    /// <summary>Picks the folders whose parent chain reaches a Trash or an Archive folder, those folders included.</summary>
    /// <param name="folders">The folders of one account or of several.</param>
    /// <returns>The identifiers of the folders in Trash or in Archive.</returns>
    internal static Guid[] Within(IReadOnlyCollection<StoredTrashedFolders.StoredFolderRole> folders) =>
        StoredTrashedFolders.Beneath(folders, PutAwayRoles);
}
