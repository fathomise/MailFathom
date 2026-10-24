// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Folders;

/// <summary>Names the folder a stored email is in, the same way whichever custody phase its account is in.</summary>
/// <param name="Id">The folder's MailFathom-issued identity, which is what a caller names the folder by.</param>
/// <param name="Path">The folder names from the top of the account's hierarchy down to it, joined by <see cref="LocalMailFolderName.HierarchyDelimiter" />, for a person to read.</param>
/// <param name="Alias">The alias configuration names the folder by, or <see langword="null" /> for a folder no declaration names.</param>
/// <remarks>
/// The path is a name rather than an identity: a rename or a move changes it, which is why every request names the
/// folder by <paramref name="Id" />. The alias is carried for the per-folder settings configuration keys on it, and
/// nothing published names a folder by it.
/// </remarks>
public sealed record StoredEmailFolder(MailFolderId Id, string Path, MailFolderAlias? Alias)
{
    /// <summary>Joins hierarchy levels into the path a folder is read by.</summary>
    /// <param name="levels">The folder names, outermost first.</param>
    /// <returns>The path.</returns>
    public static string PathOf(IEnumerable<string> levels) =>
        string.Join(LocalMailFolderName.HierarchyDelimiter, levels);
}
