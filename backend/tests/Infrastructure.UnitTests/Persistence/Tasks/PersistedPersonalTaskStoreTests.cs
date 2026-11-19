// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Tasks;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Tasks;

/// <summary>
/// Covers how the store tells a reply that answered a wait beneath a revision apart from every other failed write. The
/// check is what keeps an answer on a wait, so the database refusing a revision under it is the answer the person
/// would have had if the reply had landed first, rather than a fault.
/// </summary>
public sealed class PersistedPersonalTaskStoreTests
{
    [Fact]
    public void ViolatesTheAnsweredWait_TheAnswerCheckRefusingASaveBeneathTheProvider_IsRecognized()
    {
        // Arrange
        var failure = new DbUpdateException(
            "save failed",
            CheckViolation(PersistenceConstraintNames.PersonalTaskAnswerClosesAWaitCheckConstraintName));

        // Act
        var violates = PersistedPersonalTaskStore.ViolatesTheAnsweredWait(failure);

        // Assert
        Assert.True(violates);
    }

    [Theory]
    [InlineData(PersistenceConstraintNames.PersonalTaskCounterpartCompleteCheckConstraintName)]
    [InlineData("ck_some_other_check")]
    public void ViolatesTheAnsweredWait_AnotherCheckRefusingTheSave_IsNotMistakenForAnAnsweredWait(string constraintName)
    {
        // Arrange
        var failure = new DbUpdateException("save failed", CheckViolation(constraintName));

        // Act
        var violates = PersistedPersonalTaskStore.ViolatesTheAnsweredWait(failure);

        // Assert
        Assert.False(violates);
    }

    [Fact]
    public void ViolatesTheAnsweredWait_AFailureTheServerNeverAnswered_IsNotMistakenForAnAnsweredWait()
    {
        // Arrange
        var failure = new DbUpdateException("save failed", new TimeoutException());

        // Act
        var violates = PersistedPersonalTaskStore.ViolatesTheAnsweredWait(failure);

        // Assert
        Assert.False(violates);
    }

    private static PostgresException CheckViolation(string constraintName) => new(
        messageText: "new row violates check constraint",
        severity: "ERROR",
        invariantSeverity: "ERROR",
        sqlState: PostgresErrorCodes.CheckViolation,
        constraintName: constraintName);
}
