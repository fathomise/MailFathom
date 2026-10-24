// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>Describes one folder as a screen drawing a mailbox tree reads it.</summary>
/// <param name="Id">The folder's MailFathom-issued identity, which is what every request names it by.</param>
/// <param name="Freshness">How current the local copy of the folder is, or <see langword="null" /> for a folder no source synchronizes.</param>
/// <param name="Role">The role the folder plays for its account, or <see langword="null" /> for none.</param>
/// <param name="HierarchyLevels">The folder names from the top of the account's hierarchy down to this one.</param>
/// <param name="StoredEmailCount">How many of the folder's emails this deployment holds.</param>
/// <param name="UnreadEmailCount">How many of those are unread.</param>
/// <remarks>
/// <para>
/// The role is the answer a screen cannot work out for itself. Special-use folders are identified by server attributes
/// rather than by their names, which differ per provider and per language, so a client that guessed which folder is the
/// sent one from its name would guess wrong on somebody's Polish provider. It is the role the folder row plays, which
/// synchronization takes from configuration on a mirrored account and the hierarchy keeps on a held one.
/// </para>
/// <para>
/// The levels are split rather than published as a path and a delimiter, so no reader has to know that a delimiter
/// exists, and they are names to draw rather than an identity: a rename changes them and leaves <paramref name="Id" />.
/// </para>
/// <para>
/// The counts are of the local copy. A folder still being backfilled holds fewer than the mail server does, which is
/// why the freshness travels in the same value rather than being available separately. A folder with no freshness is
/// one MailFathom keeps itself — every folder of a held account, and one a person created — and its counts are the
/// whole of it.
/// </para>
/// </remarks>
public sealed record DescribedMailFolder(
    MailFolderId Id,
    MailFolderFreshness? Freshness,
    MailFolderSpecialUse? Role,
    IReadOnlyList<string> HierarchyLevels,
    int StoredEmailCount,
    int UnreadEmailCount);
