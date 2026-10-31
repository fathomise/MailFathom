// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.AutomaticReplies;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.AutomaticReplies;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.AutomaticReplies;

/// <summary>Covers who may save an automatic reply, on which accounts, with which of the two texts, and who is told.</summary>
public sealed class OwnAutomaticReplyTests : IAsyncDisposable
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    private static readonly MailAccountId Team = MailAccountId.Create("team");

    private static readonly MailAccountId Foreign = MailAccountId.Create("foreign");

    private static readonly Guid Organization = new("0199a0c0-0000-7000-8000-00000000a001");

    private static readonly DateTimeOffset StartsAt = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly InMemoryAutomaticReplyStore store = new();

    private readonly InMemoryPresenceStore organizations = new InMemoryPresenceStore()
        .WithOrganization(Organization, SyntheticUser.Deployment, SyntheticUser.Another);

    private readonly FakeTimeProvider clock = new(StartsAt.AddDays(1));

    private readonly RecordingClientSignalChannel channel = new();

    private readonly ClientSignals signals;

    public OwnAutomaticReplyTests() => this.signals = new ClientSignals([this.channel], this.clock);

    private readonly StubMailAccountAssignments assignments = new StubMailAccountAssignments()
        .Assigning(SyntheticUser.Deployment, Work, Team)
        .Assigning(SyntheticUser.Another, Team, Foreign);

    /// <summary>A reply on the user's own unshared account is saved, and read back as it was written.</summary>
    [Fact]
    public async Task SaveAsync_TheUsersOwnAccount_SavesTheReplyAndReadsItBack()
    {
        // Arrange
        var replies = this.Replies(organization: Organization);

        // Act
        var write = await replies.SaveAsync(Reply([Work], insideText: "Ask Ewa."), TestContext.Current.CancellationToken);
        var read = await replies.ReadAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AutomaticReplyWriteOutcome.Saved, write.Outcome);
        Assert.Equal(write.Saved, read);
        Assert.Equal("Ask Ewa.", read!.Reply.InsideText);
    }

    /// <summary>An account assigned to somebody else is refused, naming it.</summary>
    [Fact]
    public async Task SaveAsync_AnAccountThatIsNotTheUsers_IsRefusedNamingIt()
    {
        // Arrange
        var replies = this.Replies();

        // Act
        var write = await replies.SaveAsync(Reply([Work, Foreign]), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new AutomaticReplyWrite(AutomaticReplyWriteOutcome.AccountNotAssigned, Account: Foreign), write);
        Assert.Null(await replies.ReadAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>A shared mailbox is refused, naming it, because nobody is away on its behalf.</summary>
    [Fact]
    public async Task SaveAsync_ASharedMailbox_IsRefusedNamingIt()
    {
        // Arrange
        var replies = this.Replies();

        // Act
        var write = await replies.SaveAsync(Reply([Work, Team]), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new AutomaticReplyWrite(AutomaticReplyWriteOutcome.AccountShared, Account: Team), write);
    }

    /// <summary>A user in no organization has no colleagues, so a text for them is refused while the other text alone is saved.</summary>
    [Fact]
    public async Task SaveAsync_ATextForColleaguesFromAUserInNoOrganization_IsRefused()
    {
        // Arrange
        var replies = this.Replies(organization: null);

        // Act
        var withColleagues = await replies.SaveAsync(Reply([Work], insideText: "Ask Ewa."), TestContext.Current.CancellationToken);
        var outsideAlone = await replies.SaveAsync(Reply([Work]), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AutomaticReplyWriteOutcome.NoOrganization, withColleagues.Outcome);
        Assert.Equal(AutomaticReplyWriteOutcome.Saved, outsideAlone.Outcome);
    }

    /// <summary>Saving asks for the sending grant beside the profile one, because the reply sends mail as the user's accounts.</summary>
    [Fact]
    public async Task SaveAsync_ACallerThatMayNotSend_IsRefused()
    {
        // Arrange
        var replies = new OwnAutomaticReply(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.ProfileRead, MailFathomPermission.ProfileWrite),
            this.store,
            this.assignments,
            new StatedAdministrativeTargets(),
            this.organizations,
            this.signals,
            this.clock);

        // Act, Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => replies.SaveAsync(Reply([Work]), TestContext.Current.CancellationToken));
    }

    /// <summary>A reply that makes the user out of office now tells every member of their organization to read them again.</summary>
    [Fact]
    public async Task SaveAsync_AReplyRunningNow_TellsTheOrganizationTheUsersPresenceMoved()
    {
        // Act
        await this.Replies(organization: Organization).SaveAsync(Reply([Work]), TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Equal(
            [SyntheticUser.Deployment, SyntheticUser.Another],
            this.channel.Published.Select(signal => signal.User!.Value).OrderBy(user => user.Value));
        Assert.All(this.channel.Published, signal =>
        {
            Assert.Equal(ClientSignalKind.PresenceChanged, signal.Kind);
            Assert.Equal(SyntheticUser.Deployment, Assert.Single(signal.Members));
        });
    }

    /// <summary>Switching a running reply off, or moving when it ends, changes what colleagues see and tells the organization.</summary>
    /// <param name="isOn">Whether the reply saved over the running one is switched on.</param>
    /// <param name="daysUntilItEnds">How many days after its start the reply saved over the running one ends.</param>
    [Theory]
    [InlineData(false, 7)]
    [InlineData(true, 3)]
    public async Task SaveAsync_ARunningReplySwitchedOffOrItsEndMoved_TellsTheOrganization(bool isOn, int daysUntilItEnds)
    {
        // Arrange
        await this.store.SaveAsync(SyntheticUser.Deployment, Reply([Work]), TestContext.Current.CancellationToken);

        // Act
        await this.Replies(organization: Organization).SaveAsync(
            Reply([Work], endsAt: StartsAt.AddDays(daysUntilItEnds), isOn: isOn),
            TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Equal(
            [SyntheticUser.Deployment, SyntheticUser.Another],
            this.channel.Published.Select(signal => signal.User!.Value).OrderBy(user => user.Value));
    }

    /// <summary>A running reply saved again with the same end, only its text changed, changes nothing a colleague sees and tells nobody.</summary>
    [Fact]
    public async Task SaveAsync_ARunningReplySavedAgainWithTheSameEnd_TellsNobody()
    {
        // Arrange
        await this.store.SaveAsync(SyntheticUser.Deployment, Reply([Work]), TestContext.Current.CancellationToken);

        // Act
        await this.Replies(organization: Organization).SaveAsync(
            Reply([Work], insideText: "Ask Ewa."),
            TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Empty(this.channel.Published);
    }

    /// <summary>A reply that has not begun yet changes nothing a colleague sees now and tells nobody.</summary>
    [Fact]
    public async Task SaveAsync_AReplyThatHasNotBegun_TellsNobody()
    {
        // Act
        await this.Replies(organization: Organization).SaveAsync(
            Reply([Work], startsAt: StartsAt.AddDays(7)),
            TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Empty(this.channel.Published);
    }

    /// <summary>A user in no organization has no colleagues, so a running reply is saved and nobody is told.</summary>
    [Fact]
    public async Task SaveAsync_AReplyRunningNowFromAUserInNoOrganization_IsSavedAndTellsNobody()
    {
        // Act
        var write = await this.Replies(organization: null).SaveAsync(Reply([Work]), TestContext.Current.CancellationToken);
        this.clock.Advance(ClientSignals.FoldingWindow);
        await this.signals.DrainAsync();

        // Assert
        Assert.Equal(AutomaticReplyWriteOutcome.Saved, write.Outcome);
        Assert.Empty(this.channel.Published);
    }

    /// <summary>A user this deployment no longer holds is told so rather than refused for something else.</summary>
    [Fact]
    public async Task SaveAsync_AUserTheDeploymentDoesNotHold_IsAnsweredUnknown()
    {
        // Arrange
        this.store.HoldsUsers = false;

        // Act
        var write = await this.Replies().SaveAsync(Reply([Work]), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(AutomaticReplyWriteOutcome.UserUnknown, write.Outcome);
    }

    private static AutomaticReply Reply(
        MailAccountId[] accounts,
        string? insideText = null,
        DateTimeOffset? startsAt = null,
        DateTimeOffset? endsAt = null,
        bool isOn = true)
    {
        var start = startsAt ?? StartsAt;

        Assert.True(AutomaticReply.TryCreate(
            insideText,
            "I am away.",
            accounts,
            start,
            endsAt ?? start.AddDays(7),
            isOn,
            out var reply,
            out _));

        return reply!;
    }

    private OwnAutomaticReply Replies(Guid? organization = null) => new(
        AccessAuthorizations.ForCallerGranted(
            MailFathomPermission.ProfileRead,
            MailFathomPermission.ProfileWrite,
            MailFathomPermission.MailSend),
        this.store,
        this.assignments,
        new StatedAdministrativeTargets().WithUser(SyntheticUser.Deployment, organization),
        this.organizations,
        this.signals,
        this.clock);

    public ValueTask DisposeAsync() => this.signals.DisposeAsync();
}
