// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>
/// Asserts the shape of the model the signer key table's schema is generated from. The model is built in memory by the
/// real PostgreSQL provider and no connection is opened.
/// </summary>
public sealed class StoredEmailSignerKeyModelTests
{
    /// <summary>
    /// Two runs storing one message's key both insert, and the loser is retried only where the key carries the name the
    /// conflict predicate recognizes.
    /// </summary>
    [Fact]
    public void StoredEmailSignerKeyModel_TheKey_IsTheStoredEmailUnderTheNameTheConflictPredicateRecognizes()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = SignerKeyEntityType(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["StoredEmailId"], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.StoredEmailSignerKeyPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>The key names the person who signed, so erasing the message leaves none of it behind.</summary>
    [Fact]
    public void StoredEmailSignerKeyModel_TheStoredEmailItBelongsTo_TakesItWhenItIsDeleted()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = Assert.Single(SignerKeyEntityType(context).GetForeignKeys());

        // Assert
        Assert.Equal(typeof(StoredEmailEntity), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    private static IEntityType SignerKeyEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StoredEmailSignerKeyEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
