// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Footers.Configurations;

internal sealed class MailAccountFooterConfiguration : IEntityTypeConfiguration<MailAccountFooterEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailAccountFooterEntity> entity)
    {
        entity.ToTable(MailAccountFooterEntity.TableName);
        entity.HasKey(footer => new { footer.UserId, footer.MailAccountId });

        // A document rather than a row per image: a footer is written and read whole, and its handful of bounded images
        // are what one write carries.
        entity.Property(footer => footer.Document).HasColumnType("jsonb").IsRequired();

        // Off the assignment rather than the user or the account alone, because a footer is what this person sends from
        // this account: it goes when they stop being assigned the account, and with the assignment when either is erased.
        entity.HasOne<MailAccountAssignmentEntity>()
            .WithMany()
            .HasForeignKey(footer => new { footer.UserId, footer.MailAccountId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
