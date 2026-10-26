// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam;

/// <summary>The little a classification needs to know about the occurrence it is about, besides the content.</summary>
/// <param name="Id">The stable local identifier of the occurrence.</param>
/// <param name="AccountId">The account the occurrence was stored from.</param>
/// <param name="FolderAlias">MailFathom's own name for the folder it was stored from.</param>
/// <param name="CurrentFolderAlias">
/// The alias of the folder the message is in now, or <see langword="null" /> for a folder the account created and nobody
/// mapped. It differs from <paramref name="FolderAlias" /> where a held or restoring account moved the message locally.
/// </param>
/// <param name="Keywords">The keywords the message carries in its mailbox, which is where a junk marking made in any client is read from.</param>
/// <remarks>
/// The account, the folders, and the keywords are here because the decisions resting on them are not in the content:
/// whether the configured scope covers the folder the message was stored from, whether the folder it is in now is the
/// one the account advertises for junk, and whether somebody marked the message junk or not junk. Nothing else about the occurrence is needed, which is why this
/// carries no subject, no participant, and no size.
/// </remarks>
public sealed record ClassifiableEmail(
    StoredEmailId Id,
    MailAccountId AccountId,
    MailFolderAlias FolderAlias,
    MailFolderAlias? CurrentFolderAlias,
    RemoteEmailKeywords Keywords);
