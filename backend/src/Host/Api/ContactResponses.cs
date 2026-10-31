// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;
using MailFathom.Domain.Contacts;

namespace MailFathom.Host.Api;

/// <summary>The record a caller states for a contact, whether it is being written for the first time or amended.</summary>
/// <param name="DisplayName">The name to record for the person.</param>
/// <param name="Addresses">Every address the person uses; two spellings of one address count once.</param>
/// <param name="PreferredAddress">The address to use by default, which must be one of <paramref name="Addresses" />.</param>
/// <param name="Note">What the user wrote about the person, or nothing to hold no note.</param>
/// <param name="PhoneNumbers">The person's phone numbers, the preferred one first; absent holds none on a new record and keeps the held ones on an amendment, and an empty list clears them.</param>
/// <remarks>
/// One request shape for both operations, because an amendment states the whole record rather than the difference from
/// the one held: what a caller sends to create a contact and what it sends to correct one are the same things, and a
/// second record differing in nothing would be two contracts to keep in agreement. Which operation is meant is the route
/// and the verb, never a field. The phone numbers are the one part an amendment may leave out.
/// </remarks>
internal sealed record ContactRecordRequest(
    string? DisplayName,
    IReadOnlyList<string>? Addresses,
    string? PreferredAddress,
    string? Note,
    IReadOnlyList<ContactPhoneNumberDraft>? PhoneNumbers = null);

/// <summary>One phone number of a person as the book holds it.</summary>
/// <param name="Number">The number as the user wrote it.</param>
/// <param name="Label">Which of the person's phones it reaches: <c>Mobile</c>, <c>Work</c>, <c>Home</c>, or <c>Other</c>.</param>
internal sealed record ContactPhoneNumberResponse(string Number, string Label);

/// <summary>One person as the book holds them.</summary>
/// <param name="Id">The identity the book gave them, which no amendment and no promotion changes.</param>
/// <param name="DisplayName">The name the user recorded, in the casing they wrote it.</param>
/// <param name="Addresses">Every address they use, the preferred one first and the rest in comparison order.</param>
/// <param name="PreferredAddress">The address to use when something addresses them without naming which of theirs.</param>
/// <param name="Note">What the user wrote about them, or nothing where they wrote nothing.</param>
/// <param name="Origin">How the contact came to be in the book, which decides who may amend it.</param>
/// <param name="Book">Which of the reader's books holds the contact: <c>Own</c>, <c>Organization</c>, or <c>Collected</c>.</param>
/// <param name="RecordedAt">When the contact entered the book.</param>
/// <param name="AmendedAt">When it was last amended, which equals <paramref name="RecordedAt" /> until one happens.</param>
/// <param name="Organization">The organization whose book holds the contact, or nothing where a person or a mailbox holds it. A member reads such a contact and cannot change it.</param>
/// <param name="Photo">The stored photo the user supplied for the contact, or nothing where there is none. It changes whenever the photo does, so a client reads the photo again only when this does.</param>
/// <param name="PhoneNumbers">Every phone number the user wrote down for them, the preferred one first; none for a collected contact.</param>
/// <remarks>
/// Every field but the identity, the origin, the organization, and the photo's identifier is personal data about a third party. It travels to
/// the caller that asked for this person and reaches nothing else — no log line, no metric dimension, and no failure
/// message.
/// </remarks>
internal sealed record ContactResponse(
    Guid Id,
    string DisplayName,
    IReadOnlyList<string> Addresses,
    string PreferredAddress,
    string? Note,
    string Origin,
    string Book,
    DateTimeOffset RecordedAt,
    DateTimeOffset AmendedAt,
    Guid? Organization,
    Guid? Photo,
    IReadOnlyList<ContactPhoneNumberResponse> PhoneNumbers)
{
    /// <summary>Describes one contact for a caller.</summary>
    /// <param name="contact">The contact as the book holds it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="contact" /> is <see langword="null" />.</exception>
    internal static ContactResponse For(Contact contact)
    {
        ArgumentNullException.ThrowIfNull(contact);

        return new ContactResponse(
            contact.Id.Value,
            contact.DisplayName.Value,
            [.. contact.Addresses.Select(address => address.Address)],
            contact.PreferredAddress.Address,
            contact.Note?.Value,
            contact.Origin.ToString(),
            BookOf(contact),
            contact.RecordedAt,
            contact.AmendedAt,
            contact.Organization,
            contact.Photo,
            [.. contact.PhoneNumbers.Select(phoneNumber =>
                new ContactPhoneNumberResponse(phoneNumber.Number, phoneNumber.Label.ToString()))]);
    }

    /// <summary>Names the book a contact is drawn under, from its origin and the organization keeping it.</summary>
    private static string BookOf(Contact contact) => contact switch
    {
        { Origin: ContactOrigin.Collected } => "Collected",
        { Organization: not null } => "Organization",
        _ => "Own",
    };
}

