// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Domain.Contacts;

/// <summary>Who wrote an address into a contact: the surface, the user, and the credential the write was admitted by.</summary>
/// <param name="Kind">The surface the write came through.</param>
/// <param name="User">The user who wrote it, or <see langword="null" /> where none is recorded.</param>
/// <param name="Credential">The credential the write was admitted by, or <see langword="null" /> where none admitted it.</param>
/// <remarks>
/// It is recorded per address rather than per contact, because a contact's addresses can be written by different
/// surfaces: an amendment that keeps an address keeps the writer that address has, and an address the amendment adds
/// takes the amendment's writer. When it was written is the contact's own time of that write. From a trusted message an
/// operator reaches the contact, the address, and this record, which is the audit of who made a sender trusted.
/// </remarks>
public sealed record ContactAddressWriter(ContactAddressWriterKind Kind, UserId? User, Guid? Credential)
{
    /// <summary>Gets the writer an address collection recorded carries, which nobody stated.</summary>
    public static ContactAddressWriter Unrecorded { get; } = new(ContactAddressWriterKind.Unrecorded, User: null, Credential: null);

    /// <summary>Answers whether an address written through one surface can make its sender trusted.</summary>
    /// <param name="kind">The surface the address was written through.</param>
    /// <returns><see langword="true" /> for a person and an administrator; otherwise <see langword="false" />.</returns>
    public static bool GrantsTrustAs(ContactAddressWriterKind kind) =>
        kind is ContactAddressWriterKind.Person or ContactAddressWriterKind.Administrator;
}
