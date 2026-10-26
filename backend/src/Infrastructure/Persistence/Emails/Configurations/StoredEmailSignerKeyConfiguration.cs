// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Emails.Configurations;

/// <summary>Declares the key material of a stored email's signer, one row per signed message.</summary>
internal sealed class StoredEmailSignerKeyConfiguration : IEntityTypeConfiguration<StoredEmailSignerKeyEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StoredEmailSignerKeyEntity> entity)
    {
        entity.ToTable("stored_email_signer_keys");
        entity.HasKey(signerKey => signerKey.StoredEmailId);
        entity.Property(signerKey => signerKey.StoredEmailId).ValueGeneratedNever();
        entity.Property(signerKey => signerKey.KeyMaterial).IsRequired();

        entity.HasOne(signerKey => signerKey.StoredEmail)
            .WithOne()
            .HasForeignKey<StoredEmailSignerKeyEntity>(signerKey => signerKey.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
