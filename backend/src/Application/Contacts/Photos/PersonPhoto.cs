// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Application.Contacts.Photos;

/// <summary>Where the photo a person is drawn by is read from.</summary>
public enum PersonPhotoSource
{
    /// <summary>A contact of the reader's own book, whose photo the reader gave them.</summary>
    OwnContact = 0,

    /// <summary>A member of the reader's organization, drawn by the portrait their own record holds.</summary>
    Member = 1,
}

/// <summary>The photo one address is drawn by: whose it is and which stored file it is, never the picture itself.</summary>
/// <param name="Source">Which of the two places the photo is read from.</param>
/// <param name="Holder">The contact or the member's user the photo belongs to, which is what the route reading it names.</param>
/// <param name="Photo">The stored file the photo is, which changes whenever the photo does.</param>
/// <remarks>
/// A reference rather than the octets, so a list naming fifty senders carries fifty identifiers and a client reads each
/// picture once, revalidating it against its entity tag, rather than receiving every picture on every page.
/// </remarks>
public sealed record PersonPhoto(PersonPhotoSource Source, Guid Holder, Guid Photo);
