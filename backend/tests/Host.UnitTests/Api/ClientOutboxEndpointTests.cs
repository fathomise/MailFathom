// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Rendering;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers what the user's outbox routes decide about a request, and what they refuse to answer at all.</summary>
/// <remarks>
/// <para>
/// The narrowing is the claim worth asserting here. A page of an outbox names the account it reads and a request that
/// names none is refused rather than defaulted, because an unnarrowed reading on a user-facing surface would page
/// through every account this deployment serves — which is the deployment-wide catalog the administrative surface is
/// for. Everything about whose sends are answered is <c>UserOutbox</c>'s and is covered there.
/// </para>
/// <para>
/// A decision reports what became of the send it named rather than refusing, because the caller asked a question this
/// deployment can answer; only a request naming no send at all is a refusal.
/// </para>
/// </remarks>
public sealed class ClientOutboxEndpointTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    /// <summary>A second mailbox this deployment serves and the caller is not assigned.</summary>
    private static readonly MailAccountId Unassigned = MailAccountId.Create("someone-elses");

    /// <summary>The paths a client appends to the address it was configured with, pinned because it composes them from constants of its own.</summary>
    [Fact]
    public void OutboxRoutes_ArePathsAClientComposes()
    {
        // Arrange
        // Act
        // Assert
        Assert.Equal("/outbox", ClientOutboxEndpoints.OutboxRoute);
        Assert.Equal("/outbox/{outgoingEmailId:guid}", ClientOutboxEndpoints.OutboxSendRoute);
        Assert.Equal("/outbox/cancellation", ClientOutboxEndpoints.OutboxCancellationRoute);
        Assert.Equal("/outbox/requeue", ClientOutboxEndpoints.OutboxRequeueRoute);
        Assert.Equal("/outbox/dismissal", ClientOutboxEndpoints.OutboxDismissalRoute);
        Assert.Equal("/outbox/reopening", ClientOutboxEndpoints.OutboxReopeningRoute);
    }

    /// <summary>The regression this exists for: a page that narrowed to nothing would answer every user's outgoing mail.</summary>
    [Fact]
    public async Task ReadPageAsync_ARequestNamingNoAccount_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            account: null,
            stage: null,
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A stage no send is ever in is refused as a request, naming the stages a client may narrow by.</summary>
    [Fact]
    public async Task ReadPageAsync_AStageThisSystemDoesNotPublish_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            Work.Value,
            "posted",
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A delivered send has left the outbox, so narrowing a page to it is refused as a stage the outbox does not hold.</summary>
    [Fact]
    public async Task ReadPageAsync_AStageTheOutboxDoesNotHold_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            Work.Value,
            nameof(OutgoingEmailStage.Sent),
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A page carries who a send is addressed to and its subject, so a send-only credential is refused it.</summary>
    [Fact]
    public async Task ReadPageAsync_ACallerHoldingOnlyTheSendingGrant_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var refusal = () => ClientOutboxEndpoints.ReadPageAsync(
            Work.Value,
            stage: null,
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations, MailFathomPermission.MailSend),
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A cursor this deployment did not issue is refused rather than read as an offset somebody chose.</summary>
    [Fact]
    public async Task ReadPageAsync_ACursorThisDeploymentDidNotIssue_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            Work.Value,
            stage: null,
            pageSize: null,
            "not-a-cursor",
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>An account another user owns is refused exactly as one this deployment does not serve.</summary>
    [Fact]
    public async Task ReadPageAsync_AnAccountThisUserDoesNotOwn_IsRefusedWithoutReading()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            "theirs",
            stage: null,
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>An identifier naming nothing answers as a send this user did not make, which is what one nobody made answers.</summary>
    [Fact]
    public async Task ReadSendAsync_AnIdentifierNamingNoSend_AnswersAsOneThisUserDidNotMake()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();

        // Act
        var result = await ClientOutboxEndpoints.ReadSendAsync(
            Guid.Empty,
            OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A send-only credential watches its own send go as its delivery, without what the message says.</summary>
    [Fact]
    public async Task ReadSendAsync_ACallerHoldingOnlyTheSendingGrant_AnswersWithTheDelivery()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var mine = await QueueAsync(outgoingEmails, Work);

        // Act
        var result = await ClientOutboxEndpoints.ReadSendAsync(
            mine.Value,
            OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>(), MailFathomPermission.MailSend),
            TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.IsType<Ok<OutboxSendResponse>>(result.Result);
        Assert.Equal(mine.Value, read.Value!.OutgoingEmail);
    }

    /// <summary>A caller that may read the outbox reads the send as the message it is.</summary>
    [Fact]
    public async Task ReadSendAsync_ACallerHoldingBothGrants_AnswersWithThePreview()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var mine = await QueueAsync(outgoingEmails, Work);

        // Act
        var result = await ClientOutboxEndpoints.ReadSendAsync(
            mine.Value,
            OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.IsType<Ok<ClientOutboxPreviewResponse>>(result.Result);
        Assert.Equal(mine.Value, read.Value!.OutgoingEmail);
        Assert.True(read.Value.InOutbox);
    }

    /// <summary>A send put back to wait states when it may next be tried, which is not when it was written down.</summary>
    [Fact]
    public async Task ReadSendAsync_ASendWaitingForALaterAttempt_StatesWhenItMayNextBeTried()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var mine = await QueueAsync(outgoingEmails, Work);
        var nextAttempt = DateTimeOffset.UnixEpoch.AddMinutes(37);
        await outgoingEmails.DeferAsync(
            Substitute.For<IPersistenceSession>(),
            outgoingEmails.Reassign(mine),
            mine,
            nextAttempt,
            failure: null,
            TestContext.Current.CancellationToken);

        // Act
        var result = await ClientOutboxEndpoints.ReadSendAsync(
            mine.Value,
            OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.IsType<Ok<ClientOutboxPreviewResponse>>(result.Result);
        Assert.Equal(nextAttempt, read.Value!.AvailableAt);
        Assert.NotEqual(read.Value.RecordedAt, read.Value.AvailableAt);
    }

    /// <summary>A row of the page states when its send may next be tried, as the outbox read answered it.</summary>
    [Fact]
    public async Task ReadPageAsync_ASendWaitingForALaterAttempt_StatesWhenItMayNextBeTried()
    {
        // Arrange
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));
        var recordedAt = DateTimeOffset.UnixEpoch;
        var nextAttempt = recordedAt.AddMinutes(37);
        var operations = Substitute.For<IOutboxOperationStore>();
        operations
            .ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>())
            .Returns(new AccountOutboxPage(
                [
                    new AccountOutboxEntry
                    {
                        OutgoingEmailId = OutgoingEmailId.Create(Guid.CreateVersion7()),
                        AccountId = Work,
                        Stage = OutgoingEmailStage.Recorded,
                        IsHeldByAttempt = false,
                        NamedRecipients = [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)],
                        OtherRecipientCount = 0,
                        Subject = "A send",
                        RecordedAt = recordedAt,
                        DueAt = null,
                        AvailableAt = nextAttempt,
                        AttemptCount = 1,
                        LastFailure = null,
                        LastReplyCode = null,
                    },
                ],
                NextCursor: null));

        // Act
        var result = await ClientOutboxEndpoints.ReadPageAsync(
            Work.Value,
            stage: null,
            pageSize: null,
            cursor: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var read = Assert.IsType<Ok<ClientOutboxPageResponse>>(result.Result);
        var row = Assert.Single(read.Value!.Sends);
        Assert.Equal((recordedAt, nextAttempt), (row.RecordedAt, row.AvailableAt));
    }

    /// <summary>A send from a mailbox this caller is not assigned answers as one nobody made.</summary>
    [Fact]
    public async Task ReadSendAsync_ASendFromAMailboxTheCallerIsNotAssigned_AnswersAsOneNobodyMade()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var theirs = await QueueAsync(outgoingEmails, Unassigned);

        // Act
        var result = await ClientOutboxEndpoints.ReadSendAsync(
            theirs.Value,
            OutboxOver(outgoingEmails, Substitute.For<IOutboxOperationStore>()),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.IsType<NotFound>(result.Result);
    }

    /// <summary>A withdrawal naming no send is refused, because the decision it asks for names nothing to take back.</summary>
    [Fact]
    public async Task CancelAsync_ARequestNamingNoSend_IsRefused()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.CancelAsync(
            request: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A send from a mailbox this caller is not assigned is an outcome rather than a refusal, and the decision is never reached.</summary>
    [Fact]
    public async Task CancelAsync_ASendFromAMailboxTheCallerIsNotAssigned_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var operations = Substitute.For<IOutboxOperationStore>();
        var theirs = await QueueAsync(outgoingEmails, Unassigned);

        // Act
        var result = await ClientOutboxEndpoints.CancelAsync(
            new OutboxCancellationRequest(theirs.Value),
            OutboxOver(outgoingEmails, operations),
            TestContext.Current.CancellationToken);

        // Assert
        var decided = Assert.IsType<Ok<OutboxDecisionResponse>>(result.Result);
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown.ToString(), decided.Value!.Outcome);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>Offering a send again names one send, and a request naming none is refused rather than offering a set.</summary>
    [Fact]
    public async Task RequeueAsync_ARequestNamingNoSend_IsRefused()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.RequeueAsync(
            request: null,
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A dismissal naming no send is refused, because the decision it asks for names nothing to accept.</summary>
    [Fact]
    public async Task DismissAsync_ARequestNamingNoSend_IsRefused()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.DismissAsync(
            new OutboxDismissalRequest(OutgoingEmail: null),
            OutboxOver(new InMemoryOutgoingEmailStore(), operations),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>A dismissal reports what became of the send it named, so a send this user does not hold is an outcome rather than a refusal.</summary>
    [Fact]
    public async Task DismissAsync_ASendFromAMailboxTheCallerIsNotAssigned_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var operations = Substitute.For<IOutboxOperationStore>();
        var theirs = await QueueAsync(outgoingEmails, Unassigned);

        // Act
        var result = await ClientOutboxEndpoints.DismissAsync(
            new OutboxDismissalRequest(theirs.Value),
            OutboxOver(outgoingEmails, operations),
            TestContext.Current.CancellationToken);

        // Assert
        var decided = Assert.IsType<Ok<OutboxDecisionResponse>>(result.Result);
        Assert.Equal(nameof(OutboxDecisionOutcome.RecordUnknown), decided.Value!.Outcome);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>Taking a send back to edit names one send, and a request naming none is refused.</summary>
    [Fact]
    public async Task ReopenAsync_ARequestNamingNoSend_IsRefused()
    {
        // Arrange
        var operations = Substitute.For<IOutboxOperationStore>();

        // Act
        var result = await ClientOutboxEndpoints.ReopenAsync(
            new OutboxReopeningRequest(OutgoingEmail: null),
            OutboxOver(new InMemoryOutgoingEmailStore(), operations, Drafting),
            TestContext.Current.CancellationToken);

        // Assert
        var refusal = Assert.IsType<ProblemHttpResult>(result.Result);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Empty(operations.ReceivedCalls());
    }

    /// <summary>An accepted reopening names the draft the send went back into, which is what the client opens.</summary>
    [Fact]
    public async Task ReopenAsync_ARefusedSendOfTheCallers_AnswersWithTheDraftItWentBackInto()
    {
        // Arrange
        var outgoingEmails = new InMemoryOutgoingEmailStore();
        var operations = Substitute.For<IOutboxOperationStore>();
        var mine = await QueueAsync(outgoingEmails, Work);
        var draft = MailDraftId.Create(Guid.CreateVersion7());
        operations
            .ReopenAsDraftAsync(mine, Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(new OutboxReopening(OutboxDecisionOutcome.Accepted, draft));

        // Act
        var result = await ClientOutboxEndpoints.ReopenAsync(
            new OutboxReopeningRequest(mine.Value),
            OutboxOver(outgoingEmails, operations, Drafting),
            TestContext.Current.CancellationToken);

        // Assert
        var reopened = Assert.IsType<Ok<OutboxReopeningResponse>>(result.Result);
        Assert.Equal(new OutboxReopeningResponse(mine.Value, nameof(OutboxDecisionOutcome.Accepted), draft.Value), reopened.Value);
    }

    private static MailFathomPermission[] Drafting =>
        [MailFathomPermission.MailRead, MailFathomPermission.MailSend, MailFathomPermission.MailDraftsWrite];

    /// <summary>Builds the user-facing outbox the routes reach, for a caller acting for the deployment's user.</summary>
    /// <remarks>
    /// Two mailboxes are served and the caller is assigned one of them, which is what lets a test state a send
    /// somebody else made: the record is in this deployment either way, and what keeps it out of the answer is the
    /// assignment rather than the deployment not holding it.
    /// </remarks>
    private static UserOutbox OutboxOver(
        InMemoryOutgoingEmailStore outgoingEmails,
        IOutboxOperationStore operations,
        params MailFathomPermission[] granted)
    {
        var authorization = AccessAuthorizations.ForCallerGranted(
            granted is [] ? [MailFathomPermission.MailRead, MailFathomPermission.MailSend] : granted);
        var assignments = new StubMailAccountAssignments()
            .Assigning(SyntheticUser.Deployment, Work)
            .Assigning(SyntheticUser.Another, Unassigned);

        return new UserOutbox(
            AssignedMailAccountCatalogs.For(
                authorization,
                assignments,
                SyntheticServedAccount.Of(Work),
                SyntheticServedAccount.Of(Unassigned)),
            outgoingEmails,
            operations,
            Substitute.For<IEmailContentStore>(),
            Substitute.For<IEmailContentRenderer>(),
            Substitute.For<IOutgoingMailTextReader>(),
            SensitiveContentEgressGuards.Inactive(),
            new EmailContentReadOptions(),
            Substitute.For<IUserLanguages>(),
            authorization,
            ClientSignalPublishers.ReachingNobody,
            TimeProvider.System);
    }

    /// <summary>Writes one queued send down for one account, which is the arrangement the reading tests start from.</summary>
    private static async Task<OutgoingEmailId> QueueAsync(
        InMemoryOutgoingEmailStore outgoingEmails,
        MailAccountId account)
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

        var opened = await outgoingEmails.OpenAsync(
            Substitute.For<IPersistenceSession>(),
            OutgoingEmailRequest.Create(
                account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command($"mfctl-{account.Value}"),
                [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]),
            OutgoingEmailPrincipal.Of("test-caller"),
            Encoding.ASCII.GetBytes("Subject: a send\r\n\r\nHello."),
            TestContext.Current.CancellationToken);

        return opened.Record.Id;
    }
}
