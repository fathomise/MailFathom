// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Application.Mail.Mutations.Destinations;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Authoring;

/// <summary>Answers whether a person's move would carry mail across the edge of the drafts or the sent folder's tree.</summary>
/// <remarks>
/// Where a message is is read off the folder it is in rather than off its <c>\Draft</c> flag, because the folder is what a
/// client opens a message in its composer by, and a mail server files a draft or a sent copy there whichever client wrote
/// it. The drafts folder and the sent folder each stand for their whole subtree, so mail moves freely between such a
/// folder and the subfolders somebody made beneath it, and never across the edge of either — except into the trash,
/// which is how it is deleted. Every move a person makes asks this, the one they name and the one a spam mark carries,
/// because a check reachable on only one of two paths is no boundary.
/// </remarks>
internal sealed class KeptMailTrees(MailFolderReferenceResolver folderReferences, IMailFolderResolutionStore bindings)
{
    /// <summary>The roles whose folders are each the root of a tree that mail is filed neither into nor out of.</summary>
    private static MailFolderSpecialUse[] KeptTreeRoles { get; } = [MailFolderSpecialUse.Drafts, MailFolderSpecialUse.Sent];

    /// <summary>Answers whether a message in the source folder would cross the edge of a kept tree by being filed into the destination.</summary>
    /// <param name="account">The account both folders belong to.</param>
    /// <param name="source">The folder the message is in on the server.</param>
    /// <param name="destination">The folder it would be filed into.</param>
    /// <param name="cancellationToken">Cancels the binding reads.</param>
    /// <returns><see langword="true" /> where the move would cross an edge.</returns>
    internal async Task<bool> CrossesAsync(
        MailAccountId account,
        MailFolderResolution source,
        MailboxDestination destination,
        CancellationToken cancellationToken)
    {
        if (destination.Role == MailFolderSpecialUse.Trash)
        {
            return false;
        }

        var keptTrees = await this.ReadKeptTreeRootsAsync(account, cancellationToken);

        return TreeOf(source.Alias, source.RemotePath, keptTrees) != TreeOf(destination.Alias, destination.Path, keptTrees);
    }

    /// <summary>Reads where the account's drafts folder and sent folder were last found, without asking the mail server.</summary>
    /// <remarks>
    /// A folder the account maps to neither role contributes no tree. One mapped and never bound — never synchronized,
    /// and never resolved since — has no place on the server anything could lie beneath, so it stands for itself alone.
    /// </remarks>
    private async Task<IReadOnlyList<KeptTreeRoot>> ReadKeptTreeRootsAsync(
        MailAccountId account,
        CancellationToken cancellationToken)
    {
        var roots = new List<KeptTreeRoot>(KeptTreeRoles.Length);

        foreach (var role in KeptTreeRoles)
        {
            if (folderReferences.TryResolve(account, MailFolderReference.ToRole(role)) is not { } mapping)
            {
                continue;
            }

            var binding = await bindings.GetCurrentResolutionAsync(account, mapping.Alias, cancellationToken);

            roots.Add(new KeptTreeRoot(mapping.Alias, binding?.RemotePath));
        }

        return roots;
    }

    /// <summary>Names the kept tree a folder lies in, or nothing where it lies in neither.</summary>
    private static MailFolderAlias? TreeOf(
        MailFolderAlias alias,
        RemoteFolderPath path,
        IReadOnlyList<KeptTreeRoot> keptTrees) =>
        keptTrees
            .Where(root => root.Alias == alias || (root.Path is { } rootPath && path.IsWithin(rootPath)))
            .Select(static root => (MailFolderAlias?)root.Alias)
            .FirstOrDefault();

    /// <summary>The drafts or the sent folder, and where on the server it was last found where it has been.</summary>
    private sealed record KeptTreeRoot(MailFolderAlias Alias, RemoteFolderPath? Path);
}
