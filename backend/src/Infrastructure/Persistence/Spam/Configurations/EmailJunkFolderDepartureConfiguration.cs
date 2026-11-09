// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Spam.Configurations;

/// <summary>Declares what is remembered of a message that left a junk folder through another mail client.</summary>
/// <remarks>The table cascades from the account, so what is remembered goes with the mailbox it describes.</remarks>
internal sealed class EmailJunkFolderDepartureConfiguration : IEntityTypeConfiguration<EmailJunkFolderDepartureEntity>
{
    private const int HexadecimalSha256Length = 64;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailJunkFolderDepartureEntity> entity)
    {
        entity.ToTable("email_junk_folder_departures");
        entity.HasKey(departure => new { departure.MailboxAccountId, departure.MessageIdDigest })
            .HasName(PersistenceConstraintNames.EmailJunkFolderDeparturePrimaryKeyConstraintName);
        entity.Property(departure => departure.MailboxAccountId).HasMaxLength(128);
        entity.Property(departure => departure.MessageIdDigest).HasMaxLength(HexadecimalSha256Length);

        entity.HasOne<MailboxAccountEntity>()
            .WithMany()
            .HasForeignKey(departure => departure.MailboxAccountId)
            .HasConstraintName(PersistenceConstraintNames.EmailJunkFolderDepartureAccountForeignKeyName)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
