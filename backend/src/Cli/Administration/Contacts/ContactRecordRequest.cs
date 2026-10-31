// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Cli.Administration.Contacts;

/// <summary>The record the command asks a deployment to hold for one person.</summary>
/// <param name="DisplayName">The name to record.</param>
/// <param name="Addresses">Every address the person uses.</param>
/// <param name="PreferredAddress">The address to use by default, which is one of <paramref name="Addresses" />.</param>
/// <param name="Note">What the user wrote about the person, or <see langword="null" /> to hold no note.</param>
/// <param name="PhoneNumbers">The person's phone numbers, the preferred one first, or <see langword="null" /> to leave the ones an amended contact holds as they are.</param>
/// <remarks>
/// One shape for recording a person and for correcting one, because an amendment states the whole record rather than
/// the difference from the one held. Which of the two is meant is the route and the verb, never a field. The phone
/// numbers are the one part an amendment may leave out, which is what the commands that change an address or a name do.
/// </remarks>
internal sealed record ContactRecordRequest(
    [property: JsonPropertyName("displayName")] string DisplayName,
    [property: JsonPropertyName("addresses")] IReadOnlyList<string> Addresses,
    [property: JsonPropertyName("preferredAddress")] string PreferredAddress,
    [property: JsonPropertyName("note")] string? Note,
    [property: JsonPropertyName("phoneNumbers")] IReadOnlyList<ContactPhoneNumberRecord>? PhoneNumbers = null);

/// <summary>One phone number of a person, as sent to and read from a deployment.</summary>
/// <param name="Number">The number as written.</param>
/// <param name="Label">Which of the person's phones it reaches: <c>Mobile</c>, <c>Work</c>, <c>Home</c>, or <c>Other</c>.</param>
internal sealed record ContactPhoneNumberRecord(
    [property: JsonPropertyName("number")] string? Number,
    [property: JsonPropertyName("label")] string? Label);
