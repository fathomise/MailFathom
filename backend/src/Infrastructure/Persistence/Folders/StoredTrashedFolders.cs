// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Folders;

/// <summary>Names the folders of an account that sit in its Trash: the Trash folder itself and every folder beneath it.</summary>
/// <remarks>
/// Deleting a folder the account keeps moves it beneath Trash rather than erasing it, so a folder's own role says
/// nothing about whether its mail was deleted. The answer is read from the parent chain, which is why it is computed
/// over the account's rows rather than written as a clause on the folder a message is in.
/// </remarks>
internal static class StoredTrashedFolders
{
    /// <summary>Reads the identifiers of every folder in the account's Trash.</summary>
    /// <param name="folders">The folder rows to read from.</param>
    /// <param name="mailboxAccountId">The configured account whose folders are read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The identifiers, empty where the account has no Trash.</returns>
    [RequiresIntegrationCoverage]
    internal static async Task<Guid[]> ReadAsync(
        IQueryable<MailFolderEntity> folders,
        string mailboxAccountId,
        CancellationToken cancellationToken)
    {
        var rows = await folders
            .AsNoTracking()
            .Where(folder => folder.MailboxAccountId == mailboxAccountId)
            .Select(static folder => new StoredFolderRole(folder.Id, folder.ParentId, folder.Role))
            .ToArrayAsync(cancellationToken);

        return Within(rows);
    }

    /// <summary>Reads the identifiers of every folder in the Trash of any of several accounts.</summary>
    /// <param name="folders">The folder rows to read from.</param>
    /// <param name="mailboxAccountIds">The configured accounts whose folders are read.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>The identifiers, empty where none of the accounts has a Trash.</returns>
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
            .Select(static folder => new StoredFolderRole(folder.Id, folder.ParentId, folder.Role))
            .ToArrayAsync(cancellationToken);

        return Within(rows);
    }

    /// <summary>Picks the folders whose parent chain reaches a Trash folder, the Trash folder included.</summary>
    /// <param name="folders">The folders of one account or of several.</param>
    /// <returns>The identifiers of the folders in Trash.</returns>
    /// <remarks>A parent the rows do not carry ends the chain outside Trash, and a chain that loops ends at the first folder it revisits.</remarks>
    internal static Guid[] Within(IReadOnlyCollection<StoredFolderRole> folders)
    {
        var byId = folders.ToDictionary(static folder => folder.Id);

        return [.. byId.Values.Where(folder => IsInTrash(folder, byId)).Select(static folder => folder.Id)];
    }

    private static bool IsInTrash(StoredFolderRole folder, Dictionary<Guid, StoredFolderRole> byId)
    {
        var visited = new HashSet<Guid>();
        var current = folder;

        while (visited.Add(current.Id))
        {
            if (current.Role == MailFolderSpecialUse.Trash)
            {
                return true;
            }

            if (current.ParentId is not { } parentId || !byId.TryGetValue(parentId, out var parent))
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>One folder's place in the hierarchy and the role it plays.</summary>
    /// <param name="Id">The folder.</param>
    /// <param name="ParentId">The folder it sits in, or <see langword="null" /> at the top of the account.</param>
    /// <param name="Role">The special use the folder plays, if any.</param>
    internal sealed record StoredFolderRole(Guid Id, Guid? ParentId, MailFolderSpecialUse? Role);
}
