// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders.Local;

/// <summary>What local state holds about one account's own folders.</summary>
/// <param name="Folders">The account's live folders.</param>
/// <param name="ErasedSourceAliases">The source folders whose corresponding local folder has been erased.</param>
/// <remarks>
/// It says nothing about which copy of the mailbox is the truth: that is the custody phase, which only
/// <see cref="MailboxCustodianResolver" /> reads for an act.
/// </remarks>
public sealed record LocalMailFolderHolding(
    IReadOnlyList<LocalMailFolder> Folders,
    IReadOnlyList<MailFolderAlias> ErasedSourceAliases)
{
    /// <summary>Gets the rules the account's folders are acted on by.</summary>
    /// <returns>The tree of the live folders.</returns>
    public LocalMailFolderTree ToTree() => new(this.Folders, this.ErasedSourceAliases);
}
