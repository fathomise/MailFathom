// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Delivery.Governance;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Emails.Authentication;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.AutomaticReplies;

/// <summary>
/// Covers what an automatic reply answers: once per sender, within its period, with the text for the sender's side of
/// the organization, never a program, a list, or a bounce, and never twice when two replicas reach one sender.
/// </summary>
public sealed class AutomaticReplyResponderTests
{
    /// <summary>
    /// Within the period, an arrival is answered through the account's own outbox, addressed to the sender, marked as an
    /// automatic reply, and placed in the conversation it answers.
    /// </summary>
    [Fact]
    public async Task AnswerAsync_AnArrivalWithinThePeriod_WritesOneAutomaticReplyToTheSender()
    {
        // Arrange
        var world = new AutomaticReplyWorld();
        await world.AnswerAsync();
        var arrival = world.Arrive("anna@example.test");

        // Act
        var answered = await world.RespondAsync(arrival);

        // Assert
        Assert.Equal(1, answered);
        var request = Assert.Single(world.Outgoing.OpenRequests);
        Assert.Equal(OutgoingEmailOrigin.AutomaticReply, request.Requester.Origin);
        Assert.Equal(AutomaticReplyWorld.Account, request.Account);
        Assert.Equal("anna@example.test", Assert.Single(request.Recipients).Address.Address);
        var reply = Assert.Single(world.Composed);
        Assert.True(reply.IsAutomaticReply);
        Assert.Equal("Auto: Budget", reply.Subject);
        Assert.Equal("I am away until Monday.", reply.PlainTextBody);
        Assert.True(reply.Threading.IsThreaded);
    }

    /// <summary>A second message from the same sender within one period is not answered again.</summary>
    [Fact]
    public async Task AnswerAsync_ASecondMessageFromTheSameSender_IsNotAnsweredAgain()
    {
        // Arrange
        var world = new AutomaticReplyWorld();
        await world.AnswerAsync();
        var first = world.Arrive("anna@example.test");
        var second = world.Arrive("ANNA@example.test");
        var other = world.Arrive("piotr@example.test");

        // Act
        var answered = await world.RespondAsync(first, second, other);

        // Assert
        Assert.Equal(2, answered);
        Assert.Equal(2, world.Outgoing.OpenRequests.Count);
    }

    /// <summary>
    /// The losing replica: a sender another replica has already claimed under the same revision is answered by nobody
    /// here, so two replicas reaching two messages from one sender send one reply between them.
    /// </summary>
    [Fact]
    public async Task AnswerAsync_ASenderAnotherReplicaAlreadyClaimed_WritesNothing()
    {
        // Arrange
        var world = new AutomaticReplyWorld();
        await world.AnswerAsync();
        var held = await world.Store.ReadAsync(SyntheticUser.Deployment, TestContext.Current.CancellationToken);
        Assert.True(await world.Store.TryClaimAsync(
            SyntheticUser.Deployment,
            held!.Revision,
            AutomaticReplyWorld.Address("anna@example.test"),
            TestContext.Current.CancellationToken));
        var arrival = world.Arrive("anna@example.test");

        // Act
        var answered = await world.RespondAsync(arrival);

        // Assert
        Assert.Equal(0, answered);
        Assert.Empty(world.Outgoing.OpenRequests);
    }

    /// <summary>Saving the reply again starts it over, so a sender answered under the previous save is answered again.</summary>
    [Fact]
    public async Task AnswerAsync_TheReplySavedAgain_AnswersASenderTheOldOneAnswered()
    {
        // Arrange
        var world = new AutomaticReplyWorld();
        await world.AnswerAsync();
        await world.RespondAsync(world.Arrive("anna@example.test"));
        await world.AnswerAsync();

        // Act
        var answered = await world.RespondAsync(world.Arrive("anna@example.test"));

        // Assert
        Assert.Equal(1, answered);
        Assert.Equal(2, world.Outgoing.OpenRequests.Count);
    }

