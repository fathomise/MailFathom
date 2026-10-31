// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Contacts.Configurations;

/// <summary>Declares the phone numbers one person uses, keyed by their place in the person's order.</summary>
/// <remarks>
/// The row cascades from its contact, so the erasure that takes a person takes their numbers in the same statement.
/// No concurrency token of its own, for the reason an address row carries none: a number is only written by an
/// amendment of its contact, in the same batch as that contact's own tokened update.
/// </remarks>
internal sealed class ContactPhoneNumberConfiguration : IEntityTypeConfiguration<ContactPhoneNumberEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContactPhoneNumberEntity> entity)
    {
        entity.ToTable("contact_phone_numbers");
        entity.HasKey(phoneNumber => new { phoneNumber.ContactId, phoneNumber.Position })
            .HasName(PersistenceConstraintNames.ContactPhoneNumberPrimaryKeyConstraintName);
        entity.Property(phoneNumber => phoneNumber.Number)
            .HasMaxLength(ContactPhoneNumberEntity.MaximumNumberLength)
            .IsRequired();
        entity.Property(phoneNumber => phoneNumber.Label).HasConversion<string>().HasMaxLength(16).IsRequired();

        entity.HasOne<ContactEntity>()
            .WithMany(contact => contact.PhoneNumbers)
            .HasForeignKey(phoneNumber => phoneNumber.ContactId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
