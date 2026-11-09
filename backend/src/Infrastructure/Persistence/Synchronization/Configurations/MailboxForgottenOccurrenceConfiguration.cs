// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Synchronization.Configurations;

/// <summary>Declares where the server still holds a message a delete took out of MailFathom without touching the server.</summary>
/// <remarks>
/// What it holds is an account, a folder, a UIDVALIDITY, and a UID — the server's own names for a position in a mailbox
/// and no part of the message. It is all that stops the message being mirrored again, so no retention ages it out.
/// </remarks>
internal sealed class MailboxForgottenOccurrenceConfiguration : IEntityTypeConfiguration<MailboxForgottenOccurrenceEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailboxForgottenOccurrenceEntity> entity)
    {
        entity.ToTable("mailbox_forgotten_occurrences");
        entity.HasKey(forgotten => forgotten.Id);
        entity.Property(forgotten => forgotten.Id).ValueGeneratedNever();
        entity.Property(forgotten => forgotten.MailFolderBindingId).HasColumnName(PersistenceConstraintNames.MailFolderBindingColumnName);
        entity.Property(forgotten => forgotten.MailboxAccountId).HasMaxLength(128);

        // Unique on the occurrence, which is also the order a run's batch and a retirement read it in.
        entity.HasIndex(forgotten => new { forgotten.MailFolderBindingId, forgotten.UidValidity, forgotten.Uid })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailboxForgottenOccurrenceUniqueIndexName);

        // Cascades from the folder, and through it from the account, for the reason a flagged delete does.
        entity.HasOne<MailFolderBindingEntity>()
            .WithMany()
            .HasForeignKey(forgotten => forgotten.MailFolderBindingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
