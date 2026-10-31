// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.CodeCoverage;
using MailFathom.Domain.Contacts;

namespace MailFathom.Infrastructure.Persistence.Entities;

/// <summary>One phone number a user wrote down for a person, at its place in the person's own order.</summary>
/// <remarks>
/// <para>
/// A row per number rather than an array column, so erasing the person takes their numbers with them through the
/// foreign key instead of through a statement somebody has to remember to write. <see cref="Position" /> is the order
/// the user gave, whose first entry is the preferred number, and it is part of the key so an amendment rewrites a
/// position in place rather than deleting and inserting a row it would otherwise collide with.
/// </para>
/// <para>
/// The number is personal data about a third party, held under the same terms as the contact it hangs on.
/// </para>
/// </remarks>
[RequiresIntegrationCoverage]
internal sealed class ContactPhoneNumberEntity
{
    /// <summary>The longest number stored, which is the bound the domain value already refuses to exceed.</summary>
    internal const int MaximumNumberLength = ContactPhoneNumber.MaximumLength;

    /// <summary>Gets or sets the contact this number belongs to.</summary>
    public Guid ContactId { get; set; }

    /// <summary>Gets or sets the number's place in the person's order, from zero for the preferred one.</summary>
    public int Position { get; set; }

    /// <summary>Gets or sets the number as the user wrote it.</summary>
    public required string Number { get; set; }

    /// <summary>Gets or sets which of the person's phones the number reaches.</summary>
    public ContactPhoneLabel Label { get; set; }
}