/// <summary>The person a lookup found, or that the book holds none.</summary>
/// <param name="Contact">The contact, or nothing when the book holds none matching what was asked.</param>
/// <remarks>
/// A book holding nobody of that identity or address is an outcome rather than a refusal, so the answer carries no
/// contact instead of a <c>404</c>: the caller asked a question this deployment can answer, and the answer is that
/// nobody is recorded. It also keeps <c>404</c> meaning what every client already reads it as on this surface — that
/// the port serves no administrative endpoint.
/// </remarks>
internal sealed record ContactLookupResponse(ContactResponse? Contact);

/// <summary>One bounded page of the book, and where the walk continues.</summary>
/// <param name="Contacts">The contacts this page holds, ordered by the name's comparison form and then by identity.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or nothing at the end of the book.</param>
/// <remarks>
/// The absent cursor is the end of the walk rather than a page that happened to be short, so a caller stops when the
/// cursor stops instead of comparing the count against the size it asked for.
/// </remarks>
internal sealed record ContactPageResponse(IReadOnlyList<ContactResponse> Contacts, string? NextCursor);

/// <summary>What one write to the book produced.</summary>
/// <param name="Outcome">How the write ended, by the name the application's own outcome carries.</param>
/// <param name="Contact">The record the caller itself stated, present exactly when that write was performed.</param>
/// <param name="AddressHolder">The contact already holding an address the write claimed, present exactly when that is what refused it.</param>
/// <remarks>
/// <para>
/// A refusal is answered with <c>200</c> and a named outcome rather than a status code, because each one is something
/// the caller reports to its user and continues from rather than a request that was malformed. Only the holder's
/// identity is named: answering with somebody else's record would hand a third party out as a side effect of a refused
/// write.
/// </para>
/// <para>
/// What decides whether a record comes back is whether the caller stated one. Writing the book and reading it are
/// separate grants on both surfaces this shape is answered from — <c>mailfathom.admin.operate</c> beside
/// <c>mailfathom.admin.audit.read</c> for an operator, and <c>mailfathom.contacts.write</c> beside
/// <c>mailfathom.contacts.read</c> for the signed-in user — so a body carrying a record the caller never sent is a
/// read served to a grant that does not admit one. Recording and amending state the
/// whole record, so <see cref="For" /> hands the written form of it back; a promotion states an identity and nothing
/// else, so <see cref="OutcomeOf" /> is what answers it. A refusal carries no record on either path, including the two
/// the book could only reach by reading one — a write the contact's origin refuses, and a promotion of somebody already
/// asserted — because a refused write is where that read would be least visible.
/// </para>
/// </remarks>
internal sealed record ContactWriteResponse(string Outcome, ContactResponse? Contact, Guid? AddressHolder)
{
    /// <summary>Describes what a write whose record the caller stated produced.</summary>
    /// <param name="result">The outcome the book answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    /// <remarks>The written record is the caller's own request as the book settled it, which is what makes reading it back an answer about the request rather than about the book.</remarks>
    internal static ContactWriteResponse For(ContactWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ContactWriteResponse(
            result.Outcome.ToString(),
            result is { Outcome: ContactWriteOutcome.Written, Contact: { } written } ? ContactResponse.For(written) : null,
            result.AddressHolder?.Value);
    }

