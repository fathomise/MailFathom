// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Tasks;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Tasks;

/// <summary>
/// Holds the task row to the obligations that are the schema's rather than any code path's: that a task leaves with
/// the person whose list it was on, that it outlives the mail it cites, and that one person's list is read in one
/// order.
/// </summary>
/// <remarks>
/// The model is built in memory by the real PostgreSQL provider and no connection is opened, so what these assertions
/// state is what the schema is generated from. That a running PostgreSQL then performs the cascade is an integration
/// question and is measured there.
/// </remarks>
public sealed class PersonalTaskModelTests
{
    /// <summary>
    /// This table names no mail account, so the erasure walk that enumerates the tables that do would never reach it.
    /// The cascade from the user row is what discharges an erasure request over it instead.
    /// </summary>
    [Fact]
    public void Model_ThePersonWhoseListATaskIsOn_TakesTheirTasksWithThem()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var foreignKey = ForeignKeyOn(context, nameof(PersonalTaskEntity.UserId));

        // Assert
        Assert.Equal(nameof(UserAccountEntity), foreignKey.PrincipalEntityType.ClrType.Name);
        Assert.Equal(DeleteBehavior.Cascade, foreignKey.DeleteBehavior);
    }

    /// <summary>
    /// The citation carries no foreign key, which is what lets a task outlive the mail it was read out of: a
    /// commitment read out of a thread is still owed once the thread is gone. Every association to a stored message
    /// cascades but for a reply's own parent, so a task that named one would be erased with the mail — which is why
    /// this is a value, exactly as the mutation audit trail's own message is.
    /// </summary>
    [Fact]
    public void Model_TheMessageATaskCites_IsAValueRatherThanAnAssociation()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var references = EntityType(context)
            .GetForeignKeys()
            .Where(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(StoredEmailEntity));

        // Assert
        Assert.Empty(references);
    }

    /// <summary>Most tasks cite no message at all, and one whose message was erased reads the same way.</summary>
    [Fact]
    public void Model_TheMessageATaskCites_IsOptional()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var citation = EntityType(context).FindProperty(nameof(PersonalTaskEntity.SourceStoredEmailId));

        // Assert
        Assert.NotNull(citation);
        Assert.True(citation.IsNullable);
    }

    /// <summary>A day rather than an instant, and absent where nobody has said when.</summary>
    [Fact]
    public void Model_TheDayATaskIsDueOn_IsOptional()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var dueOn = EntityType(context).FindProperty(nameof(PersonalTaskEntity.DueOn));

        // Assert
        Assert.NotNull(dueOn);
        Assert.True(dueOn.IsNullable);
        Assert.Equal(typeof(DateOnly?), dueOn.ClrType);
    }

    /// <summary>The list is drawn in the order the index declares, which is what keeps a long list costing one walk.</summary>
    [Fact]
    public void Model_TheListIndex_LeadsWithThePersonAndThenTheDueDay()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType(context)
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.PersonalTaskDueOrderIndexName);

        // Assert
        Assert.Equal(
            [
                nameof(PersonalTaskEntity.UserId),
                nameof(PersonalTaskEntity.DueOn),
                nameof(PersonalTaskEntity.Id),
            ],
            index.Properties.Select(property => property.Name));
    }

    /// <summary>
    /// The producer asks one question of the whole deployment every interval, and the index it rides holds only the
    /// reminders still owed: a filter on the claim leaves out every reminder of every task already past, which is
    /// nearly all of them once a list has any history.
    /// </summary>
    [Fact]
    public void PersonalTaskReminderModel_DueIndex_HoldsOnlyTheRemindersNothingHasAnnouncedYet()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = ReminderEntityType(context)
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName()
                == PersistenceConstraintNames.PersonalTaskReminderDueIndexName);

        // Assert
        Assert.Equal([nameof(PersonalTaskReminderEntity.DueAt)], index.Properties.Select(property => property.Name));
        Assert.Equal("\"RaisedForDueAt\" IS NULL", index.GetFilter());
    }

    /// <summary>
    /// What deleting a task promises: it takes its reminders with it, so nothing is left to announce something
    /// nobody owes any more. The lead is half the key, which is what makes one lead set twice one row.
    /// </summary>
    [Fact]
    public void PersonalTaskReminderModel_TheTask_IsTheKeyItCascadesFrom()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var reminder = ReminderEntityType(context);
        var reference = Assert.Single(reminder.GetForeignKeys());

        // Assert
        Assert.Equal(
            [nameof(PersonalTaskReminderEntity.PersonalTaskId), nameof(PersonalTaskReminderEntity.MinutesBefore)],
            reminder.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Equal(typeof(PersonalTaskEntity), reference.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Cascade, reference.DeleteBehavior);
    }

    /// <summary>
    /// Why the key is named rather than left to the convention: two clients saving one task both read that a lead has
    /// no row and both insert one, and the loser is recognized by the constraint its insert violated. A name only EF
    /// Core knew about would leave that race ending as a provider failure instead of the retry from a fresh read.
    /// </summary>
    [Fact]
    public void PersonalTaskReminderModel_ItsKey_IsNamedSoALosingWriterIsRecognized()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var key = ReminderEntityType(context).FindPrimaryKey()!;

        // Assert
        Assert.Equal(PersistenceConstraintNames.PersonalTaskReminderPrimaryKeyConstraintName, key.GetName());
    }

    /// <summary>A row written before the column existed is owed, which is what every task was until a wait could be stated.</summary>
    [Fact]
    public void Model_TheDirection_IsStoredByNameAndDefaultsToOwed()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var direction = EntityType(context).FindProperty(nameof(PersonalTaskEntity.Direction))!;

        // Assert
        Assert.Equal(typeof(string), direction.GetProviderClrType());
        Assert.Equal(PersonalTaskDirection.Owed, direction.GetDefaultValue());
        Assert.False(direction.IsNullable);
    }

    /// <summary>
    /// The thread, the event, and the reply are cited for the reason the source message is: a task outlives the mail
    /// and the meeting it is about, so none of them is an association that could take the task with it.
    /// </summary>
    [Fact]
    public void Model_TheAnchorAndTheAnswer_AreValuesRatherThanAssociations()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var principals = EntityType(context).GetForeignKeys().Select(foreignKey => foreignKey.PrincipalEntityType.ClrType);

        // Assert
        Assert.Equal([typeof(UserAccountEntity)], principals);
    }

    /// <summary>Half of a plan, an anchor, or an answer names nothing, so the table refuses one whoever writes the row.</summary>
    [Theory]
    [InlineData(PersistenceConstraintNames.PersonalTaskPlanCompleteCheckConstraintName, "\"PlannedStartAt\" IS NULL", "\"PlannedMinutes\" IS NULL")]
    [InlineData(PersistenceConstraintNames.PersonalTaskSingleAnchorCheckConstraintName, "\"AnchorThreadId\" IS NULL OR \"AnchorEventId\" IS NULL", "\"AnchorThreadId\"")]
    [InlineData(PersistenceConstraintNames.PersonalTaskAnchorLabelledCheckConstraintName, "\"AnchorLabel\" IS NULL", "\"AnchorEventId\" IS NULL")]
    [InlineData(PersistenceConstraintNames.PersonalTaskCounterpartCompleteCheckConstraintName, "\"CounterpartAddress\" IS NULL", "\"CounterpartNormalizedAddress\" IS NULL")]
    [InlineData(PersistenceConstraintNames.PersonalTaskAnswerClosesAWaitCheckConstraintName, "\"AnswerStoredEmailId\" IS NULL OR", "\"Direction\" = 'Awaited' AND \"IsCompleted\"")]
    [InlineData(PersistenceConstraintNames.PersonalTaskAnswerCompleteCheckConstraintName, "\"AnswerStoredEmailId\" IS NULL", "\"AnsweredAt\" IS NULL")]
    public void Model_EachWholeValue_IsCheckedByTheTable(string name, string firstHalf, string secondHalf)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var constraint = context.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(PersonalTaskEntity))!
            .GetCheckConstraints()
            .Single(candidate => candidate.Name == name);

        // Assert
        Assert.Contains(firstHalf, constraint.Sql, StringComparison.Ordinal);
        Assert.Contains(secondHalf, constraint.Sql, StringComparison.Ordinal);
    }

    /// <summary>Each surface reads one person's tasks by one of these, so each leads with the person.</summary>
    [Theory]
    [InlineData(nameof(PersonalTaskEntity.AnchorThreadId))]
    [InlineData(nameof(PersonalTaskEntity.AnchorEventId))]
    [InlineData(nameof(PersonalTaskEntity.PlannedStartAt))]
    [InlineData(nameof(PersonalTaskEntity.CounterpartNormalizedAddress))]
    public void Model_TheNarrowingsAPersonReadsBy_AreIndexedBehindThePerson(string column)
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var indexed = EntityType(context).GetIndexes().Select(index => index.Properties.Select(property => property.Name).ToArray());

        // Assert
        Assert.Contains(indexed, columns => columns.SequenceEqual([nameof(PersonalTaskEntity.UserId), column]));
    }

    /// <summary>
    /// Settling a reply asks across every user which waits its thread answers, which a user-led index cannot serve, so
    /// it rides a partial index holding only the open waits.
    /// </summary>
    [Fact]
    public void Model_TheAwaitingAnswerIndex_HoldsOnlyTheOpenWaitsByThread()
    {
        // Arrange
        using var context = CreateContext();

        // Act
        var index = EntityType(context)
            .GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == PersistenceConstraintNames.PersonalTaskAwaitingAnswerIndexName);

        // Assert
        Assert.Equal([nameof(PersonalTaskEntity.AnchorThreadId)], index.Properties.Select(property => property.Name));
        Assert.Equal("\"Direction\" = 'Awaited' AND NOT \"IsCompleted\"", index.GetFilter());
    }

    private static IEntityType ReminderEntityType(MailFathomDbContext context) =>
        context.Model.FindEntityType(typeof(PersonalTaskReminderEntity))
            ?? throw new InvalidOperationException("The model holds no task reminder row.");

    private static IForeignKey ForeignKeyOn(MailFathomDbContext context, string propertyName) =>
        EntityType(context)
            .GetForeignKeys()
            .Single(foreignKey => foreignKey.Properties.Any(property => property.Name == propertyName));

    private static IEntityType EntityType(MailFathomDbContext context) =>
        context.Model.FindEntityType(typeof(PersonalTaskEntity))
            ?? throw new InvalidOperationException("The model holds no task row.");

    private static MailFathomDbContext CreateContext() =>
        new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
}
