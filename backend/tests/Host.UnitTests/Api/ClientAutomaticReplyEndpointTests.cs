// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.AutomaticReplies;
using MailFathom.Application.Signals;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

public sealed class ClientAutomaticReplyEndpointTests : IAsyncDisposable
{
    private static readonly MailAccountId Primary = MailAccountId.Create("primary");

    private static readonly DateTimeOffset StartsAt = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly ClientSignals signals = new([], new FakeTimeProvider());

    /// <summary>The path a client appends to the address it was configured with.</summary>
    [Fact]
    public void AutomaticReplyRoute_IsThePathAClientComposes() =>
        Assert.Equal("/automatic-reply", ClientAutomaticReplyEndpoint.AutomaticReplyRoute);

    /// <summary>A body that leaves the period out, or names an account by nothing, is refused before anything is read.</summary>
    [Theory]
    [InlineData(false, "primary")]
    [InlineData(true, " ")]
    public void Stated_ABodyMissingAPeriodOrAnAccount_SaysWhy(bool statesPeriod, string account)
    {
        // Arrange
        var request = new ClientAutomaticReplyRequest(
            OutsideText: "Away.",
            Accounts: [account],
            StartsAt: statesPeriod ? StartsAt : null,
            EndsAt: StartsAt.AddDays(7));

        // Act
        var reply = request.Stated(out var refusal);

        // Assert
        Assert.Null(reply);
        Assert.False(string.IsNullOrWhiteSpace(refusal));
    }

    /// <summary>A reply is switched on unless the body says otherwise, so a client sets one by stating the period and the texts.</summary>
    [Fact]
    public void Stated_ABodyNotSayingWhetherItIsOn_IsOn()
    {
        // Act
        var reply = Request().Stated(out _);

        // Assert
        Assert.True(reply?.IsOn);
    }

    /// <summary>A shared mailbox is refused by name, so the person sees which of their accounts cannot answer.</summary>
    [Fact]
    public async Task SaveAsync_AnAccountSharedWithAColleague_IsRefusedNamingIt()
    {
        // Arrange
        var assignments = Substitute.For<IMailAccountAssignments>();
        assignments.ReadAccountsAssignedToAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns([Primary]);
        assignments.ReadUsersAssignedToAsync(Primary, Arg.Any<CancellationToken>())
            .Returns([SyntheticUser.Deployment, SyntheticUser.Another]);

        // Act
        var result = await ClientAutomaticReplyEndpoint.SaveAsync(
            this.Replies(assignments),
            Request(),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);

        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Contains("'primary' is a shared mailbox", refusal.ProblemDetails.Detail!, StringComparison.Ordinal);
    }

    /// <summary>What was saved is what the client is handed back.</summary>
    [Fact]
    public async Task SaveAsync_AReplyOnTheirOwnAccount_AnswersWithTheReplyAsSaved()
    {
        // Arrange
        var assignments = Substitute.For<IMailAccountAssignments>();
        assignments.ReadAccountsAssignedToAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns([Primary]);
        assignments.ReadUsersAssignedToAsync(Primary, Arg.Any<CancellationToken>()).Returns([SyntheticUser.Deployment]);

        // Act
        var result = await ClientAutomaticReplyEndpoint.SaveAsync(
            this.Replies(assignments),
            Request(),
            TestContext.Current.CancellationToken);

        // Assert
        var saved = Assert.IsType<Ok<ClientAutomaticReplyResponse>>(result.Result).Value!;

        Assert.Equal("Away.", saved.OutsideText);
        Assert.Equal(["primary"], saved.Accounts);
        Assert.True(saved.On);
    }

    /// <summary>A caller this deployment holds no record for is answered as absent rather than as a malformed body.</summary>
    [Fact]
    public async Task SaveAsync_AUserTheStoreDoesNotHold_AnswersNotFound()
    {
        // Arrange
        var assignments = Substitute.For<IMailAccountAssignments>();
        assignments.ReadAccountsAssignedToAsync(SyntheticUser.Deployment, Arg.Any<CancellationToken>()).Returns([Primary]);
        assignments.ReadUsersAssignedToAsync(Primary, Arg.Any<CancellationToken>()).Returns([SyntheticUser.Deployment]);
        var store = Substitute.For<IAutomaticReplyStore>();
        store.SaveAsync(Arg.Any<UserId>(), Arg.Any<AutomaticReply>(), Arg.Any<CancellationToken>())
            .Returns((StoredAutomaticReply?)null);

        // Act
        var result = await ClientAutomaticReplyEndpoint.SaveAsync(
            this.Replies(assignments, store),
            Request(),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound<ProblemDetails>>(result.Result);
    }

    private static ClientAutomaticReplyRequest Request() =>
        new(OutsideText: "Away.", Accounts: ["primary"], StartsAt: StartsAt, EndsAt: StartsAt.AddDays(7));

    public ValueTask DisposeAsync() => this.signals.DisposeAsync();

    private OwnAutomaticReply Replies(IMailAccountAssignments assignments, IAutomaticReplyStore? store = null)
    {
        if (store is null)
        {
            store = Substitute.For<IAutomaticReplyStore>();
            store.SaveAsync(Arg.Any<UserId>(), Arg.Any<AutomaticReply>(), Arg.Any<CancellationToken>())
                .Returns(call => new StoredAutomaticReply(call.ArgAt<AutomaticReply>(1), Guid.NewGuid()));
        }

        return new OwnAutomaticReply(
            AccessAuthorizations.ForUserGranted(
                SyntheticUser.Deployment,
                MailFathomPermission.ProfileWrite,
                MailFathomPermission.MailSend),
            store,
            assignments,
            Substitute.For<IAdministrativeTargets>(),
            this.signals,
            new FakeTimeProvider());
    }
}
