// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Categories;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Categories.Configurations;

/// <summary>Declares what a stored message's categories are recorded as.</summary>
/// <remarks>
/// <para>
/// One default per message is an index rather than a check the writer performs, because two runs can reach one message
/// and neither sees the other's uncommitted write.
/// </para>
/// <para>
/// The cascade from the stored message is what gives a category that message's retention and erasure with no pass of
/// its own to remember. Both enumerations are stored as text, so a row stays readable in an ad-hoc query.
/// </para>
/// </remarks>
internal sealed class EmailCategoryConfiguration : IEntityTypeConfiguration<EmailCategoryEntity>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<EmailCategoryEntity> entity)
    {
        entity.ToTable(
            "email_categories",
            table => table.HasCheckConstraint(
                PersistenceConstraintNames.EmailCategoryProbabilityCheckConstraintName,
                $"""
                 "{nameof(EmailCategoryEntity.Probability)}" >= 0 AND "{nameof(EmailCategoryEntity.Probability)}" <= 1
                 """));

        entity.HasKey(record => new { record.StoredEmailId, record.Kind, record.Category })
            .HasName(PersistenceConstraintNames.EmailCategoryPrimaryKeyConstraintName);

        entity.Property(record => record.Kind).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(record => record.Category)
            .HasMaxLength(EmailCategoryEntity.MaximumCategoryLength)
            .IsRequired();
        entity.Property(record => record.Source).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(record => record.Origin)
            .HasMaxLength(EmailCategoryEntity.MaximumOriginLength)
            .IsRequired();
        entity.Property(record => record.QuestionRevision)
            .HasMaxLength(EmailCategoryEntity.MaximumQuestionRevisionLength)
            .IsRequired();

        entity.HasIndex(record => record.StoredEmailId)
            .IsUnique()
            .HasFilter($"""
                        "{nameof(EmailCategoryEntity.Kind)}" = '{nameof(EmailCategoryKind.Default)}'
                        """)
            .HasDatabaseName(PersistenceConstraintNames.EmailCategoryOneDefaultUniqueIndexName);

        entity.HasOne(record => record.StoredEmail)
            .WithMany(email => email.Categories)
            .HasForeignKey(record => record.StoredEmailId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
