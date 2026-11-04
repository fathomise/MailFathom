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
/// Asserts the shape of the model the filing table's schema is generated from. The model is built in memory by the real
/// PostgreSQL provider and no connection is opened, so this states what the constraint and the two indexes are declared
/// to be; whether PostgreSQL then plans the join against them is an integration question.
/// </summary>
public sealed class OutgoingEmailFilingModelTests
{
    /// <summary>
    /// The key is what refuses a second copy of one send into one place, and a lost race is only recognized as one
    /// where the constraint carries a name the conflict predicate can name back.
    /// </summary>
    [Fact]
    public void OutgoingEmailFilingModel_TheKeyThatRefusesASecondCopy_CarriesTheNameTheConflictPredicateRecognizes()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = FilingEntityType(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal(["OutgoingEmailId", "Filing"], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.OutgoingEmailFilingPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>
    /// Both read the copies a folder may still hold, so both carry the same filter and it is the stage alone.
    /// Dropping the stage would leave every mirror withdrawn before a run saw it, and every append the server never
    /// answered, sitting in all of them for the life of the deployment; adding the meeting back would drop a copy out
    /// of them the moment it was recognized, which is what would leave the second occurrence of the same message — the
    /// one a provider filed itself — read as mail somebody sent the user.
    /// </summary>
    [Theory]
    [InlineData(PersistenceConstraintNames.OutgoingEmailFilingPlacementIndexName, "PlacementUid")]
    [InlineData(PersistenceConstraintNames.OutgoingEmailFilingMessageIdIndexName, "InternetMessageId")]
    public void OutgoingEmailFilingModel_TheFilingIndexes_AreFilteredToTheCopiesAFolderMayStillHold(
        string indexName,
        string expectedLastColumn)
    {
        // Act
        var index = FindFilingIndex(indexName);

        // Assert — a filing names the account by its generated identifier alone, so every one of them is entered
        // by that identifier and nothing stands before it.
        Assert.Equal("MailboxAccountId", index.Properties[0].Name);
        Assert.Equal(expectedLastColumn, index.Properties[^1].Name);
        Assert.Equal("\"Stage\" = 'Confirmed'", index.GetFilter());
    }

    /// <summary>
    /// The provider copies an outbox pass still owes a removal are a handful in flight, so the index holding them is
    /// filtered to exactly those rows and leaves one the moment its copy is settled.
    /// </summary>
    [Fact]
    public void OutgoingEmailFilingModel_TheProviderCopyIndex_HoldsOnlyTheCopiesStillOwedARemoval()
    {
        // Act
        var index = FindFilingIndex(PersistenceConstraintNames.OutgoingEmailFilingProviderCopyIndexName);

        // Assert
        Assert.Equal(
            ["MailboxAccountId", "AppendedAt", "OutgoingEmailId"],
            index.Properties.Select(property => property.Name));
        Assert.Equal("\"ProviderCopyUid\" IS NOT NULL AND \"ProviderCopySettledAt\" IS NULL", index.GetFilter());
    }

    private static IIndex FindFilingIndex(string indexName)
    {
        using var context = CreateContext();

        var index = FilingEntityType(context)
            .GetIndexes()
            .FirstOrDefault(candidate => candidate.GetDatabaseName() == indexName);

        Assert.NotNull(index);

        return index;
    }

    /// <summary>Reads the design-time model, for the reason the stored email's own model tests do.</summary>
    private static IEntityType FilingEntityType(MailFathomDbContext context) =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OutgoingEmailFilingEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
