// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Categories;
using MailFathom.Infrastructure.Persistence.Categories;
using MailFathom.Infrastructure.Persistence.Entities;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Categories;

/// <summary>Covers which records leave when an own category leaves an account's declaration.</summary>
public sealed class EmailCategorySelectionsTests
{
    [Fact]
    public void OfWithdrawnOwnCategories_AnOwnCategoryNoLongerDeclared_SelectsItsRecord()
    {
        // Arrange
        var withdrawn = Record("work", EmailCategoryKind.Own, "Contracts");

        // Act
        var selected = Select("work", [], withdrawn);

        // Assert
        Assert.Same(withdrawn, Assert.Single(selected));
    }

    [Fact]
    public void OfWithdrawnOwnCategories_AnOwnCategoryStillDeclared_LeavesItsRecord()
    {
        // Act
        var selected = Select("work", ["Contracts"], Record("work", EmailCategoryKind.Own, "Contracts"));

        // Assert
        Assert.Empty(selected);
    }

    /// <summary>Names are compared exactly, because a rename is what removes a name, and case is part of what the record says.</summary>
    [Fact]
    public void OfWithdrawnOwnCategories_ANameDifferingOnlyInCase_IsWithdrawn()
    {
        // Act
        var selected = Select("work", ["Contracts"], Record("work", EmailCategoryKind.Own, "contracts"));

        // Assert
        Assert.Single(selected);
    }

    /// <summary>A default is switched off by being hidden, so nothing about an account's own list reaches one.</summary>
    [Fact]
    public void OfWithdrawnOwnCategories_ADefaultCategory_IsNeverSelected()
    {
        // Act
        var selected = Select("work", [], Record("work", EmailCategoryKind.Default, "finance"));

        // Assert
        Assert.Empty(selected);
    }

    [Fact]
    public void OfWithdrawnOwnCategories_AnotherAccountsRecord_IsLeftAlone()
    {
        // Act
        var selected = Select("work", [], Record("home", EmailCategoryKind.Own, "Contracts"));

        // Assert
        Assert.Empty(selected);
    }

    private static List<EmailCategoryEntity> Select(
        string account,
        IReadOnlyList<string> declaredNames,
        params EmailCategoryEntity[] records) =>
        [.. records.AsQueryable().OfWithdrawnOwnCategories(account, declaredNames)];

    private static EmailCategoryEntity Record(string account, EmailCategoryKind kind, string category) => new()
    {
        StoredEmailId = Guid.CreateVersion7(),
        StoredEmail = new StoredEmailEntity { MailboxAccountId = account, MailFolderBinding = null! },
        Kind = kind,
        Category = category,
        Probability = 0.9,
        Source = EmailCategorySource.Model,
        Origin = "provider/build",
        QuestionRevision = "abc123",
    };
}
