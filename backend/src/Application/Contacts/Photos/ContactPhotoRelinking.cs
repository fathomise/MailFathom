// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.StoredFiles;

namespace MailFathom.Application.Contacts.Photos;

/// <summary>What linking a contact to a photo did.</summary>
/// <param name="ContactHeld">Whether the user's own book holds the contact; nothing was written when it does not.</param>
/// <param name="Replaced">The file the contact linked as its photo before the write, which the caller removes.</param>
public sealed record ContactPhotoRelinking(bool ContactHeld, StoredFileId? Replaced)
{
    /// <summary>Gets the answer for a contact the user's own book does not hold.</summary>
    public static ContactPhotoRelinking NoSuchContact { get; } = new(ContactHeld: false, Replaced: null);
}
