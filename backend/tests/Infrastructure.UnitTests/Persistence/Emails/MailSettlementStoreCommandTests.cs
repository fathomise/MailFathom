// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Emails;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Emails;

/// <summary>Covers what the settlement asks PostgreSQL for, which the C# it is written in does not show.</summary>
public sealed class MailSettlementStoreCommandTests
{
    /// <summary>The read walks the settling queue and nothing else, which is what its filtered index was built for.</summary>
    [Fact]
    public void Settling_AnAccount_ReadsOnlyTheRowsStillSettling()
    {
        // Act
        var command = SettlingCommand();

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.SettledAt)}\" IS NULL", command, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether a move is still on its way is one probe of the mutation records, translated rather than evaluated here. It
    /// reads only records that commit nothing locally, which is a mirrored account's: a held account opens none, and a
    /// restoring one marks its record as already committed — so the phase is told apart without being read.
    /// </summary>
    [Fact]
    public void Settling_AnAccount_AsksTheMutationRecordsWhetherAMoveIsStillOnItsWay()
    {
        // Act
        var command = SettlingCommand();

        // Assert
        Assert.Contains("EXISTS", command, StringComparison.Ordinal);
        Assert.Contains("mailbox_mutations", command, StringComparison.Ordinal);
        Assert.Contains($"\"{nameof(MailboxMutationEntity.PlacementObservedAt)}\" IS NULL", command, StringComparison.Ordinal);
        Assert.Contains($"\"{nameof(MailboxMutationEntity.LocalChange)}\"", command, StringComparison.Ordinal);
    }

    /// <summary>The write is conditional on the row still settling, which is what makes a competing run's settlement a no-op.</summary>
    [Fact]
    public void Compose_AnySettlement_SettlesOnlyRowsStillSettlingAndReturnsThem()
    {
        // Act
        var statement = MailSettlementStatement.Compose([Guid.Empty], DateTimeOffset.UnixEpoch).Format;

        // Assert
        Assert.Contains($"\"{nameof(StoredEmailEntity.SettledAt)}\" IS NULL", statement, StringComparison.Ordinal);
        Assert.Contains("RETURNING \"Id\" AS \"Value\"", statement, StringComparison.Ordinal);
    }

    private static string SettlingCommand()
    {
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        return MailSettlementStore
            .Settling(context, MailAccountId.Create("primary"), resumeAfter: null, batchSize: 200)
            .ToQueryString();
    }
}
