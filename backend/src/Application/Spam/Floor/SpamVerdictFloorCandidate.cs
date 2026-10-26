// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;

namespace MailFathom.Application.Spam.Floor;

/// <summary>A stored message the spam floor may have to raise, with the two facts the floor reads about it.</summary>
/// <param name="Id">The stored message.</param>
/// <param name="FolderAlias">The alias of the folder it sits in, or <see langword="null" /> for a folder no mapping names, which is never a junk folder.</param>
/// <param name="Keywords">The keywords stored for it.</param>
/// <remarks>The folder is the one the message is in now, in every custody phase, so a held message is read where the person put it.</remarks>
public sealed record SpamVerdictFloorCandidate(
    StoredEmailId Id,
    MailFolderAlias? FolderAlias,
    RemoteEmailKeywords Keywords);
