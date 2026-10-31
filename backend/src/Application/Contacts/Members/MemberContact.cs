// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.StoredFiles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Contacts;
using MailFathom.Domain.Emails;

namespace MailFathom.Application.Contacts.Members;

/// <summary>One member of an organization as the other members read them: a contact derived from their user record when it is read, and held nowhere.</summary>
/// <param name="Member">The member's user, which is the contact's identity within the directory.</param>
/// <param name="DisplayName">The label the member's user carries.</param>
/// <param name="Addresses">The addresses of the mail accounts assigned to the member alone, in the ordinal order of the accounts' identifiers; empty for a member with no such account.</param>
/// <param name="PreferredAddress">The address to use by default, which is one of <paramref name="Addresses" />, or <see langword="null" /> exactly when that is empty.</param>
/// <param name="PhoneNumbers">The phone numbers the member's record states, the preferred one first.</param>
/// <param name="JobTitle">The title an administrator gave the member, or <see langword="null" /> for none.</param>
/// <param name="OrganizationName">The name of the organization the member belongs to.</param>
/// <param name="Portrait">The stored file the member is drawn by, or <see langword="null" /> for none.</param>
/// <param name="TimeZone">The zone the member's record states, or <see langword="null" /> where it states none.</param>
/// <remarks>
/// <para>
/// <see href="https://github.com/fathomise/MailFathom/blob/main/docs/decisions/0044-an-organizations-member-directory-as-a-projection-of-user-records.md">ADR 0044</see>
/// decides the shape: a projection rather than a copy, so a member who leaves the organization, is erased, or becomes a
/// service account is absent from the next read with nothing written for it.
/// </para>
/// <para>
/// Every field but the identity is personal data about a colleague, and none of it reaches a log line, a metric
/// dimension, or a failure message.
/// </para>
/// </remarks>
public sealed record MemberContact(
    UserId Member,
    ContactDisplayName DisplayName,
    IReadOnlyList<EmailAddress> Addresses,
    EmailAddress? PreferredAddress,
    IReadOnlyList<ContactPhoneNumber> PhoneNumbers,
    string? JobTitle,
    string OrganizationName,
    StoredFileId? Portrait,
    UserTimeZone? TimeZone)
{
    /// <summary>Resolves which of a member's current addresses answers as their preferred one.</summary>
    /// <param name="addresses">The member's current addresses, in the ordinal order of their accounts' identifiers.</param>
    /// <param name="stated">The preferred address the member's record states, or <see langword="null" /> where it states none.</param>
    /// <returns>The stated address while it is one of <paramref name="addresses" />, otherwise the first of them, and <see langword="null" /> where there is none.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="addresses" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// Resolved on every read rather than kept in step, so unassigning the account that held the choice, sharing it, or
    /// moving it out of the organization needs nothing written to the record: the next read answers from the addresses
    /// as they then stand, and a choice that is out of the set stays stated and answers again if its address comes back.
    /// The address returned is the account's own spelling rather than the record's.
    /// </remarks>
    public static EmailAddress? PreferredAddressAmong(IReadOnlyList<EmailAddress> addresses, string? stated)
    {
        ArgumentNullException.ThrowIfNull(addresses);

        if (EmailAddress.TryCreate(displayName: null, stated, out var chosen) && addresses.Contains(chosen))
        {
            return addresses.First(address => address.Equals(chosen));
        }

        return addresses.Count > 0 ? addresses[0] : null;
    }
}
