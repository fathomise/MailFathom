// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Contacts;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One address a person in a contact book uses.</summary>
/// <remarks>
/// <para>
/// The comparison form is unique within one book rather than within one contact, which is the rule that keeps a book
/// from holding one person twice: whoever claims an address second is refused by the database rather than by a check
/// that two callers could both pass. Across books it is not unique at all, because a user's own book and the collected
/// book of each mailbox they read may each hold a record of one person — which the read resolves by showing the first
/// of them rather than by refusing the write.
/// </para>
/// <para>
/// The book is repeated here rather than reached through the contact, because a unique index spans one table and the
/// rule it holds is about the book and the address together. That the value agrees with the contact's own is
/// structural rather than remembered: the foreign key onto the contact carries both columns, so a row can only name the
/// book its contact is filed under.
/// </para>
/// <para>
/// The writer columns record who stated this address and when, which decides whether it can make its sender trusted.
/// They are written once, when the address joins the contact, and an amendment that keeps the address keeps them. A
/// row stored before they were recorded carries <see cref="ContactAddressWriterKind.Unrecorded" /> and no time.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactAddressEntity
{
    /// <summary>The longest address stored, which is the bound the domain already refuses to exceed.</summary>
    internal const int MaximumAddressLength = Contact.MaximumAddressLength;

    public Guid Id { get; set; }

    /// <summary>Gets or sets the contact this address belongs to.</summary>
    public Guid ContactId { get; set; }

    /// <summary>Gets or sets the book the contact this address belongs to is in.</summary>
    public required string BookHolderId { get; set; }

    /// <summary>Gets or sets the address as it was written, which is what a reader is shown.</summary>
    public required string Address { get; set; }

    /// <summary>Gets or sets the comparison form, which is what anything matches, groups, or indexes on.</summary>
    public required string NormalizedAddress { get; set; }

    public ContactAddressWriterKind WriterKind { get; set; }

    public Guid? WriterUserId { get; set; }

    public Guid? WriterCredentialId { get; set; }

    public DateTimeOffset? WrittenAt { get; set; }
}
