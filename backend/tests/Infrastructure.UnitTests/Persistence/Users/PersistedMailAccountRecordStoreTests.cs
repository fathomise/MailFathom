// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Users;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Users;

/// <summary>
/// Covers how the store tells a write refused by one of its unique indexes apart from every other failed write: a
/// second account claiming an address, and a second user for an account in no organization. Each index is the one
/// guarantee of its rule, so a violation of it is an answer to report rather than a fault.
/// </summary>
public sealed class PersistedMailAccountRecordStoreTests
{
    [Fact]
    public void Violates_TheAddressIndexRefusingASaveBeneathTheProvider_IsRecognized()
    {
        // Arrange
        var failure = new DbUpdateException(
            "save failed",
            UniqueViolation(PersistenceConstraintNames.MailAccountRecordAddressUniqueIndexName));

        // Act
        var violates = PersistedMailAccountRecordStore.Violates(failure, PersistenceConstraintNames.MailAccountRecordAddressUniqueIndexName);

        // Assert
        Assert.True(violates);
    }

    [Theory]
    [InlineData(PersistenceConstraintNames.MailAccountAssignmentUserForeignKeyName)]
    [InlineData("ix_some_other_unique_index")]
    public void Violates_AnotherConstraintRefusingTheSave_IsNotMistakenForAHeldAddress(string constraintName)
    {
        // Arrange
        var failure = new DbUpdateException("save failed", UniqueViolation(constraintName));

        // Act
        var violates = PersistedMailAccountRecordStore.Violates(failure, PersistenceConstraintNames.MailAccountRecordAddressUniqueIndexName);

        // Assert
        Assert.False(violates);
    }

    /// <summary>
    /// A second user for an account in no organization, refused by the index as an assignment raises it — straight from
    /// the provider rather than wrapped — is told apart from the address index refusing a save.
    /// </summary>
    [Fact]
    public void Violates_TheSoleUserIndexRefusingAnAssignment_IsRecognizedAsThatIndexAndNotAsAHeldAddress()
    {
        // Arrange
        var failure = UniqueViolation(PersistenceConstraintNames.MailAccountAssignmentSoleUserIndexName);

        // Act
        var soleUser = PersistedMailAccountRecordStore.Violates(failure, PersistenceConstraintNames.MailAccountAssignmentSoleUserIndexName);
        var heldAddress = PersistedMailAccountRecordStore.Violates(failure, PersistenceConstraintNames.MailAccountRecordAddressUniqueIndexName);

        // Assert
        Assert.True(soleUser);
        Assert.False(heldAddress);
    }

    [Fact]
    public void Violates_AFailureTheServerNeverAnswered_IsNotMistakenForAHeldAddress()
    {
        // Arrange
        var failure = new DbUpdateException("save failed", new TimeoutException());

        // Act
        var violates = PersistedMailAccountRecordStore.Violates(failure, PersistenceConstraintNames.MailAccountRecordAddressUniqueIndexName);

        // Assert
        Assert.False(violates);
    }

    private static PostgresException UniqueViolation(string constraintName) => new(
        messageText: "duplicate key value violates unique constraint",
        severity: "ERROR",
        invariantSeverity: "ERROR",
        sqlState: PostgresErrorCodes.UniqueViolation,
        constraintName: constraintName);
}
