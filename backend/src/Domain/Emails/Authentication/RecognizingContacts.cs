// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Contacts;

namespace MailFathom.Domain.Emails.Authentication;

/// <summary>The contacts that hold a message's author's address and may make that author trusted.</summary>
/// <param name="OrganizationContact">The contact of the receiving account's organization holding the address, if any does.</param>
/// <param name="PersonalContact">A contact holding the address in the book of a user the receiving account is assigned to, if any does.</param>
/// <remarks>
/// Only a contact whose address a person or an administrator wrote can be named here. An address collection recorded,
/// one an agent wrote, and one recorded before the writer was, never make anybody trusted, so whoever looks the books up
/// leaves them out rather than this type or the policy having to tell them apart.
/// </remarks>
public sealed record RecognizingContacts(ContactId? OrganizationContact, ContactId? PersonalContact)
{
    /// <summary>Gets the answer for an author no contact that grants trust holds.</summary>
    public static RecognizingContacts None { get; } = new(OrganizationContact: null, PersonalContact: null);
}