    /// <summary>Before the period starts and once it has ended, nothing is answered.</summary>
    [Theory]
    [InlineData(-2)]
    [InlineData(7)]
    public async Task AnswerAsync_OutsideThePeriod_WritesNothing(int daysFromNow)
    {
        // Arrange
        var world = new AutomaticReplyWorld(new FakeTimeProvider(AutomaticReplyWorld.Now.AddDays(daysFromNow)));
        await world.AnswerAsync();

        // Act
        var answered = await world.RespondAsync(world.Arrive("anna@example.test"));

        // Assert
        Assert.Equal(0, answered);
        Assert.Empty(world.Outgoing.OpenRequests);
    }

    /// <summary>A reply switched off before its period ends answers nothing.</summary>
    [Fact]
    public async Task AnswerAsync_TheReplySwitchedOff_WritesNothing()
    {
        // Arrange
        var world = new AutomaticReplyWorld();
        await world.AnswerAsync(isOn: false);

        // Act
        var answered = await world.RespondAsync(world.Arrive("anna@example.test"));

        // Assert
        Assert.Equal(0, answered);
    }

    /// <summary>
    /// Nothing RFC 3834 excludes is answered: a program's or a list's message, a bulk one, a bounce from the null sender,
    /// a no-reply sender, and the account writing to itself.
    /// </summary>
    [Theory]
    [InlineData("anna@example.test", EmailAutomation.AutomaticallySubmitted, false)]
    [InlineData("anna@example.test", EmailAutomation.MailingList, false)]
    [InlineData("anna@example.test", EmailAutomation.BulkPrecedence, false)]
    [InlineData("anna@example.test", EmailAutomation.None, true)]
    [InlineData("no-reply@example.test", EmailAutomation.None, false)]
    [InlineData(AutomaticReplyWorld.OwnAddress, EmailAutomation.None, false)]
    public async Task AnswerAsync_AnArrivalRfc3834Excludes_WritesNothing(
        string from,
        EmailAutomation automation,
        bool hasNullReturnPath)
    {
        // Arrange
        var world = new AutomaticReplyWorld();
        await world.AnswerAsync();

        // Act
        var answered = await world.RespondAsync(world.Arrive(from, automation, hasNullReturnPath));

        // Assert
        Assert.Equal(0, answered);
        Assert.Empty(world.Outgoing.OpenRequests);
    }

    /// <summary>A sender the organization's signature proved a member is answered with the text for colleagues, and anybody else with the other.</summary>
    [Theory]
    [InlineData(SenderTrustSource.OrganizationMember, "Back on Monday; ask Ewa meanwhile.")]
    [InlineData(SenderTrustSource.OrganizationContact, "I am away until Monday.")]
    [InlineData(SenderTrustSource.None, "I am away until Monday.")]
    public async Task AnswerAsync_EachSideOfTheOrganization_IsAnsweredWithItsOwnText(SenderTrustSource trustedBy, string expected)
    {
        // Arrange
        var world = new AutomaticReplyWorld();
        await world.AnswerAsync(insideText: "Back on Monday; ask Ewa meanwhile.");

        // Act
        await world.RespondAsync(world.Arrive("anna@example.test", trustedBy: trustedBy));

        // Assert
        Assert.Equal(expected, Assert.Single(world.Composed).PlainTextBody);
    }

    /// <summary>An account shared after the reply was saved is not answered, because nobody is away on a shared mailbox's behalf.</summary>
    [Fact]
    public async Task AnswerAsync_AnAccountSharedSinceTheReplyWasSaved_WritesNothing()
    {
        // Arrange
        var world = new AutomaticReplyWorld();
        await world.AnswerAsync();
        world.Assignments.Assigning(SyntheticUser.Another, AutomaticReplyWorld.Account);

        // Act
        var answered = await world.RespondAsync(world.Arrive("anna@example.test"));

        // Assert
        Assert.Equal(0, answered);
    }

    /// <summary>A reply the deployment refuses to send is dropped rather than raised, so the settlement around it carries on.</summary>
    [Fact]
    public async Task AnswerAsync_SendingRefusedForTheAccount_WritesNothingAndDoesNotThrow()
    {
        // Arrange
        var world = new AutomaticReplyWorld(sendRefusal: OutgoingSendRefusalReason.AccountNotEnabled);
        await world.AnswerAsync();

        // Act
        var answered = await world.RespondAsync(world.Arrive("anna@example.test"));

        // Assert
        Assert.Equal(0, answered);
        Assert.Empty(world.Outgoing.OpenRequests);
    }
}
