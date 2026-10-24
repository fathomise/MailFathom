// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders.Local;

/// <summary>Describes an account's own folders as the folder surface reports them, under the acts its custodian allows.</summary>
/// <remarks>
/// A folder is named by its identity, which is the identity every other read reports it by. Whether anything may be
/// done to the folders is the custodian's to say rather than this type's: a held account allows every act on a folder
/// that plays no role, and an account whose mailbox is being restored allows none.
/// </remarks>
internal static class LocalMailFolderManagement
{
    /// <summary>Describes the account's folders.</summary>
    /// <param name="holding">The account's folders.</param>
    /// <param name="allowsActs">Whether the account allows acts on its folders at all.</param>
    /// <returns>The folders and their acts.</returns>
    internal static MailFolderManagement Describe(LocalMailFolderHolding holding, bool allowsActs)
    {
        var folders = holding.Folders;
        var mayCreate = allowsActs && folders.Count < LocalMailFolderTree.MaximumFolders;

        return new MailFolderManagement(
            mayCreate ? [MailFolderAct.Create] : [],
            // A held account is given every role it needs the moment its hierarchy is read, and an archive there is an
            // ordinary folder somebody makes, so no role is ever waiting to be created.
            [],
            [
                .. folders
                    .Select(folder => Describe(folder, allowsActs))
                    .OrderBy(static folder => folder.Name, StringComparer.OrdinalIgnoreCase),
            ]);
    }

    /// <summary>Describes what an edit of a held account's folders did.</summary>
    /// <param name="outcome">The edit's outcome.</param>
    /// <returns>The outcome as the folder surface reports it.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the outcome carries neither a folder nor a refusal.</exception>
    internal static MailFolderActOutcome Describe(LocalMailFolderEditOutcome outcome) =>
        outcome is { Folder: { } folder, Kind: { } kind }
            ? new MailFolderActOutcome(Describe(folder, allowsActs: true), kind, Refusal: null, outcome.MailErasureDeferred)
            : MailFolderActOutcome.Refused(
                outcome.Refusal ?? throw new InvalidOperationException("An edit outcome carried neither a folder nor a refusal."));

    private static ManagedMailFolder Describe(LocalMailFolder folder, bool allowsActs) => new(
        folder.Id.Value.ToString(),
        folder.ParentId?.Value.ToString(),
        folder.Name.Value,
        folder.Role,
        folder.IsProtected || !allowsActs
            ? []
            : [MailFolderAct.Rename, MailFolderAct.Move, MailFolderAct.Delete]);
}
