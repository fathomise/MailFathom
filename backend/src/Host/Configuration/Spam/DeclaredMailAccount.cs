// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Host.Configuration.Spam;

/// <summary>One account as a claim about its folders is judged against it: its identifier and its mapped folders.</summary>
/// <param name="AccountId">The account's identifier, as the synchronization section writes it.</param>
/// <param name="MappedFolders">
/// Every folder this account maps, whether or not it mirrors one, because a mapping is all a destination needs. A folder
/// the account only maps is resolved when a change first files into it rather than by a run of its own, so filing into
/// one is reachable in exactly the way filing into a mirrored folder is.
/// </param>
internal sealed record DeclaredMailAccount(string AccountId, IReadOnlyCollection<DeclaredMailFolder> MappedFolders)
{
    /// <summary>Reports whether a destination names a folder this account maps.</summary>
    /// <param name="destination">The alias or role that was written.</param>
    /// <returns><see langword="true" /> when one mapped folder of this account answers to that name.</returns>
    /// <remarks>
    /// The two kinds of name are answered together rather than by two rules that could drift apart, which is what makes
    /// a destination of <c>role:Junk</c> refused for exactly the accounts that map no junk folder — and accepted for the
    /// ones that do, whatever each of them called it and whether or not they mirror it.
    /// </remarks>
    internal bool Maps(MailFolderReference destination) =>
        this.MappedFolders.Any(folder => folder.IsNamedBy(destination));
}
