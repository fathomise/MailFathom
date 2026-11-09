// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Categories;

/// <summary>
/// Asserts the shape of the model the category table's schema is generated from. The model is built in memory by the
/// real PostgreSQL provider and no connection is opened.
/// </summary>
public sealed class EmailCategoryModelTests
{
    /// <summary>One row per message and category is what makes asking twice one record.</summary>
    [Fact]
    public void EmailCategoryModel_TheKey_IsTheMessageTheKindAndTheCategory()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = CategoryEntityType(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["StoredEmailId", "Kind", "Category"], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.EmailCategoryPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>The defaults are one choice, so a message holds one of them whatever two runs reach it at once.</summary>
    [Fact]
    public void EmailCategoryModel_TheDefaults_AreUniquePerMessage()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = Assert.Single(
            CategoryEntityType(context).GetIndexes(),
            candidate => candidate.GetDatabaseName() == PersistenceConstraintNames.EmailCategoryOneDefaultUniqueIndexName);

        // Assert
        Assert.True(index.IsUnique);
        Assert.Equal("""
                     "Kind" = 'Default'
                     """, index.GetFilter());
    }

    /// <summary>A category is derived from the message and goes with it, so erasing the message leaves nothing behind.</summary>
    [Fact]
    public void EmailCategoryModel_TheStoredEmailItDescribes_TakesItWhenItIsDeleted()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = Assert.Single(CategoryEntityType(context).GetForeignKeys());

        // Assert
        Assert.Equal(typeof(StoredEmailEntity), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>Reads the design-time model, for the reason the stored email's own model tests do.</summary>
    private static IEntityType CategoryEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(EmailCategoryEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
