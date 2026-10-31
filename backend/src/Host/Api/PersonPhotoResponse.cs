// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Photos;
using MailFathom.Domain.Emails;

namespace MailFathom.Host.Api;

/// <summary>Which photo a person a response draws is drawn by, and the route the picture is read at.</summary>
/// <param name="Book">Which route reads it: <c>Own</c> for a contact of the reader's own book, read at its photo route, or <c>Member</c> for a colleague, read at theirs.</param>
/// <param name="Id">The contact or the colleague's user the photo belongs to, which is what that route names.</param>
/// <param name="Photo">An identifier that changes whenever the photo does, so a client reads the picture again only when this does.</param>
/// <remarks>
/// <para>
/// A reference rather than the picture, so a page naming fifty senders carries fifty of these and a client reads each
/// picture it has not drawn yet once, revalidating against its entity tag. The two book names are the ones a contact
/// and a colleague already carry, so a client reads a sender's photo exactly as it reads that person's on the People
/// screen.
/// </para>
/// <para>
/// Absent is a person drawn by their initials: an address no book of the reader's holds, one only a collected or an
/// organization's contact holds, one the reader's own contact holds without a photo, a colleague with no portrait, and
/// every address for a caller who may not read the books.
/// </para>
/// </remarks>
internal sealed record PersonPhotoResponse(string Book, Guid Id, Guid Photo)
{
    /// <summary>The name a contact of the reader's own book carries as its book.</summary>
    private const string OwnBook = "Own";

    /// <summary>Reads the addresses a set of written addresses names, leaving out every one that names no mailbox.</summary>
    /// <param name="written">The addresses as the responses carry them, in any order and with repeats.</param>
    /// <returns>The distinct addresses, in the order first written.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="written" /> is <see langword="null" />.</exception>
    internal static EmailAddress[] AddressesOf(IEnumerable<string?> written)
    {
        ArgumentNullException.ThrowIfNull(written);

        return [.. written.Select(Parsed).OfType<EmailAddress>().Distinct()];
    }

    /// <summary>Describes the photo the person at one written address is drawn by, or nothing where it is their initials.</summary>
    /// <param name="written">The address as the response carries it, or <see langword="null" /> where it carries none.</param>
    /// <param name="photos">What the resolution answered for the read the response describes.</param>
    /// <returns>The reference, or <see langword="null" /> where nothing names a photo for that address.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="photos" /> is <see langword="null" />.</exception>
    internal static PersonPhotoResponse? For(string? written, IReadOnlyDictionary<EmailAddress, PersonPhoto> photos) =>
        For(Parsed(written), photos);

    /// <summary>Describes the photo the person at one address is drawn by, or nothing where it is their initials.</summary>
    /// <param name="address">The address, or <see langword="null" /> where the response names none.</param>
    /// <param name="photos">What the resolution answered for the read the response describes.</param>
    /// <returns>The reference, or <see langword="null" /> where nothing names a photo for that address.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="photos" /> is <see langword="null" />.</exception>
    internal static PersonPhotoResponse? For(EmailAddress? address, IReadOnlyDictionary<EmailAddress, PersonPhoto> photos)
    {
        ArgumentNullException.ThrowIfNull(photos);

        return address is { } named && photos.TryGetValue(named, out var photo)
            ? new PersonPhotoResponse(
                photo.Source == PersonPhotoSource.Member ? ColleagueResponse.MemberBook : OwnBook,
                photo.Holder,
                photo.Photo)
            : null;
    }

    private static EmailAddress? Parsed(string? written) =>
        EmailAddress.TryCreate(displayName: null, written, out var address) ? address : null;
}
