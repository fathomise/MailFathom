// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Persistence;
using MailFathom.Infrastructure.Persistence.Accounts;
using MailFathom.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Persistence.Accounts;

/// <summary>
/// Covers the condition a recording updates under, which is the whole of how two replicas' recordings are ordered.
/// PostgreSQL evaluates it in the statement, so the integration suite proves the statement; here the condition is read
/// against rows in each state it has to tell apart, and its translation is read off the command.
/// </summary>
public sealed class MailboxServerCapabilityStoreTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly DateTimeOffset Recorded = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReplaceableBy_ARowNoSessionHasRecordedOn_TakesTheFirstObservation()
    {
        // Act
        var replaceable = IsReplaceable(RowObservedAt(observedAt: null), Recorded);

        // Assert
        Assert.True(replaceable);
    }

    [Fact]
    public void ReplaceableBy_ALaterObservation_RefreshesTheOlderOne()
    {
        // Act
        var replaceable = IsReplaceable(RowObservedAt(Recorded), Recorded.AddMinutes(5));

        // Assert
        Assert.True(replaceable);
    }

    /// <summary>The replica that established first and recorded last must not put back what a later session replaced.</summary>
    [Fact]
    public void ReplaceableBy_AnOlderObservationArrivingLast_LeavesTheNewerOneInPlace()
    {
        // Act
        var replaceable = IsReplaceable(RowObservedAt(Recorded), Recorded.AddMinutes(-5));

        // Assert
        Assert.False(replaceable);
    }

    [Fact]
    public void ReplaceableBy_AnObservationOfAnotherAccount_LeavesThisRowAlone()
    {
        // Arrange
        var row = RowObservedAt(observedAt: null);
        row.Id = "personal";

        // Act
        var replaceable = IsReplaceable(row, Recorded);

        // Assert
        Assert.False(replaceable);
    }

    /// <summary>Only a condition the database evaluates orders two replicas, so it has to be in the command rather than applied afterwards.</summary>
    [Fact]
    public void ReplaceableBy_Always_TranslatesToACommandComparingTheStoredInstant()
    {
        // Arrange
        using var context = new MailFathomDbContextDesignTimeFactory().CreateDbContext([]);

        // Act
        var command = context.MailboxAccounts
            .Where(MailboxServerCapabilityStore.ReplaceableBy(Work, Recorded))
            .ToQueryString();

        // Assert
        var narrowing = command[command.IndexOf("WHERE", StringComparison.Ordinal)..];
        Assert.Contains(nameof(MailboxAccountEntity.ServerCapabilitiesObservedAt), narrowing, StringComparison.Ordinal);
        Assert.Contains("IS NULL", narrowing, StringComparison.Ordinal);
    }

    private static bool IsReplaceable(MailboxAccountEntity row, DateTimeOffset observedAt) =>
        MailboxServerCapabilityStore.ReplaceableBy(Work, observedAt).Compile()(row);

    private static MailboxAccountEntity RowObservedAt(DateTimeOffset? observedAt) => new()
    {
        Id = Work.Value,
        ServerCapabilities = MailboxServerCapabilities.UidPlus,
        ServerCapabilitiesObservedAt = observedAt,
    };
}