    /// <summary>Describes what a write the caller stated no record for produced, which is the outcome and nothing else.</summary>
    /// <param name="result">The outcome the book answered with.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="result" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A promotion names one person and changes their origin, so a record in the answer would be the book's own
    /// contents rather than the caller's request — the whole of what the reading grant publishes, obtained under the
    /// writing one from an identity alone. The caller learns that the promotion happened and reads the person through
    /// the route that is published for reading them.
    /// </remarks>
    internal static ContactWriteResponse OutcomeOf(ContactWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new ContactWriteResponse(result.Outcome.ToString(), Contact: null, AddressHolder: null);
    }
}

/// <summary>What erasing one contact removed.</summary>
/// <param name="Contact">The contact the erasure was asked for.</param>
/// <param name="WasHeld">Whether the book held that contact when the erasure ran.</param>
/// <param name="AddressesErased">How many addresses went with them.</param>
/// <remarks>
/// The counts are what a user is entitled to rather than a courtesy, and they are the whole of what an erasure says
/// about a person: that they are gone, and how much went. No name, address, or note is in this answer, deliberately —
/// a report of an erasure that echoed the record would be a copy of what was just removed.
/// </remarks>
internal sealed record ContactErasureResponse(Guid Contact, bool WasHeld, int AddressesErased);

/// <summary>What erasing the collected half of the book removed.</summary>
/// <param name="ContactsErased">How many contacts of the collected origin went.</param>
/// <param name="AddressesErased">How many addresses went with them.</param>
/// <remarks>
/// Two counts and nobody's identity, for the reason the single erasure above carries none: what a disposal reports is
/// how much of a record about other people this deployment had built and no longer has. A book that had collected
/// nobody answers with two zeroes rather than with a failure, because the state the user asked for is the state the
/// book is in.
/// </remarks>
internal sealed record CollectedContactErasureResponse(int ContactsErased, int AddressesErased);

/// <summary>Everything the deployment holds about one person, as of the instant it was taken.</summary>
/// <param name="Contact">The complete record, or nothing when the book holds no such contact.</param>
/// <param name="ProducedAt">When the export was produced, absent together with the contact.</param>
/// <param name="Photo">The photo the user supplied for the person, or nothing where there is none.</param>
/// <remarks>
/// The data-subject access path. It carries the same complete record every other surface over the book renders, and the
/// photo itself rather than a pointer to it, because which parts of a person are handed back is not a decision any
/// surface gets to make.
/// </remarks>
internal sealed record ContactExportResponse(ContactResponse? Contact, DateTimeOffset? ProducedAt, ContactPhotoResponse? Photo)
{
    /// <summary>Gets the answer for a book that holds no such person.</summary>
    internal static ContactExportResponse None { get; } = new(Contact: null, ProducedAt: null, Photo: null);

    /// <summary>Describes one export for a caller.</summary>
    /// <param name="export">The export, or <see langword="null" /> where the book holds no such person.</param>
    /// <returns>The response body.</returns>
    internal static ContactExportResponse For(ContactExport? export) =>
        export is null
            ? None
            : new ContactExportResponse(
                ContactResponse.For(export.Contact),
                export.ProducedAt,
                export.Photo is { } photo ? new ContactPhotoResponse(photo.Type.MediaType, photo.Content) : null);
}

/// <summary>A contact's photo as an export hands it back.</summary>
/// <param name="MediaType">The kind of image the octets are, proven from their signature when they were stored.</param>
/// <param name="Content">The octets the user supplied, unchanged, which JSON carries as base64.</param>
internal sealed record ContactPhotoResponse(string MediaType, ReadOnlyMemory<byte> Content);
