// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Folders;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace MailFathom.Infrastructure.Persistence.Folders;

/// <summary>Names each folder by its place in its account's hierarchy: the folder names from the top down to it.</summary>
/// <remarks>
/// A path is read from the parent chain rather than stored, so a rename or a move is one row update however many
/// folders sit beneath it. It is a name to read rather than an identity: two folders never share one among live
/// siblings, but a path stops naming a folder the moment it is renamed, which is why everything that has to keep naming
/// a folder holds its identifier instead.
/// </remarks>
internal static class StoredMailFolderPaths
{
    /// <summary>Reads the path of every folder of the given accounts.</summary>
    /// <param name="folders">The folder rows to read from.</param>
    /// <param name="accountIds">The accounts whose folders are wanted.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>Each folder's path, outermost level first.</returns>
    [RequiresIntegrationCoverage]
    internal static async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> ReadAsync(
        IQueryable<MailFolderEntity> folders,
        IReadOnlyCollection<string> accountIds,
        CancellationToken cancellationToken)
    {
        if (accountIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        string[] accounts = [.. accountIds.Distinct(StringComparer.Ordinal)];

        var rows = await folders
            .AsNoTracking()
            .Where(folder => accounts.Contains(folder.MailboxAccountId))
            .Select(static folder => new StoredMailFolderNode(folder.Id, folder.ParentId, folder.Name))
            .ToArrayAsync(cancellationToken);

        return Compose(rows);
    }

    /// <summary>Names the folder a stored email is in, from the paths <see cref="ReadAsync" /> answered.</summary>
    /// <param name="folderId">The folder the email is in.</param>
    /// <param name="alias">The alias configuration names the folder by, or <see langword="null" /> for none.</param>
    /// <param name="paths">The paths of the folders of the email's account.</param>
    /// <returns>The folder, with an empty path when the paths do not carry it.</returns>
    /// <remarks>
    /// Erased folders are read too, because one keeps its rows while its mail is erased in passes, and a message read in
    /// between is still in a folder with a name.
    /// </remarks>
    internal static StoredEmailFolder Describe(
        Guid folderId,
        string? alias,
        IReadOnlyDictionary<Guid, IReadOnlyList<string>> paths) => new(
            MailFolderId.Create(folderId),
            StoredEmailFolder.PathOf(paths.GetValueOrDefault(folderId) ?? []),
            alias is null ? null : MailFolderAlias.Create(alias));

    /// <summary>Composes each folder's path from the parent chain the rows describe.</summary>
    /// <param name="nodes">The folders of one or more accounts.</param>
    /// <returns>Each folder's path, outermost level first.</returns>
    /// <remarks>
    /// A parent the rows do not carry ends the chain where it is, so a folder whose parent was erased reads as the top of
    /// what remains rather than disappearing; a corrupted chain that loops ends at the first folder it revisits.
    /// </remarks>
    internal static IReadOnlyDictionary<Guid, IReadOnlyList<string>> Compose(IReadOnlyCollection<StoredMailFolderNode> nodes)
    {
        var byId = nodes.ToDictionary(static node => node.Id);

        return byId.Values.ToDictionary(static node => node.Id, node => (IReadOnlyList<string>)SegmentsOf(node, byId));
    }

    private static List<string> SegmentsOf(StoredMailFolderNode folder, Dictionary<Guid, StoredMailFolderNode> byId)
    {
        var segments = new List<string>();
        var visited = new HashSet<Guid>();
        var current = folder;

        while (visited.Add(current.Id))
        {
            segments.Add(current.Name);

            if (current.ParentId is not { } parentId || !byId.TryGetValue(parentId, out var parent))
            {
                break;
            }

            current = parent;
        }

        segments.Reverse();

        return segments;
    }
}
