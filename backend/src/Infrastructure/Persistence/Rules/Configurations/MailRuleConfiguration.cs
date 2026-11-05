// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Rules.Editing;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Rules.Configurations;

/// <summary>Maps one rule of one account, with the order and the names the account's set is held to as constraints.</summary>
internal sealed class MailRuleConfiguration : IEntityTypeConfiguration<MailRuleEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailRuleEntity> entity)
    {
        entity.ToTable("mail_rules");
        entity.HasKey(rule => rule.Id);
        entity.Property(rule => rule.Id).ValueGeneratedNever();
        entity.Property(rule => rule.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(rule => rule.Name).HasMaxLength(MailRuleSetValidator.MaximumNameLength).IsRequired();
        entity.Property(rule => rule.NormalizedName).HasMaxLength(MailRuleSetValidator.MaximumNameLength).IsRequired();
        entity.Property(rule => rule.Condition).IsRequired();
        entity.Property(rule => rule.Actions).HasColumnType("jsonb").IsRequired();
        entity.Property(rule => rule.Triggers).IsRequired();

        entity.HasOne<MailRuleSetEntity>()
            .WithMany()
            .HasForeignKey(rule => rule.MailboxAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // The position and the name are constraints rather than checks, so no surface can commit a set whose order has
        // a gap filled twice or two rules a person would read as one. The first is also the order a pass reads.
        entity.HasIndex(rule => new { rule.MailboxAccountId, rule.Position })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailRulePositionUniqueIndexName);
        entity.HasIndex(rule => new { rule.MailboxAccountId, rule.NormalizedName })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailRuleNameUniqueIndexName);
    }
}
