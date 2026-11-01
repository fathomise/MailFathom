// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Presence;
using Xunit;

namespace MailFathom.Domain.UnitTests.Presence;

/// <summary>Covers how a member's presence is worked out from their clients, their choice, and the reader.</summary>
public sealed class MemberPresenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Earlier = Now.AddHours(-3);

    [Fact]
    public void OwnStateAt_NoClientOpen_IsOffline()
    {
        // Arrange
        var presence = new MemberPresence([], Choice: null, LastSeen: Earlier);

        // Act
        var state = presence.OwnStateAt(Now);

        // Assert
        Assert.Equal(PresenceState.Offline, state);
    }

    [Theory]
    [InlineData(ClientActivity.Active, PresenceState.Active)]
    [InlineData(ClientActivity.Idle, PresenceState.Away)]
    public void OwnStateAt_OneClientOpen_FollowsItsActivity(ClientActivity activity, PresenceState expected)
    {
        // Arrange
        var presence = new MemberPresence([Open(activity)], Choice: null, LastSeen: null);

        // Act
        var state = presence.OwnStateAt(Now);

        // Assert
        Assert.Equal(expected, state);
    }

    [Fact]
    public void OwnStateAt_OneDeviceActiveAndAnotherIdle_TheMostPresentWins()
    {
        // Arrange
        var presence = new MemberPresence([Open(ClientActivity.Idle), Open(ClientActivity.Active)], Choice: null, LastSeen: null);

        // Act
        var state = presence.OwnStateAt(Now);

        // Assert
        Assert.Equal(PresenceState.Active, state);
    }

    /// <summary>The case of the replica that died: nothing deleted its row, and the row stops counting at its own expiry.</summary>
    [Fact]
    public void OwnStateAt_EveryClientPastItsExpiry_IsOffline()
    {
        // Arrange
        var presence = new MemberPresence(
            [new OpenPresenceClient(ClientActivity.Active, Now), new OpenPresenceClient(ClientActivity.Idle, Now.AddSeconds(-1))],
            Choice: null,
            LastSeen: Earlier);

        // Act
        var state = presence.OwnStateAt(Now);

        // Assert
        Assert.Equal(PresenceState.Offline, state);
    }

    [Theory]
    [InlineData(PresenceState.Busy)]
    [InlineData(PresenceState.DoNotDisturb)]
    [InlineData(PresenceState.Away)]
    [InlineData(PresenceState.Offline)]
    [InlineData(PresenceState.Hidden)]
    public void OwnStateAt_ChoiceInEffectWithAClientOpen_IsTheChosenState(PresenceState chosen)
    {
        // Arrange
        var presence = new MemberPresence([Open(ClientActivity.Active)], new PresenceChoice(chosen, Duration: null, EndsAt: null), LastSeen: null);

        // Act
        var state = presence.OwnStateAt(Now);

        // Assert
        Assert.Equal(chosen, state);
    }

    [Fact]
    public void OwnStateAt_ChosenActiveForADurationWhileIdle_StaysActive()
    {
        // Arrange
        var presence = new MemberPresence(
            [Open(ClientActivity.Idle)],
            new PresenceChoice(PresenceState.Active, PresenceDuration.ThirtyMinutes, Now.AddMinutes(5)),
            LastSeen: null);

        // Act
        var state = presence.OwnStateAt(Now);

        // Assert
        Assert.Equal(PresenceState.Active, state);
    }

    [Fact]
    public void OwnStateAt_EveryClientClosedOverAChoice_IsOffline()
    {
        // Arrange
        var presence = new MemberPresence([], new PresenceChoice(PresenceState.Busy, Duration: null, EndsAt: null), LastSeen: Earlier);

        // Act
        var state = presence.OwnStateAt(Now);

        // Assert
        Assert.Equal(PresenceState.Offline, state);
    }

    [Fact]
    public void OwnStateAt_ChoiceRunOut_IsBackToWhatTheClientsProduce()
    {
        // Arrange
        var presence = new MemberPresence([Open(ClientActivity.Idle)], new PresenceChoice(PresenceState.Busy, PresenceDuration.ThirtyMinutes, Now), LastSeen: null);

        // Act
        var state = presence.OwnStateAt(Now);

        // Assert
        Assert.Equal(PresenceState.Away, state);
    }

    [Fact]
    public void SeenByOthersAt_HiddenWithAClientOpen_IsOfflineWithNoLastSeen()
    {
        // Arrange
        var presence = new MemberPresence([Open(ClientActivity.Active)], new PresenceChoice(PresenceState.Hidden, Duration: null, EndsAt: null), Earlier);

        // Act
        var reading = presence.SeenByOthersAt(Now);

        // Assert
        Assert.Equal(new PresenceReading(PresenceState.Offline, LastSeen: null, UsesPhoneApp: false), reading);
    }

    [Fact]
    public void SeenByOthersAt_HiddenWithEveryClientClosed_ShowsNoLastSeen()
    {
        // Arrange
        var presence = new MemberPresence([], new PresenceChoice(PresenceState.Hidden, Duration: null, EndsAt: null), Earlier);

        // Act
        var reading = presence.SeenByOthersAt(Now);

        // Assert
        Assert.Equal(new PresenceReading(PresenceState.Offline, LastSeen: null, UsesPhoneApp: false), reading);
    }

    [Fact]
    public void SeenByOthersAt_Offline_ShowsTheLastSeenTime()
    {
        // Arrange
        var presence = new MemberPresence([], Choice: null, Earlier);

        // Act
        var reading = presence.SeenByOthersAt(Now);

        // Assert
        Assert.Equal(new PresenceReading(PresenceState.Offline, Earlier, UsesPhoneApp: false), reading);
    }

    [Fact]
    public void SeenByOthersAt_Present_ShowsNoLastSeen()
    {
        // Arrange
        var presence = new MemberPresence([Open(ClientActivity.Active)], Choice: null, Earlier);

        // Act
        var reading = presence.SeenByOthersAt(Now);

        // Assert
        Assert.Equal(new PresenceReading(PresenceState.Active, LastSeen: null, UsesPhoneApp: false), reading);
    }

    [Theory]
    [InlineData(PresenceState.Hidden, false)]
    [InlineData(PresenceState.Offline, false)]
    [InlineData(PresenceState.Busy, true)]
    [InlineData(PresenceState.Away, true)]
    public void AdvancesLastSeenAt_ChoiceInEffect_HoldsStillWhileTheMemberLooksAbsent(PresenceState chosen, bool expected)
    {
        // Arrange
        var presence = new MemberPresence([Open(ClientActivity.Active)], new PresenceChoice(chosen, Duration: null, EndsAt: null), Earlier);

        // Act
        var advances = presence.AdvancesLastSeenAt(Now);

        // Assert
        Assert.Equal(expected, advances);
    }

    [Fact]
    public void AdvancesLastSeenAt_HiddenChoiceRunOut_MovesAgain()
    {
        // Arrange
        var presence = new MemberPresence([Open(ClientActivity.Active)], new PresenceChoice(PresenceState.Hidden, PresenceDuration.ThirtyMinutes, Now), Earlier);

        // Act
        var advances = presence.AdvancesLastSeenAt(Now);

        // Assert
        Assert.True(advances);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void SeenByOthersAt_PhoneAppOpenedDaysAgo_MarksOnlyWithinSevenDays(int daysAgo, bool expected)
    {
        // Arrange
        var presence = new MemberPresence([], Choice: null, LastSeen: null)
        {
            PhoneAppSessions = [new PhoneAppSession(Today.AddDays(-daysAgo), Now.AddDays(20))],
        };

        // Act
        var reading = presence.SeenByOthersAt(Now);

        // Assert
        Assert.Equal(expected, reading.UsesPhoneApp);
    }

    [Fact]
    public void SeenByOthersAt_PhoneAppSessionExpired_DoesNotMark()
    {
        // Arrange
        var presence = new MemberPresence([], Choice: null, LastSeen: null)
        {
            PhoneAppSessions = [new PhoneAppSession(Today, Now.AddSeconds(-1))],
        };

        // Act
        var reading = presence.SeenByOthersAt(Now);

        // Assert
        Assert.False(reading.UsesPhoneApp);
    }

    [Fact]
    public void SeenByOthersAt_NoPhoneAppSessionLeft_DoesNotMark()
    {
        // Arrange
        var presence = new MemberPresence([Open(ClientActivity.Active)], Choice: null, LastSeen: null);

        // Act
        var reading = presence.SeenByOthersAt(Now);

        // Assert
        Assert.False(reading.UsesPhoneApp);
    }

    [Theory]
    [InlineData(PresenceState.Hidden, false)]
    [InlineData(PresenceState.Offline, true)]
    [InlineData(PresenceState.DoNotDisturb, true)]
    public void SeenByOthersAt_PhoneAppUsedUnderAChoice_IsHiddenOnlyByHiding(PresenceState chosen, bool expected)
    {
        // Arrange
        var presence = new MemberPresence([], new PresenceChoice(chosen, Duration: null, EndsAt: null), Earlier)
        {
            PhoneAppSessions = [new PhoneAppSession(Today, Now.AddDays(20))],
        };

        // Act
        var reading = presence.SeenByOthersAt(Now);

        // Assert
        Assert.Equal(expected, reading.UsesPhoneApp);
    }

    private static DateOnly Today => DateOnly.FromDateTime(Now.UtcDateTime);

    private static OpenPresenceClient Open(ClientActivity activity) => new(activity, Now.AddSeconds(30));
}
