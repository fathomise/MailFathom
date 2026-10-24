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
/// Asserts the shape of the model a held account's folders are generated from. The two unique indexes are what a second
/// writer meets, so a filter or a null semantics lost here would let two edits commit a duplicate the domain refused.
/// </summary>
public sealed class MailFolderModelTests
{
    /// <summary>A top-level folder has no parent, so the name index treats that null as one value rather than many.</summary>
    [Fact]
    public void MailFolderModel_TheSiblingNameIndex_IsUniqueAmongLiveFoldersWithTheTopLevelAsOneParent()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(PersistenceConstraintNames.MailFolderSiblingNameUniqueIndexName, context);

        // Assert
        Assert.Equal(
            ["MailboxAccountId", "ParentId", "NameKey"],
            index.Properties.Select(property => property.Name));
        Assert.True(index.IsUnique);
        Assert.False((bool?)index.FindAnnotation("Npgsql:NullsDistinct")?.Value);
        Assert.Equal("\"ErasedAt\" IS NULL", index.GetFilter());
    }

    /// <summary>One live folder per protected role, and a folder without a role or already erased takes no part.</summary>
    [Fact]
    public void MailFolderModel_TheRoleIndex_IsUniqueAmongLiveFoldersThatPlayARole()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(PersistenceConstraintNames.MailFolderRoleUniqueIndexName, context);

        // Assert
        Assert.Equal(["MailboxAccountId", "Role"], index.Properties.Select(property => property.Name));
        Assert.True(index.IsUnique);
        Assert.Equal("\"Role\" IS NOT NULL AND \"ErasedAt\" IS NULL", index.GetFilter());
    }

    /// <summary>One live folder per alias, which is where two runs projecting one newly declared folder at once meet.</summary>
    [Fact]
    public void MailFolderModel_TheAliasIndex_IsUniqueAmongLiveFoldersAnAliasFeeds()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = FindIndex(PersistenceConstraintNames.MailFolderAliasUniqueIndexName, context);

        // Assert
        Assert.Equal(["MailboxAccountId", "Alias"], index.Properties.Select(property => property.Name));
        Assert.True(index.IsUnique);
        Assert.Equal("\"SourceFolderAlias\" IS NOT NULL AND \"ErasedAt\" IS NULL", index.GetFilter());
    }

    /// <summary>Erasing an account takes its hierarchy with it, and removing a folder never takes the mail filed in it.</summary>
    [Fact]
    public void MailFolderModel_TheKeys_CascadeFromTheAccountAndNeverOntoStoredMail()
    {
        // Arrange
        using var context = CreateContext();
        var model = context.GetService<IDesignTimeModel>().Model;

        // Act
        var accountKey = model.FindEntityType(typeof(MailFolderEntity))!
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(MailboxAccountEntity));
        var storedEmailKeys = model.FindEntityType(typeof(StoredEmailEntity))!
            .GetForeignKeys()
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(MailFolderEntity))
            .ToArray();

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, accountKey.DeleteBehavior);
        Assert.NotEmpty(storedEmailKeys);
        Assert.All(storedEmailKeys, foreignKey => Assert.Equal(DeleteBehavior.NoAction, foreignKey.DeleteBehavior));
    }

    /// <summary>Every hierarchy write bumps the revision, so it is what makes a write conditional on the hierarchy it read.</summary>
    [Fact]
    public void MailFolderModel_TheAccountsFoldersRevision_IsAConcurrencyToken()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var revision = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(MailboxAccountEntity))!
            .FindProperty(nameof(MailboxAccountEntity.LocalMailFoldersRevision));

        // Assert
        Assert.NotNull(revision);
        Assert.True(revision.IsConcurrencyToken);
    }

    private static IIndex FindIndex(string indexName, MailFathomDbContext context)
    {
        var index = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(MailFolderEntity))!
            .GetIndexes()
            .FirstOrDefault(candidate => candidate.GetDatabaseName() == indexName);

        Assert.NotNull(index);

        return index;
    }

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
