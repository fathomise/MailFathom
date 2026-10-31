// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.AutomaticReplies;
using Xunit;

namespace MailFathom.Domain.UnitTests.AutomaticReplies;

/// <summary>Covers until when a user's colleagues are told they are out of office.</summary>
public sealed class AutomaticReplyPeriodTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset End = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);

    /// <summary>A colleague is out of office over the reply's half-open period alone, and is told its end.</summary>
    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(60, true)]
    [InlineData(7 * 24 * 60, false)]
    public void OutOfOfficeUntil_AnInstantAgainstAReplySwitchedOn_IsTheEndWithinThePeriodAlone(int minutesFromStart, bool outOfOffice)
    {
        // Arrange
        var period = new AutomaticReplyPeriod(Start, End, IsOn: true);

        // Act
        var until = period.OutOfOfficeUntil(Start.AddMinutes(minutesFromStart));

        // Assert
        Assert.Equal(outOfOffice ? End : null, until);
    }

    /// <summary>A reply switched off before its period ends says nobody is away, inside the period included.</summary>
    [Fact]
    public void OutOfOfficeUntil_SwitchedOff_IsNothing()
    {
        // Arrange
        var period = new AutomaticReplyPeriod(Start, End, IsOn: false);

        // Act
        var until = period.OutOfOfficeUntil(Start.AddHours(1));

        // Assert
        Assert.Null(until);
    }
}
