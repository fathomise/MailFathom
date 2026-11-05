// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Rules.Configurations;

/// <summary>Maps the append-only archive of every committed write to an account's rules.</summary>
internal sealed class MailRuleSetRevisionConfiguration : IEntityTypeConfiguration<MailRuleSetRevisionEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<MailRuleSetRevisionEntity> entity)
    {
        entity.ToTable("mail_rule_set_revisions");
        entity.HasKey(revision => revision.Id);
        entity.Property(revision => revision.Id).ValueGeneratedNever();
        entity.Property(revision => revision.MailboxAccountId).HasMaxLength(128).IsRequired();
        entity.Property(revision => revision.Revision)
            .HasMaxLength(MailRuleExecutionEntity.RevisionLength)
            .IsFixedLength()
            .IsRequired();
        entity.Property(revision => revision.ReplacedRevision)
            .HasMaxLength(MailRuleExecutionEntity.RevisionLength)
            .IsFixedLength()
            .IsRequired();
        entity.Property(revision => revision.Surface)
            .HasMaxLength(MailRuleSetRevisionEntity.MaximumSurfaceLength)
            .IsRequired();
        entity.Property(revision => revision.Declarations).HasColumnType("jsonb").IsRequired();

        entity.HasOne<MailRuleSetEntity>()
            .WithMany()
            .HasForeignKey(revision => revision.MailboxAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.HasIndex(revision => new { revision.MailboxAccountId, revision.Version })
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.MailRuleSetRevisionVersionUniqueIndexName);
        entity.HasIndex(revision => new { revision.MailboxAccountId, revision.Revision, revision.CommittedAt })
            .HasDatabaseName(PersistenceConstraintNames.MailRuleSetRevisionResolutionIndexName);
    }
}
