// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.ThreadStates;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Entities;

/// <summary>
/// Asserts the shape of the two tables a conversation's state is stored in. The model is built in memory by the real
/// PostgreSQL provider and no connection is opened, so what this states is what the keys, constraints, delete
/// behaviours, and lengths are declared to be.
/// </summary>
/// <remarks>
/// Two of these carry an obligation nothing else reports. The cascades are the privacy rule the schema itself keeps —
/// erasing a correspondence has to take what was derived from it, and a delete behaviour that drifted would leave
/// sentences about mail nobody holds any more, which no test of the deleting code would notice. And the statements are
/// replaced whole on every derivation, so a cascade that stopped reaching them would leave a superseded reading beside
/// the current one and a screen would draw both.
/// </remarks>
public sealed class EmailThreadStateModelTests
{
    /// <summary>One state per conversation, keyed by the conversation rather than by a row identity of its own.</summary>
    [Fact]
    public void EmailThreadStateModel_PrimaryKey_IsTheConversationUnderItsDeclaredName()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = EntityType<EmailThreadStateEntity>(context).FindPrimaryKey();

        // Assert
        Assert.NotNull(key);
        Assert.Equal([nameof(EmailThreadStateEntity.EmailThreadId)], key.Properties.Select(property => property.Name));
        Assert.Equal(PersistenceConstraintNames.EmailThreadStatePrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>Erasing the correspondence erases what was derived about it, which is the schema's privacy obligation.</summary>
    [Fact]
    public void EmailThreadStateModel_TheConversationItDescribes_TakesTheStateWithIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = EntityType<EmailThreadStateEntity>(context).GetForeignKeys().Single();

        // Assert
        Assert.Equal(nameof(EmailThreadEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>A state is replaced whole, so its statements go with the state rather than outliving it.</summary>
    [Fact]
    public void EmailThreadStateEntryModel_TheStateItBelongsTo_TakesItsStatementsWithIt()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = EntityType<EmailThreadStateEntryEntity>(context).GetForeignKeys().Single();

        // Assert
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
        Assert.Equal(PersistenceConstraintNames.EmailThreadStateEntryForeignKeyName, foreignKey.GetConstraintName());
    }

    /// <summary>The read is one conversation's statements in the producer's order, so the index carries that order.</summary>
    [Fact]
    public void EmailThreadStateEntryModel_TheIndex_CarriesTheOrderTheStatementsAreReadIn()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType<EmailThreadStateEntryEntity>(context).GetIndexes().Single();

        // Assert
        Assert.False(index.IsUnique);
        Assert.Equal(
            [
                nameof(EmailThreadStateEntryEntity.EmailThreadId),
                nameof(EmailThreadStateEntryEntity.Aspect),
                nameof(EmailThreadStateEntryEntity.Ordinal),
            ],
            index.Properties.Select(property => property.Name));
    }

    /// <summary>A statement a model wrote is bounded before it reaches the database rather than by it.</summary>
    [Fact]
    public void EmailThreadStateEntryModel_WhatAStatementSays_IsBoundedByTheColumnItIsStoredIn()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var statement = EntityType<EmailThreadStateEntryEntity>(context);

        // Assert
        Assert.Equal(
            EmailThreadStateEntryEntity.MaximumTextLength,
            statement.FindProperty(nameof(EmailThreadStateEntryEntity.Text))!.GetMaxLength());
        Assert.Equal(
            EmailThreadStateEntryEntity.MaximumOwedByLength,
            statement.FindProperty(nameof(EmailThreadStateEntryEntity.OwedBy))!.GetMaxLength());
    }

    /// <summary>A title's two lines are bounded where a model's answer is read, and the columns hold exactly that much.</summary>
    [Fact]
    public void EmailThreadStateModel_TheTitle_IsBoundedByTheColumnsItIsStoredIn()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var state = EntityType<EmailThreadStateEntity>(context);

        // Assert
        Assert.Equal(
            ThreadTitle.MaximumTopicLength,
            state.FindProperty(nameof(EmailThreadStateEntity.Topic))!.GetMaxLength());
        Assert.Equal(
            ThreadTitle.MaximumStandingLength,
            state.FindProperty(nameof(EmailThreadStateEntity.Standing))!.GetMaxLength());
    }

    private static IEntityType EntityType<TEntity>(MailFathomDbContext context)
        where TEntity : class =>
        context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(TEntity))!;

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
