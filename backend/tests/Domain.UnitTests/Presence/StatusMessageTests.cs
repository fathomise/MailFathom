// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;
using Xunit;

namespace MailFathom.Domain.UnitTests.Presence;

/// <summary>Covers how a status message is written: its bound, what it refuses, and when it clears.</summary>
public sealed class StatusMessageTests
{
    private static readonly DateTimeOffset WrittenAt = Instant("2026-10-31T09:00:00Z");

    private static readonly UserTimeZone Warsaw =
        UserTimeZone.TryRead("Europe/Warsaw", out var zone) ? zone : throw new InvalidOperationException("The test host does not know Europe/Warsaw.");

    [Fact]
    public void Written_AtTheBound_IsKeptTrimmed()
    {
        // Arrange
        var text = new string('a', StatusMessage.MostCharacters);

        // Act
        var message = StatusMessage.Written($"  {text} ", clears: null, WrittenAt, Warsaw);

        // Assert
        Assert.Equal(new StatusMessage(text, Clears: null, ClearsAt: null), message);
    }

    [Fact]
    public void Written_PastTheBound_IsRefused()
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() =>
            StatusMessage.Written(new string('a', StatusMessage.MostCharacters + 1), clears: null, WrittenAt, Warsaw));
    }

    [Theory]
    [InlineData("Two\nlines")]
    [InlineData("A\u0007bell")]
    [InlineData("Two\u2028lines")]
    [InlineData("Out \u202Eeciffo\u202C")]
    public void Written_HoldingWhatRendersAsSomethingOtherThanText_IsRefused(string text)
    {
        // Act, Assert
        Assert.Throws<ArgumentException>(() => StatusMessage.Written(text, clears: null, WrittenAt, Warsaw));
    }

    [Fact]
    public void Written_HoldingAnUnpairedSurrogate_IsRefused()
    {
        // Arrange
        var text = "Half " + '\uD83D' + " pair";

        // Act, Assert
        Assert.Throws<ArgumentException>(() => StatusMessage.Written(text, clears: null, WrittenAt, Warsaw));
    }

    [Fact]
    public void Written_AJoinerInsideAWord_IsKept()
    {
        // Arrange
        const string text = "Working on \u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645";

        // Act
        var message = StatusMessage.Written(text, clears: null, WrittenAt, Warsaw);

        // Assert
        Assert.Equal(text, message?.Text);
    }

    [Fact]
    public void Written_NothingButWhiteSpace_ClearsTheMessage()
    {
        // Act
        var message = StatusMessage.Written(" \t ", PresenceDuration.Today, WrittenAt, Warsaw);

        // Assert
        Assert.Null(message);
    }

    [Theory]
    [InlineData(PresenceDuration.OneHour, "2026-10-31T10:00:00Z")]
    [InlineData(PresenceDuration.Today, "2026-10-31T23:00:00Z")]
    [InlineData(PresenceDuration.ThisWeek, "2026-11-01T23:00:00Z")]
    public void Written_ClearingAfterAWhile_ClearsWhenThatWhileEndsInTheMembersZone(PresenceDuration clears, string expected)
    {
        // Act
        var message = StatusMessage.Written("In workshops", clears, WrittenAt, Warsaw);

        // Assert
        Assert.Equal(new StatusMessage("In workshops", clears, Instant(expected)), message);
    }

    [Fact]
    public void IsInEffectAt_AtTheInstantItClears_IsShownNoLonger()
    {
        // Arrange
        var message = new StatusMessage("In workshops", PresenceDuration.OneHour, Instant("2026-10-31T10:00:00Z"));

        // Act, Assert
        Assert.True(message.IsInEffectAt(Instant("2026-10-31T09:59:59Z")));
        Assert.False(message.IsInEffectAt(Instant("2026-10-31T10:00:00Z")));
    }

    private static DateTimeOffset Instant(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);
}
