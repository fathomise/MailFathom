// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape of the model the organization signing keys table is generated from. The model is built in memory
/// by the real PostgreSQL provider and no connection is opened, so this states what the table is declared to be.
/// </summary>
public sealed class OrganizationSigningKeyModelTests
{
    /// <summary>
    /// Two replicas minting an organization's key at once both insert, and only this index decides that one of them
    /// writes nothing — so it is unique, and only over the keys that are neither retired nor revoked.
    /// </summary>
    [Fact]
    public void OrganizationSigningKeyModel_TheActiveKeyIndex_HoldsAnOrganizationToOneUnspentKey()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var index = Assert.Single(
            SigningKeyEntityType(context).GetIndexes(),
            candidate => candidate.GetDatabaseName() == PersistenceConstraintNames.OrganizationSigningKeyActiveUniqueIndexName);

        // Assert
        Assert.True(index.IsUnique);
        Assert.Equal([nameof(OrganizationSigningKeyEntity.OrganizationId)], index.Properties.Select(property => property.Name));
        Assert.Equal("\"RetiredAt\" IS NULL AND \"RevokedAt\" IS NULL", index.GetFilter());
    }

    /// <summary>The listing pages through one organization's keys in identifier order, which this index is ordered by.</summary>
    [Fact]
    public void OrganizationSigningKeyModel_TheListingIndex_OrdersOneOrganizationsKeysByIdentifier()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var index = Assert.Single(
            SigningKeyEntityType(context).GetIndexes(),
            candidate => candidate.GetDatabaseName() == PersistenceConstraintNames.OrganizationSigningKeyOrganizationIndexName);

        // Assert
        Assert.Equal(
            [nameof(OrganizationSigningKeyEntity.OrganizationId), nameof(OrganizationSigningKeyEntity.Id)],
            index.Properties.Select(property => property.Name));
    }

    /// <summary>A spent key must hold no private half the database could still hand out, whatever wrote the row.</summary>
    [Fact]
    public void OrganizationSigningKeyModel_TheCheckConstraints_RefuseAPrivateHalfOnASpentKeyAndASealWithoutItsKey()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var constraints = SigningKeyEntityType(context).GetCheckConstraints()
            .ToDictionary(constraint => constraint.Name!, constraint => constraint.Sql, StringComparer.Ordinal);

        // Assert
        Assert.Equal(
            "(\"SealedPrivateKey\" IS NULL) = (\"DataEncryptionKeyId\" IS NULL)",
            constraints[PersistenceConstraintNames.OrganizationSigningKeySealedPairCheckConstraintName]);
        Assert.Equal(
            "(\"RetiredAt\" IS NULL AND \"RevokedAt\" IS NULL) OR \"SealedPrivateKey\" IS NULL",
            constraints[PersistenceConstraintNames.OrganizationSigningKeySpentHoldsNoPrivateKeyCheckConstraintName]);
    }

    /// <summary>Removing an organization removes its keys, since nothing else in the schema would.</summary>
    [Fact]
    public void OrganizationSigningKeyModel_TheOrganizationItBelongsTo_TakesItsKeysWithItWhenRemoved()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var foreignKey = Assert.Single(SigningKeyEntityType(context).GetForeignKeys());

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(PersistenceConstraintNames.OrganizationSigningKeyOrganizationForeignKeyName, foreignKey.GetConstraintName());
    }

    private static IEntityType SigningKeyEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OrganizationSigningKeyEntity))!;
}
