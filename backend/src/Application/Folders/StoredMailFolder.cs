// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Folders;

/// <summary>What local state holds about one folder: where it sits in its account's hierarchy, and how much of it is stored here.</summary>
/// <param name="Account">The account the folder belongs to.</param>
/// <param name="Id">The folder's MailFathom-issued identity.</param>
/// <param name="Alias">The alias configuration names the folder by, or <see langword="null" /> for a folder no declaration names.</param>
/// <param name="Role">The role the folder plays for its account, or <see langword="null" /> for none.</param>
/// <param name="HierarchyLevels">The folder names from the top of the account's hierarchy down to this one.</param>
/// <param name="StoredEmailCount">How many of the folder's emails this deployment holds and would serve.</param>
/// <param name="UnreadEmailCount">How many of those are unread.</param>
/// <remarks>
/// <para>
/// One shape for every account in every custody phase: on a mirrored account the folder is synchronization's projection
/// of the source folder, and on a held one it is the folder people edit, but either way it is a row with an identity,
/// a place, and a role, and a reader asks nothing about which.
/// </para>
/// <para>
/// Both counts are of the local copy rather than of a mailbox. A folder the deployment is still backfilling holds fewer
/// than the server does, which is why nothing reading this may present it as the mailbox's own figure without the
/// folder's freshness beside it. The unread count is read from the flag snapshot and never written back towards a
/// server: counting what is unread sets nothing.
/// </para>
/// </remarks>
public sealed record StoredMailFolder(
    MailAccountId Account,
    MailFolderId Id,
    MailFolderAlias? Alias,
    MailFolderSpecialUse? Role,
    IReadOnlyList<string> HierarchyLevels,
    int StoredEmailCount,
    int UnreadEmailCount);
