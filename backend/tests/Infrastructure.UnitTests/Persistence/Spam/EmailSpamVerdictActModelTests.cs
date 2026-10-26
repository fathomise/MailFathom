// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Spam;

/// <summary>
/// Asserts the shape of the model the verdict-act table's schema is generated from. The model is built in memory by the
/// real PostgreSQL provider and no connection is opened.
/// </summary>
public sealed class EmailSpamVerdictActModelTests
{
    /// <summary>
    /// One row per act on one message is what makes recording an act twice one row, and a lost race is recognized as
    /// one only where the key carries the name the conflict predicate names back.
    /// </summary>
    [Fact]
    public void EmailSpamVerdictActModel_TheKey_IsTheMessageAndTheActUnderTheNameTheConflictPredicateRecognizes()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = VerdictActEntityType(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["StoredEmailId", "Act"], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.EmailSpamVerdictActPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>What a verdict did is derived from the message and goes with it, so erasing the message leaves nothing behind.</summary>
    [Fact]
    public void EmailSpamVerdictActModel_TheStoredEmailItDescribes_TakesItWhenItIsDeleted()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = Assert.Single(VerdictActEntityType(context).GetForeignKeys());

        // Assert
        Assert.Equal(typeof(StoredEmailEntity), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>Reads the design-time model, for the reason the stored email's own model tests do.</summary>
    private static IEntityType VerdictActEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(EmailSpamVerdictActEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
