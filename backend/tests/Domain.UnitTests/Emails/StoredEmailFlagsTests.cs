// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Emails;
using Xunit;

namespace MailFathom.Domain.UnitTests.Emails;

/// <summary>Verifies what the flags stored for an email say when nothing has observed them and when something has.</summary>
public sealed class StoredEmailFlagsTests
{
    /// <summary>A row nobody has looked at is told apart from one carrying no flags by the timestamp alone.</summary>
    [Fact]
    public void NeverObserved_CarriesNoFlagsAndReportsThatNothingWasObserved()
    {
        // Act
        var flags = StoredEmailFlags.NeverObserved;

        // Assert
        Assert.Null(flags.ObservedAt);
        Assert.False(flags.WasObserved);
        Assert.Equal(
            [false, false, false, false, false],
            [flags.IsSeen, flags.IsAnswered, flags.IsFlagged, flags.IsDraft, flags.IsDeleted]);
        Assert.Empty(flags.Keywords.Values);
    }

    [Fact]
    public void WasObserved_FlagsCarryingATimestamp_ReportsThatTheyWereRecorded()
    {
        // Arrange
        var observedAt = new DateTimeOffset(2026, 7, 30, 6, 0, 0, TimeSpan.Zero);

        // Act
        var flags = StoredEmailFlags.NeverObserved with { ObservedAt = observedAt };

        // Assert
        Assert.True(flags.WasObserved);
    }
}
