// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Footers.Configurations;

internal sealed class OrganizationFooterTemplateConfiguration : IEntityTypeConfiguration<OrganizationFooterTemplateEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrganizationFooterTemplateEntity> entity)
    {
        entity.ToTable(OrganizationFooterTemplateEntity.TableName);
        entity.HasKey(template => template.OrganizationId);
        entity.Property(template => template.OrganizationId).ValueGeneratedNever();
        entity.Property(template => template.Document).HasColumnType("jsonb").IsRequired();

        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(template => template.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
