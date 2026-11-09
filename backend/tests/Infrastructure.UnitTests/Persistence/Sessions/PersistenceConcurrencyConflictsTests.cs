// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Sessions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Sessions;

/// <summary>Covers which unique violations are a race a retry can resolve.</summary>
public sealed class PersistenceConcurrencyConflictsTests
{
    /// <summary>Every constraint a competing writer can legitimately win.</summary>
    public static TheoryData<string> RecognizedConstraints =>
    [
        PersistenceConstraintNames.SynchronizationCheckpointPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailFolderBindingUniqueIndexName,
        PersistenceConstraintNames.MailboxAccountPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailboxMutationIdentityUniqueIndexName,
        PersistenceConstraintNames.MailboxMutationAuditEntryMutationUniqueIndexName,
        PersistenceConstraintNames.MailActKeyUniqueIndexName,
        PersistenceConstraintNames.EmbeddingProfileFingerprintUniqueIndexName,
        PersistenceConstraintNames.EmbeddingProfileLifecycleUniqueIndexName,
        PersistenceConstraintNames.MailRuleSetPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailRuleEvaluationRunPrimaryKeyConstraintName,
        PersistenceConstraintNames.SpamClassificationRunPrimaryKeyConstraintName,
        PersistenceConstraintNames.EmailSpamClassificationPrimaryKeyConstraintName,
        PersistenceConstraintNames.EmailSpamVerdictActPrimaryKeyConstraintName,
        PersistenceConstraintNames.EmailChunkOrdinalUniqueIndexName,
        PersistenceConstraintNames.StoredEmailSignerKeyPrimaryKeyConstraintName,
        PersistenceConstraintNames.EmailCategoryPrimaryKeyConstraintName,
        PersistenceConstraintNames.EmailCategoryOneDefaultUniqueIndexName,
        PersistenceConstraintNames.EmailEnrichmentPrimaryKeyConstraintName,
        PersistenceConstraintNames.EmailEnrichmentMarkAspectUniqueIndexName,
        PersistenceConstraintNames.MailRederivationPositionPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailRederivationRunPrimaryKeyConstraintName,
        PersistenceConstraintNames.ContactAddressUniqueIndexName,
        PersistenceConstraintNames.ContactPhoneNumberPrimaryKeyConstraintName,
        PersistenceConstraintNames.MailDraftCopyPrimaryKeyConstraintName,
        PersistenceConstraintNames.ContentMoveRunPrimaryKeyConstraintName,
        PersistenceConstraintNames.StoredSecretUserNameUniqueIndexName,
        PersistenceConstraintNames.NotificationUnreadConditionUniqueIndexName,
        PersistenceConstraintNames.MailFolderSiblingNameUniqueIndexName,
        PersistenceConstraintNames.MailFolderRoleUniqueIndexName,
        PersistenceConstraintNames.MailFolderAliasUniqueIndexName,
        PersistenceConstraintNames.EmailThreadStatePrimaryKeyConstraintName,
        PersistenceConstraintNames.MailboxSourceRemovalOccurrenceUniqueIndexName,
        PersistenceConstraintNames.MailboxFlaggedDeleteOccurrenceUniqueIndexName,
        PersistenceConstraintNames.CalendarEventReminderPrimaryKeyConstraintName,
        PersistenceConstraintNames.PersonalTaskReminderPrimaryKeyConstraintName,
        PersistenceConstraintNames.CalendarEventImportedUidUniqueIndexName,
    ];

    [Theory]
    [MemberData(nameof(RecognizedConstraints))]
    public void IsConcurrencyConflict_UniqueViolationOnARecognizedConstraint_IsAConflict(string constraintName)
    {
        // Arrange
        var exception = CreateUniqueViolation(constraintName);

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.True(isConflict);
    }

    /// <summary>The vector row is written by an upsert that cannot violate its key, so a violation of it is a defect rather than a race.</summary>
    [Fact]
    public void IsConcurrencyConflict_UniqueViolationOnTheVectorKey_IsNotAConflict()
    {
        // Arrange
        var exception = CreateUniqueViolation(PersistenceConstraintNames.EmailEmbeddingPrimaryKeyConstraintName);

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.False(isConflict);
    }

    /// <summary>An unlisted constraint stays a failure, because retrying it would repeat a write that cannot succeed.</summary>
    [Fact]
    public void IsConcurrencyConflict_UniqueViolationOnAnUnlistedConstraint_IsNotAConflict()
    {
        // Arrange
        var exception = CreateUniqueViolation("ix_something_this_repository_does_not_retry");

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.False(isConflict);
    }

    /// <summary>A recognized constraint name does not make a different provider error a race.</summary>
    [Fact]
    public void IsConcurrencyConflict_ForeignKeyViolationOnARecognizedConstraint_IsNotAConflict()
    {
        // Arrange
        var exception = new DbUpdateException(
            "save failed",
            CreatePostgresException(
                PostgresErrorCodes.ForeignKeyViolation,
                PersistenceConstraintNames.EmailChunkOrdinalUniqueIndexName));

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.False(isConflict);
    }

    /// <summary>Every key a writer can lose when the nightly removal takes the conversation under it.</summary>
    public static TheoryData<string> ConversationForeignKeys =>
    [
        PersistenceConstraintNames.StoredEmailThreadForeignKeyName,
        PersistenceConstraintNames.EmailThreadIdentifierThreadForeignKeyName,
        PersistenceConstraintNames.EmailThreadStateThreadForeignKeyName,
        PersistenceConstraintNames.EmailThreadStateEntryForeignKeyName,
        PersistenceConstraintNames.EmailThreadMergedIntoForeignKeyName,
    ];

    /// <summary>The retry finds nothing bound and starts a new conversation, so the arrival is neither lost nor charged against its run.</summary>
    [Theory]
    [MemberData(nameof(ConversationForeignKeys))]
    public void IsConcurrencyConflict_ForeignKeyViolationOnAConversationKey_IsAConflict(string constraintName)
    {
        // Arrange
        var exception = new DbUpdateException(
            "save failed",
            CreatePostgresException(PostgresErrorCodes.ForeignKeyViolation, constraintName));

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.True(isConflict);
    }

    /// <summary>The names are not declared on the mapping, so a test is what keeps them agreeing with it.</summary>
    [Theory]
    [MemberData(nameof(ConversationForeignKeys))]
    public void ConversationForeignKeys_TheModel_HoldsEachUnderThatName(string constraintName)
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var names = context.Model.GetEntityTypes()
            .SelectMany(static entityType => entityType.GetForeignKeys())
            .Select(static foreignKey => foreignKey.GetConstraintName());

        // Assert
        Assert.Contains(constraintName, names);
    }

    /// <summary>A failure that never reached PostgreSQL carries no constraint to classify.</summary>
    [Fact]
    public void IsConcurrencyConflict_InnerExceptionIsNotAProviderFailure_IsNotAConflict()
    {
        // Arrange
        var exception = new DbUpdateException("save failed", new InvalidOperationException("something else"));

        // Act
        var isConflict = PersistenceConcurrencyConflicts.IsConcurrencyConflict(exception);

        // Assert
        Assert.False(isConflict);
    }

    private static DbUpdateException CreateUniqueViolation(string constraintName) =>
        new("save failed", CreatePostgresException(PostgresErrorCodes.UniqueViolation, constraintName));

    private static PostgresException CreatePostgresException(string sqlState, string constraintName) =>
        new(
            messageText: "duplicate key value violates unique constraint",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: sqlState,
            constraintName: constraintName);
}
