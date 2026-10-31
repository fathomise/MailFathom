// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Application.Contacts;

/// <summary>Answers whether a contact a member names is one their organization keeps, which no member may change.</summary>
/// <remarks>
/// Asked by every act a member performs on one contact before the act reads its own book, so a contact found only in the
/// organization's book is refused as read-only or copied rather than answered as one the member does not hold. A
/// contact's identity is filed in exactly one book, so a contact of the member's own book never answers here.
/// </remarks>
internal static class OrganizationContacts
{
    /// <summary>Reads the contact of the scope's organization book carrying an identity.</summary>
    /// <param name="directory">Reads the organization's book.</param>
    /// <param name="scope">The books the member reads.</param>
    /// <param name="contactId">The contact the member named.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The organization's contact, or <see langword="null" /> where the member belongs to no organization or its book holds no such contact.</returns>
    internal static async Task<Contact?> FindAsync(
        IContactDirectory directory,
        ContactBookScope scope,
        ContactId contactId,
        CancellationToken cancellationToken) =>
        scope.OrganizationBook?.Organization is { } organization
            ? await directory.FindAsync(ContactBookScope.OfOrganization(organization, []), contactId, cancellationToken)
            : null;
}
