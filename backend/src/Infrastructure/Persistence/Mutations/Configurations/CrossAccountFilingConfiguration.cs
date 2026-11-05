// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Mutations.Filing;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Mutations.Configurations;

internal sealed class CrossAccountFilingConfiguration : IEntityTypeConfiguration<CrossAccountFilingEntity>
{
    public void Configure(EntityTypeBuilder<CrossAccountFilingEntity> entity)
    {
        entity.ToTable("cross_account_filings");
        entity.HasKey(filing => filing.Id)
            .HasName(PersistenceConstraintNames.CrossAccountFilingPrimaryKeyConstraintName);
        entity.Property(filing => filing.Id).ValueGeneratedNever();

        entity.Property(filing => filing.DestinationAccountId).HasMaxLength(128).IsRequired();
        entity.Property(filing => filing.SourceAccountId).HasMaxLength(128).IsRequired();
        entity.Property(filing => filing.DestinationFolderPath)
            .HasMaxLength(CrossAccountFilingEntity.MaximumFolderPathLength)
            .IsRequired();

        // Stored as text for the reason every other stage is: readable in an ad-hoc audit query, and safe against a
        // later reordering of the enum.
        entity.Property(filing => filing.Stage).HasConversion<string>().HasMaxLength(64).IsRequired();
        entity.Property(filing => filing.ConcurrencyVersion).IsRowVersion();

        // What a destination's pass reads, filtered to the rows it can still act on, so a deployment's ended filings
        // never weigh on the read.
        entity.HasIndex(filing => new { filing.DestinationAccountId, filing.RecordedAt, filing.Id })
            .HasDatabaseName(PersistenceConstraintNames.CrossAccountFilingOutstandingIndexName)
            .HasFilter(
                $"\"{nameof(CrossAccountFilingEntity.Stage)}\" IN ('{nameof(CrossAccountFilingStage.Recorded)}', '{nameof(CrossAccountFilingStage.AppendIssued)}')");

        // The lookup a source's convergence pass makes for every delete it is about to issue, and the one a given-up
        // removal makes to say which notification it is.
        entity.HasIndex(filing => filing.RemovalMutationId)
            .IsUnique()
            .HasDatabaseName(PersistenceConstraintNames.CrossAccountFilingRemovalUniqueIndexName)
            .HasFilter($"\"{nameof(CrossAccountFilingEntity.RemovalMutationId)}\" IS NOT NULL");
    }
}
