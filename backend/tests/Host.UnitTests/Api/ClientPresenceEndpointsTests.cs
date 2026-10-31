// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Presence;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Presence;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the presence routes accept from a client and how each outcome is answered.</summary>
/// <remarks>The rules presence follows are <c>OwnPresence</c>'s and the domain's, covered there; what is asserted here is the wire above them.</remarks>
public sealed class ClientPresenceEndpointsTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid Organization = new("0198f0aa-0000-7000-8000-0000000000a1");

    private static readonly Guid Laptop = new("0198f0aa-0000-7000-8000-00000000e001");

    private readonly IPresenceStore store = Substitute.For<IPresenceStore>();

    private readonly FakeTimeProvider clock = new(Now);

    private readonly ClientSignals signals;

    public ClientPresenceEndpointsTests()
    {
        this.signals = new ClientSignals([new RecordingClientSignalChannel()], this.clock);
        this.store.ReadAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<UserId, MemberPresence>());
        this.store.ReadOrganizationUsersAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    [Theory]
    [InlineData("active")]
    [InlineData("idle")]
    public async Task RenewAsync_AnActivityItKnows_AnswersNoContent(string activity)
    {
        // Arrange
        this.store.RenewClientAsync(default, default, default, default, default, default).ReturnsForAnyArgs(true);

        // Act
        var result = await ClientPresenceEndpoints.RenewAsync(
            Laptop,
            new PresenceClientRequest(activity),
            this.Presence(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Active")]
    [InlineData("away")]
    public async Task RenewAsync_AnActivityItDoesNotKnow_IsRefusedWithoutWriting(string? activity)
    {
        // Act
        var result = await ClientPresenceEndpoints.RenewAsync(
            Laptop,
            new PresenceClientRequest(activity),
            this.Presence(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        await this.store.DidNotReceiveWithAnyArgs().RenewClientAsync(default, default, default, default, default, default);
    }

    [Fact]
    public async Task RenewAsync_TheEmptyClientIdentifier_IsRefused()
    {
        // Act
        var result = await ClientPresenceEndpoints.RenewAsync(
            Guid.Empty,
            new PresenceClientRequest("active"),
            this.Presence(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task RenewAsync_PastTheMostOpenClients_AnswersConflict()
    {
        // Arrange
        this.store.RenewClientAsync(default, default, default, default, default, default).ReturnsForAnyArgs(false);

        // Act
        var result = await ClientPresenceEndpoints.RenewAsync(
            Laptop,
            new PresenceClientRequest("active"),
            this.Presence(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status409Conflict, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task RenewAsync_CallerInNoOrganization_AnswersNotFound()
    {
        // Act
        var result = await ClientPresenceEndpoints.RenewAsync(
            Laptop,
            new PresenceClientRequest("active"),
            this.Presence(inOrganization: false),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    [Fact]
    public async Task CloseAsync_InAnOrganization_AnswersNoContent()
    {
        // Act
        var result = await ClientPresenceEndpoints.CloseAsync(Laptop, this.Presence(), TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NoContent>(result.Result);
    }

    [Fact]
    public async Task ChooseAsync_AStateForADuration_AnswersThePresenceItLeft()
    {
        // Arrange
        this.store.ReadAsync(Arg.Any<IReadOnlyCollection<UserId>>(), Arg.Any<CancellationToken>())
            .Returns(
                new Dictionary<UserId, MemberPresence>(),
                new Dictionary<UserId, MemberPresence>
                {
                    [SyntheticUser.Deployment] = new MemberPresence(
                        [new OpenPresenceClient(ClientActivity.Active, Now.AddMinutes(1))],
                        new PresenceChoice(PresenceState.Busy, PresenceDuration.OneHour, Now.AddHours(1)),
                        LastSeen: null),
                });

        // Act
        var result = await ClientPresenceEndpoints.ChooseAsync(
            new PresenceChoiceRequest("Busy", "1h"),
            this.Presence(),
            TestContext.Current.CancellationToken);

        // Assert
        var answered = Assert.IsType<Ok<OwnPresenceResponse>>(result.Result);
        Assert.Equal(new OwnPresenceResponse("Busy", "Busy", "1h", Now.AddHours(1)), answered.Value);
        await this.store.Received(1).ChooseAsync(
            SyntheticUser.Deployment,
            new PresenceChoice(PresenceState.Busy, PresenceDuration.OneHour, Now.AddHours(1)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("busy", null)]
    [InlineData("3", null)]
    [InlineData("Busy, Away", null)]
    [InlineData("Busy", "3h")]
    [InlineData("Busy", "")]
    public async Task ChooseAsync_AStateOrDurationItDoesNotKnow_IsRefusedWithoutWriting(string state, string? duration)
    {
        // Act
        var result = await ClientPresenceEndpoints.ChooseAsync(
            new PresenceChoiceRequest(state, duration),
            this.Presence(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, Assert.IsType<ProblemHttpResult>(result.Result).StatusCode);
        await this.store.DidNotReceiveWithAnyArgs().ChooseAsync(default, default, default);
    }

    [Fact]
    public async Task ReadAsync_CallerInNoOrganization_AnswersNotFound()
    {
        // Act
        var result = await ClientPresenceEndpoints.ReadAsync(this.Presence(inOrganization: false), TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    public ValueTask DisposeAsync() => this.signals.DisposeAsync();

    private OwnPresence Presence(bool inOrganization = true)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite);
        var ownership = ContactBookOwnerships.For(
            authorization,
            new StubMailAccountAssignments(),
            new StatedAdministrativeTargets().WithUser(SyntheticUser.Deployment, inOrganization ? Organization : null));
        var zones = Substitute.For<IUserTimeZones>();
        zones.ZoneOf(Arg.Any<UserId>()).Returns(UserTimeZone.Coordinated);

        return new OwnPresence(this.store, this.signals, ownership, authorization, zones, new PresenceSweepSchedule(), this.clock);
    }
}
