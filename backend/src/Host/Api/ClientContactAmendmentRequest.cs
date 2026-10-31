// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts;

namespace MailFathom.Host.Api;

/// <summary>The record a person states for one of their own contacts, and which of the addresses it keeps they state again.</summary>
/// <param name="DisplayName">The name to record for the person.</param>
/// <param name="Addresses">Every address the person uses; two spellings of one address count once.</param>
/// <param name="PreferredAddress">The address to use by default, which must be one of <paramref name="Addresses" />.</param>
/// <param name="Note">What the user wrote about the person, or nothing to hold no note.</param>
/// <param name="RestatedAddresses">Addresses of <paramref name="Addresses" /> the person states again, or nothing to restate none.</param>
/// <param name="PhoneNumbers">The person's phone numbers afterwards, the preferred one first; absent keeps the held ones and an empty list clears them.</param>
/// <remarks>
/// <para>
/// The record a creation states, plus one field only a person's amendment carries. An amendment keeps the writer each
/// kept address has, so an address an agent wrote, or one written before writers were recorded, grants nothing however
/// often its contact is amended; naming it here records the person as its writer, which is what makes it count. That
/// is a client-only field because only the person's own word does that here: the administrative routes write as an
/// administrator through a record of their own, and the tools never restate anything.
/// </para>
/// <para>
/// Every restated address has to be one of the record's addresses, compared in the book's comparison form, and the
/// list is bounded like the addresses themselves; anything else is refused as a bad request.
/// </para>
/// </remarks>
internal sealed record ClientContactAmendmentRequest(
    string? DisplayName,
    IReadOnlyList<string>? Addresses,
    string? PreferredAddress,
    string? Note,
    IReadOnlyList<string>? RestatedAddresses,
    IReadOnlyList<ContactPhoneNumberDraft>? PhoneNumbers = null);
