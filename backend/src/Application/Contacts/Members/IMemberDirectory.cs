// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Contacts.Members;

/// <summary>Reads an organization's member directory: every person in it, as a contact derived from their user record.</summary>
/// <remarks>
/// <para>
/// A projection, never a copy: every read composes the members from the user rows, the documents beside them, and the
/// mail accounts assigned to each, as those stand at that moment on every replica. Nothing writes the directory, so a
/// member who leaves the organization, whose user is erased, or who becomes a service account is absent from the next
/// read.
/// </para>
/// <para>
/// A member's addresses are those of the mail accounts in the same organization assigned to them and to nobody else,
/// because a mailbox several users are assigned is never one person's address.
/// </para>
/// <para>
/// The read answers about the organization rather than about a reader, so it leaves nobody out: a reader is left out of
/// their own directory by whoever reads it for them.
/// </para>
/// </remarks>
public interface IMemberDirectory
{
    /// <summary>The greatest number of members one read composes.</summary>
    /// <remarks>
    /// A ceiling rather than a page, because every read of the colleagues composes the whole directory to search and
    /// order it in process. An organization past it is answered with the first members by identifier and the rest are
    /// absent from the directory; one that grows that large earns a directory searched and paged in the database,
    /// with an index on the user's name, instead of in process.
    /// </remarks>
    const int MaximumMemberCount = 1_000;

    /// <summary>Reads every person in one organization.</summary>
    /// <param name="organization">The organization whose members are read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every member whose type is a person, at most <see cref="MaximumMemberCount" /> of them, in no particular order; empty for an organization this deployment does not hold.</returns>
    Task<IReadOnlyList<MemberContact>> ReadAsync(Guid organization, CancellationToken cancellationToken);

    /// <summary>Reads one person of one organization, composing nobody else.</summary>
    /// <param name="organization">The organization the member belongs to.</param>
    /// <param name="member">The member's user.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The member, or <see langword="null" /> where they belong to another organization or none, are not a person, or are not held at all.</returns>
    Task<MemberContact?> FindAsync(Guid organization, UserId member, CancellationToken cancellationToken);
}
