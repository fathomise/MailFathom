// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Presence;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Presence;

/// <summary>Covers the member's own presence: renewing and closing clients, choosing a state, the status message and work mode, and who is told when it moves.</summary>
public sealed class OwnPresenceTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000a1");

    private static readonly UserId Member = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000d001"));

    private static readonly UserId Colleague = UserId.Create(new Guid("0198f0aa-0000-7000-8000-00000000d002"));

    private static readonly Guid Laptop = new("0198f0aa-0000-7000-8000-00000000e001");

    private static readonly Guid Phone = new("0198f0aa-0000-7000-8000-00000000e002");

    private readonly FakeTimeProvider clock = new(Now);

    private readonly InMemoryPresenceStore store = new InMemoryPresenceStore().WithOrganization(Organization, Member, Colleague);

    private readonly RecordingClientSignalChannel channel = new();

    private readonly ClientSignals signals;

    public OwnPresenceTests() => this.signals = new ClientSignals([this.channel], this.clock);

    [Fact]
    public async Task RenewAsync_FirstClientOpened_IsActiveAndTellsOnlyTheMembersOwnClients()
    {
        // Act
        var outcome = await this.Presence().RenewAsync(Laptop, ClientActivity.Active, TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Equal(PresenceWriteOutcome.Recorded, outcome);
        Assert.Equal(PresenceState.Active, this.store.Of(Member).OwnStateAt(Now));
        var signal = Assert.Single(this.channel.Published);
        Assert.Equal(ClientSignalKind.PresenceChanged, signal.Kind);
        Assert.Equal(Member, signal.User);
    }

    [Fact]
    public async Task RenewAsync_NothingTheOthersSeeMoved_TellsNobody()
    {
        // Arrange
        this.store.WithClient(Member, Laptop, ClientActivity.Active, Now.AddSeconds(30));

        // Act
        await this.Presence().RenewAsync(Laptop, ClientActivity.Active, TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Empty(this.channel.Published);
    }

    [Fact]
    public async Task RenewAsync_Present_MovesTheLastSeenTime()
    {
        // Act
        await this.Presence().RenewAsync(Laptop, ClientActivity.Idle, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(Now, this.store.Of(Member).LastSeen);
    }

    /// <summary>A time that kept moving while a member chose to hide would tell anybody watching it that they were around.</summary>
    [Fact]
    public async Task RenewAsync_WhileHidden_LeavesTheLastSeenTimeWhereItWas()
    {
        // Arrange
        var earlier = Now.AddHours(-2);
        this.store
            .WithChoice(Member, new PresenceChoice(PresenceState.Hidden, Duration: null, EndsAt: null))
            .WithLastSeen(Member, earlier);

        // Act
        await this.Presence().RenewAsync(Laptop, ClientActivity.Active, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(earlier, this.store.Of(Member).LastSeen);
    }

    /// <summary>The losing attempt of the bound: a client past the most a member keeps open is refused and nothing about it is written.</summary>
    [Fact]
    public async Task RenewAsync_ANewClientPastTheBound_IsRefusedAndWritesNothing()
    {
        // Arrange
        foreach (var held in Enumerable.Range(0, MemberPresence.MostOpenClients).Select(index => new Guid(index + 1, 0, 0, new byte[8])))
        {
            this.store.WithClient(Member, held, ClientActivity.Active, Now.AddSeconds(30));
        }

        // Act
        var outcome = await this.Presence().RenewAsync(Laptop, ClientActivity.Active, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresenceWriteOutcome.TooManyClients, outcome);
        Assert.Equal(MemberPresence.MostOpenClients, this.store.Of(Member).Clients.Count);
    }

    [Fact]
    public async Task RenewAsync_AnExpiredClientOfTheMember_DoesNotCountTowardsTheBound()
    {
        // Arrange
        foreach (var held in Enumerable.Range(0, MemberPresence.MostOpenClients).Select(index => new Guid(index + 1, 0, 0, new byte[8])))
        {
            this.store.WithClient(Member, held, ClientActivity.Active, Now);
        }

        // Act
        var outcome = await this.Presence().RenewAsync(Laptop, ClientActivity.Active, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresenceWriteOutcome.Recorded, outcome);
        Assert.Single(this.store.Of(Member).Clients);
    }

    [Fact]
    public async Task RenewAsync_CallerInNoOrganization_RecordsNothing()
    {
        // Act
        var outcome = await this.Presence(inOrganization: false).RenewAsync(Laptop, ClientActivity.Active, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresenceWriteOutcome.NotInOrganization, outcome);
        Assert.Empty(this.store.Of(Member).Clients);
        Assert.Null(this.store.Of(Member).LastSeen);
    }

    [Fact]
    public async Task RenewAsync_TwiceWithinAClientLifetime_SweepsExpiredClientsOnce()
    {
        // Arrange
        var presence = this.Presence();

        // Act
        await presence.RenewAsync(Laptop, ClientActivity.Active, TestContext.Current.CancellationToken);
        await presence.RenewAsync(Phone, ClientActivity.Active, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, this.store.Sweeps);
    }

    [Fact]
    public async Task CloseAsync_TheLastOpenClient_ReadsOfflineAndTellsOnlyTheMembersOwnClients()
    {
        // Arrange
        this.store.WithClient(Member, Laptop, ClientActivity.Active, Now.AddSeconds(30));

        // Act
        await this.Presence().CloseAsync(Laptop, TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Equal(PresenceState.Offline, this.store.Of(Member).OwnStateAt(Now));
        Assert.Equal(Member, Assert.Single(this.channel.Published).User);
    }

    [Fact]
    public async Task CloseAsync_AnotherClientStillOpen_TellsNobody()
    {
        // Arrange
        this.store
            .WithClient(Member, Laptop, ClientActivity.Active, Now.AddSeconds(30))
            .WithClient(Member, Phone, ClientActivity.Active, Now.AddSeconds(30));

        // Act
        await this.Presence().CloseAsync(Laptop, TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Equal(PresenceState.Active, this.store.Of(Member).OwnStateAt(Now));
        Assert.Empty(this.channel.Published);
    }

    [Fact]
    public async Task ChooseAsync_BusyForAnHour_IsBusyUntilThenAndTellsOnlyTheMembersOwnClients()
    {
        // Arrange
        this.store.WithClient(Member, Laptop, ClientActivity.Active, Now.AddSeconds(30));

        // Act
        var reading = await this.Presence().ChooseAsync(PresenceState.Busy, PresenceDuration.OneHour, TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Equal(new OwnPresenceReading(PresenceState.Busy, new PresenceChoice(PresenceState.Busy, PresenceDuration.OneHour, Now.AddHours(1))), reading);
        Assert.Equal(Member, Assert.Single(this.channel.Published).User);
    }

    [Fact]
    public async Task ChooseAsync_Hidden_IsHiddenToTheMemberAndOfflineToEverybodyElse()
    {
        // Arrange
        this.store.WithClient(Member, Laptop, ClientActivity.Active, Now.AddSeconds(30));

        // Act
        var reading = await this.Presence().ChooseAsync(PresenceState.Hidden, duration: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(PresenceState.Hidden, reading?.State);
        Assert.Equal(new PresenceReading(PresenceState.Offline, LastSeen: null, UsesPhoneApp: false), this.store.Of(Member).SeenByOthersAt(Now));
    }

    [Fact]
    public async Task ChooseAsync_ActiveUntilChanged_EndsAnEarlierChoice()
    {
        // Arrange
        this.store
            .WithClient(Member, Laptop, ClientActivity.Idle, Now.AddSeconds(30))
            .WithChoice(Member, new PresenceChoice(PresenceState.DoNotDisturb, PresenceDuration.TwoHours, Now.AddHours(2)));

        // Act
        var reading = await this.Presence().ChooseAsync(PresenceState.Active, duration: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new OwnPresenceReading(PresenceState.Away, Choice: null), reading);
    }

    [Fact]
    public async Task ChooseAsync_CallerInNoOrganization_AnswersNothingAndRecordsNothing()
    {
        // Act
        var reading = await this.Presence(inOrganization: false).ChooseAsync(PresenceState.Busy, duration: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading);
        Assert.Null(this.store.Of(Member).Choice);
    }

    [Fact]
    public async Task ChooseAsync_WithoutTheWritingGrant_IsRefused()
    {
        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            this.Presence(granted: MailFathomPermission.ProfileRead)
                .ChooseAsync(PresenceState.Busy, duration: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_ChoiceRunOut_ReadsTheAutomaticPresence()
    {
        // Arrange
        this.store
            .WithClient(Member, Laptop, ClientActivity.Active, Now.AddSeconds(30))
            .WithChoice(Member, new PresenceChoice(PresenceState.Busy, PresenceDuration.ThirtyMinutes, Now));

        // Act
        var reading = await this.Presence().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new OwnPresenceReading(PresenceState.Active, Choice: null), reading);
    }

    [Fact]
    public async Task WriteStatusMessageAsync_ClearingToday_IsShownToTheOrganizationUntilMidnightAndTellsOnlyTheMembersOwnClients()
    {
        // Act
        var reading = await this.Presence().WriteStatusMessageAsync("  In workshops until 15:00 ", PresenceDuration.Today, TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        var expected = new StatusMessage("In workshops until 15:00", PresenceDuration.Today, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(expected, reading?.Message);
        Assert.Equal(expected, this.store.Of(Member).SeenByOthersAt(Now).Message);
        Assert.Equal(Member, Assert.Single(this.channel.Published).User);
    }

    [Fact]
    public async Task WriteStatusMessageAsync_NothingButSpaces_ClearsTheMessage()
    {
        // Arrange
        await this.Presence().WriteStatusMessageAsync("Back at noon", clears: null, TestContext.Current.CancellationToken);

        // Act
        var reading = await this.Presence().WriteStatusMessageAsync("   ", clears: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading?.Message);
        Assert.Null(this.store.Of(Member).Message);
    }

    [Fact]
    public async Task WriteStatusMessageAsync_CallerInNoOrganization_AnswersNothingAndRecordsNothing()
    {
        // Act
        var reading = await this.Presence(inOrganization: false).WriteStatusMessageAsync("Back at noon", clears: null, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading);
        Assert.Null(this.store.Of(Member).Message);
    }

    [Fact]
    public async Task WriteStatusMessageAsync_WithoutTheWritingGrant_IsRefused()
    {
        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            this.Presence(granted: MailFathomPermission.ProfileRead)
                .WriteStatusMessageAsync("Back at noon", clears: null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChooseWorkModeAsync_RemoteToday_HoldsUntilTheMembersMidnightAndTellsOnlyTheMembersOwnClients()
    {
        // Act
        var reading = await this.Presence().ChooseWorkModeAsync(WorkMode.Remote, todayOnly: true, TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Equal(new WorkModeChoice(WorkMode.Remote, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)), reading?.WorkMode);
        Assert.Equal(Member, Assert.Single(this.channel.Published).User);
    }

    [Fact]
    public async Task ChooseWorkModeAsync_CallerInNoOrganization_AnswersNothingAndRecordsNothing()
    {
        // Act
        var reading = await this.Presence(inOrganization: false).ChooseWorkModeAsync(WorkMode.Office, todayOnly: false, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading);
        Assert.Null(this.store.Of(Member).WorkMode);
    }

    [Fact]
    public async Task ChooseWorkModeAsync_WithoutTheWritingGrant_IsRefused()
    {
        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(() =>
            this.Presence(granted: MailFathomPermission.ProfileRead)
                .ChooseWorkModeAsync(WorkMode.Office, todayOnly: false, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadAsync_WorkModeForADayThatEnded_ReadsNone()
    {
        // Arrange
        await this.Presence().ChooseWorkModeAsync(WorkMode.Office, todayOnly: true, TestContext.Current.CancellationToken);
        this.clock.Advance(TimeSpan.FromHours(15));

        // Act
        var reading = await this.Presence().ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(reading?.WorkMode);
    }

    public ValueTask DisposeAsync() => this.signals.DisposeAsync();

    private OwnPresence Presence(bool inOrganization = true, params MailFathomPermission[] granted)
    {
        var authorization = AccessAuthorizations.ForUserGranted(
            Member,
            granted.Length == 0 ? [MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite] : granted);
        var ownership = ContactBookOwnerships.For(
            authorization,
            new StubMailAccountAssignments(),
            new StatedAdministrativeTargets().WithUser(Member, inOrganization ? Organization : null));
        var zones = Substitute.For<IUserTimeZones>();
        zones.ZoneOf(Arg.Any<UserId>()).Returns(UserTimeZone.Coordinated);

        return new OwnPresence(this.store, this.signals, ownership, authorization, zones, new PresenceSweepSchedule(), this.clock);
    }
}
