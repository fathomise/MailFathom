// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Tasks;
using MailFathom.Domain.Calendar;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Tasks;
using Xunit;

namespace MailFathom.Application.UnitTests.Tasks;

/// <summary>Covers which narrowings a store answers, and which due days a window reaches.</summary>
public sealed class PersonalTaskQueryTests
{
    private static readonly TimeSpan Warsaw = TimeSpan.FromHours(1);

    private static readonly DateTimeOffset Opens = new(2026, 11, 2, 0, 0, 0, Warsaw);

    /// <summary>
    /// An undeclared origin, a default thread, event, and address, each half of a window, an empty and an inverted
    /// window, and one a minute past the longest.
    /// </summary>
    private static readonly PersonalTaskQuery[] UnanswerableQueries =
    [
        new PersonalTaskQuery((PersonalTaskOrigin)99),
        new PersonalTaskQuery(null, Thread: default(EmailThreadId)),
        new PersonalTaskQuery(null, Event: default(CalendarEventId)),
        new PersonalTaskQuery(null, Counterpart: default(EmailAddress)),
        new PersonalTaskQuery(null, From: Opens),
        new PersonalTaskQuery(null, Until: Opens),
        new PersonalTaskQuery(null, From: Opens, Until: Opens),
        new PersonalTaskQuery(null, From: Opens, Until: Opens.AddDays(-1)),
        new PersonalTaskQuery(null, From: Opens, Until: Opens.AddDays(PersonalTaskQuery.LongestWindowDays).AddMinutes(1)),
    ];

    /// <summary>A query stating nothing but an origin is the list itself, which every store answers.</summary>
    [Fact]
    public void EnsureAnswerable_AnOriginAlone_IsAccepted()
    {
        // Arrange
        var query = new PersonalTaskQuery(PersonalTaskOrigin.Asserted);

        // Act
        var exception = Record.Exception(query.EnsureAnswerable);

        // Assert
        Assert.Null(exception);
    }

    /// <summary>Two months of a calendar is the longest window, so one exactly that long is still answered.</summary>
    [Fact]
    public void EnsureAnswerable_AWindowOfExactlySixtyTwoDays_IsAccepted()
    {
        // Arrange
        var query = new PersonalTaskQuery(null, From: Opens, Until: Opens.AddDays(PersonalTaskQuery.LongestWindowDays));

        // Act
        var exception = Record.Exception(query.EnsureAnswerable);

        // Assert
        Assert.Null(exception);
    }

    /// <summary>Every narrowing no store should answer is refused rather than read as something else.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public void EnsureAnswerable_AQueryNoStoreShouldAnswer_IsRefused(int unanswerable)
    {
        // Arrange
        var query = UnanswerableQueries[unanswerable];

        // Act
        var answering = () => query.EnsureAnswerable();

        // Assert
        Assert.Throws<ArgumentException>(answering);
    }

    /// <summary>The days are read in the offset the window opens in, not in UTC, where Monday midnight is still Sunday.</summary>
    [Fact]
    public void DueDays_AWindowOfOneDayAheadOfUtc_ReachesThatDayInItsOwnOffset()
    {
        // Arrange
        var query = new PersonalTaskQuery(null, From: Opens, Until: Opens.AddDays(1));

        // Act
        var days = query.DueDays;

        // Assert
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2)), days);
    }

    /// <summary>A window closing partway through a day reaches that day, because some of it is inside.</summary>
    [Fact]
    public void DueDays_AWindowClosingAtMidday_ReachesTheDayItClosesOn()
    {
        // Arrange
        var query = new PersonalTaskQuery(null, From: Opens, Until: Opens.AddDays(2).AddHours(12));

        // Act
        var days = query.DueDays;

        // Assert
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4)), days);
    }

    /// <summary>A close stated in another offset is read in the offset the window opens in, so it names the same day.</summary>
    [Fact]
    public void DueDays_AClosingStatedInUtc_IsReadInTheOffsetTheWindowOpensIn()
    {
        // Arrange
        var closesAtWarsawMidnight = new DateTimeOffset(2026, 11, 3, 23, 0, 0, TimeSpan.Zero);
        var query = new PersonalTaskQuery(null, From: Opens, Until: closesAtWarsawMidnight);

        // Act
        var days = query.DueDays;

        // Assert
        Assert.Equal((new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 3)), days);
    }

    /// <summary>A query stating no window reaches no due days at all.</summary>
    [Fact]
    public void DueDays_NoWindow_IsAbsent()
    {
        // Act
        var days = new PersonalTaskQuery(null).DueDays;

        // Assert
        Assert.Null(days);
    }
}
