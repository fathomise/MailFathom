// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Contacts.Members;
using MailFathom.Domain.Access;

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
/// <param name="WorkingHours">When the member works, on their own wall clock and so read in <paramref name="TimeZone" />, or nothing where none is stated.</param>
/// <param name="Photo">An identifier that changes whenever the member's portrait does, or nothing where they have none.</param>
/// <param name="Presence">How the member reads to the other members: <c>Active</c>, <c>Away</c>, <c>Busy</c>, <c>DoNotDisturb</c>, or <c>Offline</c>, which is also what a member who chose to hide reads as.</param>
/// <param name="LastSeen">When the member was last present, only where they read as <c>Offline</c> and did not choose to hide.</param>
/// <param name="OutOfOfficeUntil">When the member's automatic reply stops answering, while it is switched on and in its period, and nothing otherwise — never what it answers with.</param>
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
    WorkingHoursResponse? WorkingHours,
    Guid? Photo,
    string Presence,
    DateTimeOffset? LastSeen,
    DateTimeOffset? OutOfOfficeUntil)
{
    /// <summary>The name every colleague's <see cref="Book" /> carries.</summary>
    internal const string MemberBook = "Member";

    /// <summary>Describes one member for a colleague.</summary>
    /// <param name="colleague">The member as the directory composed them, with their presence and absence.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="colleague" /> is <see langword="null" />.</exception>
    internal static ColleagueResponse For(Colleague colleague)
    {
        ArgumentNullException.ThrowIfNull(colleague);

        var member = colleague.Contact;
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
            member.WorkingHours is { } hours ? WorkingHoursResponse.For(hours) : null,
            member.Portrait?.Value,
            colleague.Presence.State.ToString(),
            colleague.Presence.LastSeen,
            colleague.OutOfOfficeUntil);
    }
}

/// <summary>When a member works: the days, and one range of hours those days share on the member's own wall clock.</summary>
/// <param name="Days">The days worked, each named in English, Monday first.</param>
/// <param name="Start">The time work starts at, written <c>HH:mm</c>.</param>
/// <param name="End">The time work ends at, written <c>HH:mm</c> and after <paramref name="Start" />.</param>
internal sealed record WorkingHoursResponse(IReadOnlyList<string> Days, string Start, string End)
{
    /// <summary>Describes working hours for a colleague.</summary>
    /// <param name="hours">The working hours.</param>
    /// <returns>The response body.</returns>
    internal static WorkingHoursResponse For(WorkingHours hours) =>
        new(
            [.. hours.Days.Select(static day => day.ToString())],
            hours.Start.ToString("HH:mm", CultureInfo.InvariantCulture),
            hours.End.ToString("HH:mm", CultureInfo.InvariantCulture));
}

/// <summary>Where one colleague sits in the reader's organization.</summary>
/// <param name="Managers">The managers above the colleague, from the top of the organization down to the colleague's own.</param>
/// <param name="DirectReports">The members reporting to the colleague, ordered by name.</param>
/// <param name="ReaderId">The reader's own user identity, which either list may hold and which leads to no colleague.</param>
/// <remarks>Personal data about colleagues, travelling as <see cref="ColleagueResponse" /> does and nowhere else.</remarks>
internal sealed record ColleagueStructureResponse(
    IReadOnlyList<ColleagueResponse> Managers,
    IReadOnlyList<ColleagueResponse> DirectReports,
    Guid ReaderId)
{
    /// <summary>Describes one colleague's structure for the reader.</summary>
    /// <param name="structure">The structure as the directory derived it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="structure" /> is <see langword="null" />.</exception>
    internal static ColleagueStructureResponse For(ColleagueStructure structure)
    {
        ArgumentNullException.ThrowIfNull(structure);

        return new ColleagueStructureResponse(
            [.. structure.Managers.Select(ColleagueResponse.For)],
            [.. structure.DirectReports.Select(ColleagueResponse.For)],
            structure.Reader.Value);
    }
}

/// <summary>One bounded page of the reader's colleagues, and where the walk continues.</summary>
/// <param name="Contacts">The colleagues this page holds, ordered by name and then by identity.</param>
/// <param name="NextCursor">The cursor the following page is asked with, or nothing at the end of the directory.</param>
internal sealed record ColleaguePageResponse(IReadOnlyList<ColleagueResponse> Contacts, string? NextCursor);
