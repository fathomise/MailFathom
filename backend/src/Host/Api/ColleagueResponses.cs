// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Contacts.Members;

namespace MailFathom.Host.Api;

/// <summary>One person of the reader's organization, as the member directory composes them from their user record.</summary>
/// <param name="Id">The member's user identity, which is also what promoting them and reading their portrait name.</param>
/// <param name="Book">Always <c>Member</c>, so a client drawing several books side by side tells this one apart without knowing which route it read.</param>
/// <param name="DisplayName">The label the member's user carries.</param>
/// <param name="Addresses">The addresses of the mail accounts that are the member's alone, the preferred one first; empty for a member with no mail account.</param>
/// <param name="PreferredAddress">The address to write to them at, or nothing exactly when they have none.</param>
/// <param name="PhoneNumbers">The phone numbers the member's record states, the preferred one first.</param>
/// <param name="JobTitle">The title an administrator gave them, or nothing where none was stated.</param>
/// <param name="OrganizationName">The name of the organization they belong to.</param>
/// <param name="TimeZone">The IANA zone the member's record states, or nothing where it states none.</param>
/// <param name="Photo">An identifier that changes whenever the member's portrait does, or nothing where they have none.</param>
/// <remarks>
/// Personal data about a colleague. It travels to the member of the same organization who asked and reaches nothing
/// else — no log line, no metric dimension, and no failure message.
/// </remarks>
internal sealed record ColleagueResponse(
    Guid Id,
    string Book,
    string DisplayName,
    IReadOnlyList<string> Addresses,
    string? PreferredAddress,
    IReadOnlyList<ContactPhoneNumberResponse> PhoneNumbers,
    string? JobTitle,
    string OrganizationName,
    string? TimeZone,
    Guid? Photo)
{
    /// <summary>The name every colleague's <see cref="Book" /> carries.</summary>
    internal const string MemberBook = "Member";

    /// <summary>Describes one member for a colleague.</summary>
    /// <param name="member">The member as the directory composed them.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="member" /> is <see langword="null" />.</exception>
    internal static ColleagueResponse For(MemberContact member)
    {
        ArgumentNullException.ThrowIfNull(member);

        IReadOnlyList<string> addresses = member.PreferredAddress is { } preferred
            ? [preferred.Address, .. member.Addresses.Where(address => address != preferred).Select(address => address.Address)]
            : [];

        return new ColleagueResponse(
            member.Member.Value,
            MemberBook,
            member.DisplayName.Value,
            addresses,
            member.PreferredAddress?.Address,
            [.. member.PhoneNumbers.Select(phoneNumber =>
                new ContactPhoneNumberResponse(phoneNumber.Number, phoneNumber.Label.ToString()))],
            member.JobTitle,
            member.OrganizationName,
            member.TimeZone?.Id,
            member.Portrait?.Value);
    }
}

/// <summary>One bounded page of the reader's colleagues, and where the walk continues.</summary>
/// <param name="Contacts">The colleagues this page holds, ordered by name and then by identity.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or nothing at the end of the directory.</param>
internal sealed record ColleaguePageResponse(IReadOnlyList<ColleagueResponse> Contacts, string? NextCursor);
