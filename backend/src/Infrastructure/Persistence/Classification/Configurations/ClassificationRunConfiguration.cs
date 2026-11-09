// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Classification.Configurations;

/// <summary>Declares the outstanding whole-mailbox classification run of one account.</summary>
/// <remarks>
/// One row per account, which is what makes "one outstanding whole-mailbox classification run" a property of the key
/// rather than of a check. The scope is a text array because it is read back whole and never filtered on: the run
/// states which folders it walks, and nothing asks the database which runs walk one folder.
/// </remarks>
internal sealed class ClassificationRunConfiguration : IEntityTypeConfiguration<ClassificationRunEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ClassificationRunEntity> entity)
    {
        entity.ToTable("classification_runs");
        // One row per account, keyed by the generated identifier alone: it names one mailbox across the
        // deployment, so a shared mailbox has one outstanding run rather than one per assigned user.
        entity.HasKey(run => run.MailboxAccountId)
            .HasName(PersistenceConstraintNames.ClassificationRunPrimaryKeyConstraintName);
        entity.Property(run => run.MailboxAccountId).HasMaxLength(128).ValueGeneratedNever();
        entity.Property(run => run.SpamFolderAliases).IsRequired();
        entity.Property(run => run.CategoryFolderAliases).IsRequired();
        entity.Property(run => run.OwnCategory).HasMaxLength(EmailCategoryEntity.MaximumCategoryLength);
        entity.Property(run => run.Posture).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(run => run.Profile)
            .HasMaxLength(ClassificationRunEntity.ProfileLength)
            .IsFixedLength();
        entity.Property(run => run.Ending).HasConversion<string>().HasMaxLength(64);

        // See the stored-email mapping: this is the PostgreSQL `xmin` system column, not a user-defined column.
        entity.Property(run => run.ConcurrencyVersion).IsRowVersion();
    }
}
