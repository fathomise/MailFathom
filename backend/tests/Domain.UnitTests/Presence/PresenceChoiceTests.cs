// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;
using Xunit;

namespace MailFathom.Domain.UnitTests.Presence;

/// <summary>Covers how a chosen presence and its duration are made, and when the choice stops holding.</summary>
public sealed class PresenceChoiceTests
{
    private static readonly UserTimeZone Warsaw = Zone("Europe/Warsaw");

    [Fact]
    public void Made_ActiveUntilChanged_ReturnsToAutomaticPresence()
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.Active, duration: null, Instant("2026-10-31T09:00:00Z"), Warsaw);

        // Assert
        Assert.Null(choice);
    }

    [Fact]
    public void Made_ActiveForADuration_IsAChoice()
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.Active, PresenceDuration.OneHour, Instant("2026-10-31T09:00:00Z"), Warsaw);

        // Assert
        Assert.Equal(new PresenceChoice(PresenceState.Active, PresenceDuration.OneHour, Instant("2026-10-31T10:00:00Z")), choice);
    }

    [Theory]
    [InlineData(PresenceState.Busy)]
    [InlineData(PresenceState.Hidden)]
    [InlineData(PresenceState.Offline)]
    public void Made_AnyOtherStateUntilChanged_HasNoEnd(PresenceState state)
    {
        // Act
        var choice = PresenceChoice.Made(state, duration: null, Instant("2026-10-31T09:00:00Z"), Warsaw);

        // Assert
        Assert.Equal(new PresenceChoice(state, Duration: null, EndsAt: null), choice);
    }

    [Theory]
    [InlineData(PresenceDuration.ThirtyMinutes, "2026-10-31T09:30:00Z")]
    [InlineData(PresenceDuration.TwoHours, "2026-10-31T11:00:00Z")]
    public void Made_FixedDuration_EndsThatLongAfterTheChoice(PresenceDuration duration, string expected)
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.Busy, duration, Instant("2026-10-31T09:00:00Z"), Warsaw);

        // Assert
        Assert.Equal(Instant(expected), choice?.EndsAt);
    }

    /// <summary>Warsaw is two hours ahead of UTC on a Saturday at the end of October, and moves to one hour that night.</summary>
    [Fact]
    public void Made_Today_EndsAtTheMembersOwnNextMidnight()
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.Busy, PresenceDuration.Today, Instant("2026-10-24T21:30:00Z"), Warsaw);

        // Assert
        Assert.Equal(Instant("2026-10-24T22:00:00Z"), choice?.EndsAt);
    }

    [Theory]
    [InlineData("2026-10-26T08:00:00Z", "2026-11-01T23:00:00Z")]
    [InlineData("2026-11-01T12:00:00Z", "2026-11-01T23:00:00Z")]
    [InlineData("2026-11-01T23:30:00Z", "2026-11-08T23:00:00Z")]
    public void Made_ThisWeek_EndsAtTheMidnightThatEndsTheMembersSunday(string madeAt, string expected)
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.DoNotDisturb, PresenceDuration.ThisWeek, Instant(madeAt), Warsaw);

        // Assert
        Assert.Equal(Instant(expected), choice?.EndsAt);
    }

    /// <summary>Warsaw's day of 25 October 2026 lasts twenty-five hours and its day of 29 March twenty-three, so each ends an hour off the offset it began under.</summary>
    [Theory]
    [InlineData("2026-10-25T10:00:00Z", "2026-10-25T23:00:00Z")]
    [InlineData("2026-03-29T08:00:00Z", "2026-03-29T22:00:00Z")]
    public void Made_TodayOnADaylightSavingChange_EndsAtTheMidnightUnderTheNewOffset(string madeAt, string expected)
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.Busy, PresenceDuration.Today, Instant(madeAt), Warsaw);

        // Assert
        Assert.Equal(Instant(expected), choice?.EndsAt);
    }

    /// <summary>Made on Saturday 24 October under summer time, a week runs across that night's change and ends at midnight under winter time.</summary>
    [Fact]
    public void Made_ThisWeekAcrossADaylightSavingChange_EndsAtTheMidnightUnderTheNewOffset()
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.DoNotDisturb, PresenceDuration.ThisWeek, Instant("2026-10-24T08:00:00Z"), Warsaw);

        // Assert
        Assert.Equal(Instant("2026-10-25T23:00:00Z"), choice?.EndsAt);
    }

    /// <summary>Santiago moves its clocks at midnight itself on 6 September 2026, so that day begins at one o'clock and a choice made the day before ends there.</summary>
    [Fact]
    public void Made_TodayBeforeAMidnightTheZoneSkips_EndsAtTheFirstLocalTimeThatHappens()
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.Busy, PresenceDuration.Today, Instant("2026-09-05T16:00:00Z"), Zone("America/Santiago"));

        // Assert
        Assert.Equal(Instant("2026-09-06T04:00:00Z"), choice?.EndsAt);
    }

    [Fact]
    public void Made_TodayInUtc_EndsAtMidnightUtc()
    {
        // Act
        var choice = PresenceChoice.Made(PresenceState.Busy, PresenceDuration.Today, Instant("2026-10-31T09:00:00Z"), UserTimeZone.Coordinated);

        // Assert
        Assert.Equal(Instant("2026-11-01T00:00:00Z"), choice?.EndsAt);
    }

    [Fact]
    public void IsInEffectAt_AtItsEnd_HoldsNoLonger()
    {
        // Arrange
        var choice = new PresenceChoice(PresenceState.Busy, PresenceDuration.OneHour, Instant("2026-10-31T10:00:00Z"));

        // Act, Assert
        Assert.True(choice.IsInEffectAt(Instant("2026-10-31T09:59:59Z")));
        Assert.False(choice.IsInEffectAt(Instant("2026-10-31T10:00:00Z")));
    }

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static UserTimeZone Zone(string id) =>
        UserTimeZone.TryRead(id, out var zone) ? zone : throw new InvalidOperationException($"The test host does not know {id}.");
}
