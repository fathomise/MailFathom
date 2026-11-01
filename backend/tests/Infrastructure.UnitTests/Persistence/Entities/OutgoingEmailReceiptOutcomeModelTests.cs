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
/// Asserts what erases a recipient's recorded outcome. The model is built in memory by the real PostgreSQL provider and
/// no connection is opened, so this states what the foreign keys are declared to do; that PostgreSQL then cascades is an
/// integration question.
/// </summary>
public sealed class OutgoingEmailReceiptOutcomeModelTests
{
    /// <summary>
    /// An outcome this deployment saw between two accounts of one organization says something about the recipient's own
    /// copy of the message, so it goes with that copy and with the account holding it, and never outlives either.
    /// </summary>
    [Fact]
    public void OutgoingEmailReceiptOutcomeModel_TheReceivedCopyItWasObservedOn_ErasesItWhenErased()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);
        var outcome = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(OutgoingEmailReceiptOutcomeEntity))!;

        // Act
        var observedIn = outcome.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(StoredEmailEntity));

        // Assert
        Assert.Equal(["ObservedInStoredEmailId"], observedIn.Properties.Select(property => property.Name));
        Assert.False(observedIn.IsRequired);
        Assert.Equal(DeleteBehavior.Cascade, observedIn.DeleteBehavior);
        Assert.Equal(PersistenceConstraintNames.OutgoingEmailReceiptOutcomeObservedInForeignKeyName, observedIn.GetConstraintName());
    }
}
