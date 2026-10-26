// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MailFathom.Infrastructure.Persistence.Users.Configurations;

/// <summary>Declares the organizations' signing keys, the one-active-key rule, and the rule that a spent key holds no private half.</summary>
internal sealed class OrganizationSigningKeyConfiguration : IEntityTypeConfiguration<OrganizationSigningKeyEntity>
{
    /// <summary>The filter naming an active key: neither retired nor revoked.</summary>
    internal const string ActiveKeyFilter = "\"RetiredAt\" IS NULL AND \"RevokedAt\" IS NULL";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OrganizationSigningKeyEntity> entity)
    {
        entity.ToTable(
            OrganizationSigningKeyEntity.TableName,
            table =>
            {
                // The sealed value and the ring key that sealed it are one fact, so neither stands without the other.
                table.HasCheckConstraint(
                    PersistenceConstraintNames.OrganizationSigningKeySealedPairCheckConstraintName,
                    "(\"SealedPrivateKey\" IS NULL) = (\"DataEncryptionKeyId\" IS NULL)");

                // A retired or revoked key has had its private half destroyed; a row that kept one would be a stealable
                // key the rotation claimed to have removed.
                table.HasCheckConstraint(
                    PersistenceConstraintNames.OrganizationSigningKeySpentHoldsNoPrivateKeyCheckConstraintName,
                    $"({ActiveKeyFilter}) OR \"SealedPrivateKey\" IS NULL");
            });

        entity.HasKey(key => key.Id);

        // Minted by the write that creates the key, so the selector it reports is the one stored.
        entity.Property(key => key.Id).ValueGeneratedNever();
        entity.Property(key => key.PublicKey).IsRequired();
        entity.Property(key => key.DataEncryptionKeyId).HasMaxLength(128);

        // At most one active key per organization, enforced here rather than by a read before the write: two replicas
        // minting or rotating at once leave one active key, and the loser's insert finds the winner's row.
        entity.HasIndex(key => key.OrganizationId)
            .IsUnique()
            .HasFilter(ActiveKeyFilter)
            .HasDatabaseName(PersistenceConstraintNames.OrganizationSigningKeyActiveUniqueIndexName);

        // One organization's keys in identifier order, which is the order they were minted in and the order a listing
        // pages through them.
        entity.HasIndex(key => new { key.OrganizationId, key.Id })
            .HasDatabaseName(PersistenceConstraintNames.OrganizationSigningKeyOrganizationIndexName);

        // Cascaded, as the organization's policy and groups are: a key is nothing but what the organization signed with,
        // and an organization is deleted only once it has neither members nor mail accounts left to sign or verify.
        entity.HasOne<OrganizationEntity>()
            .WithMany()
            .HasForeignKey(key => key.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName(PersistenceConstraintNames.OrganizationSigningKeyOrganizationForeignKeyName);
    }
}
