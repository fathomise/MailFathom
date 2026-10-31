// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Reads what an organization's member directory is composed from: its people, their records, and the addresses that are each one's own.</summary>
/// <remarks>
/// The rows rather than the contacts, because what a member's record states is read through the settings policies
/// governing them, which the configuration layer resolves. Everything a member contact needs comes back over one
/// connection but not from one snapshot: each statement reads what was committed when it ran, so a member retyped, a
/// mailbox reassigned, or a policy changed between two of them is read half before and half after, and the next read
/// answers with the change whole.
/// </remarks>
public interface IOrganizationMemberReader
{
    /// <summary>Reads the people of one organization.</summary>
    /// <param name="organization">The organization whose members are read.</param>
    /// <param name="limit">The greatest number of members to read, taken in identifier order.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organization's name, the policies governing its members, and every member whose type is a person; <see langword="null" /> for an organization this deployment does not hold.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="limit" /> is not positive.</exception>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the database declined the read.</exception>
    Task<OrganizationMembers?> ReadAsync(Guid organization, int limit, CancellationToken cancellationToken);

    /// <summary>Reads one person of one organization, under the rules <see cref="ReadAsync" /> reads every one of them by.</summary>
    /// <param name="organization">The organization the member belongs to.</param>
    /// <param name="member">The member's user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organization's name, the policies governing its members, and the member where they belong to it and their type is a person, otherwise no member; <see langword="null" /> for an organization this deployment does not hold.</returns>
    /// <exception cref="UserSettingsUnreadableException">Thrown when the database declined the read.</exception>
    Task<OrganizationMembers?> ReadMemberAsync(Guid organization, UserId member, CancellationToken cancellationToken);
}
