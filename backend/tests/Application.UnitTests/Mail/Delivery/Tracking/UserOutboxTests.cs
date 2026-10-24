// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.Access;
using MailFathom.Application.Accounts;
using MailFathom.Application.EmailContent;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Localization;
using MailFathom.Application.Mail.Delivery.Operations;
using MailFathom.Application.Mail.Delivery.Screening;
using MailFathom.Application.Mail.Delivery.Tracking;
using MailFathom.Application.Persistence;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Tracking;

/// <summary>Covers the outbox one user reads and decides about, and whose sends each act may reach.</summary>
/// <remarks>
/// Every act here narrows by the mailboxes the caller is assigned rather than by who queued the send, which is what
/// makes a shared mailbox's outbox one outbox: a colleague assigned the same mailbox reads and withdraws the sends
/// made from it, and a caller assigned no mailbox reaches none of them.
/// </remarks>
public sealed class UserOutboxTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    /// <summary>A mailbox this deployment serves that the caller under test is not assigned.</summary>
    private static readonly MailAccountId Unassigned = MailAccountId.Create("theirs");

    private static readonly ReadOnlyMemory<byte> RawMime =
        Encoding.ASCII.GetBytes("Subject: Quarterly figures\r\n\r\nHello.");

    /// <summary>A page is read for the account the request named, narrowed to the mailboxes the caller is assigned.</summary>
    [Fact]
    public async Task ReadPageAsync_AMailboxThisUserIsAssigned_ReadsTheOutboxOfThatAccountAlone()
    {
        // Arrange
        await using var context = new OutboxContext();

        // Act
        var result = await context.Outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage: null,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxQueryOutcome.Accepted, result.Outcome);
        await context.Operations.Received(1).ReadAccountOutboxAsync(
            Arg.Is<OutboxQuery>(query => query!.AccountId == Work),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A mailbox the caller is not assigned is refused exactly as one this deployment does not serve.</summary>
    [Fact]
    public async Task ReadPageAsync_AMailboxThisUserIsNotAssigned_IsRefusedBeforeAnythingIsRead()
    {
        // Arrange
        await using var context = new OutboxContext();

        // Act
        var refusal = () => context.Outbox.ReadPageAsync(
            MailAccountSelector.For(Unassigned),
            stage: null,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<MailAccountNotAccessibleException>(refusal);
        Assert.Empty(context.Operations.ReceivedCalls());
    }

    /// <summary>A page size outside what a page holds is reported as the refusal rather than read.</summary>
    [Fact]
    public async Task ReadPageAsync_APageSizeOutsideWhatAPageHolds_ReportsTheRefusalWithoutReading()
    {
        // Arrange
        await using var context = new OutboxContext();

        // Act
        var result = await context.Outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage: null,
            OutboxQuery.MaximumPageSize + 1,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxQueryOutcome.PageSizeOutOfRange, result.Outcome);
        Assert.Null(result.Page);
        Assert.Empty(context.Operations.ReceivedCalls());
    }

    /// <summary>The outbox holds what has not left and what was refused, so each of those three is a stage it is read at.</summary>
    [Theory]
    [InlineData(OutgoingEmailStage.Recorded)]
    [InlineData(OutgoingEmailStage.TransmissionBegun)]
    [InlineData(OutgoingEmailStage.Refused)]
    public async Task ReadPageAsync_AStageTheOutboxHolds_ReadsTheOutboxNarrowedToIt(OutgoingEmailStage stage)
    {
        // Arrange
        await using var context = new OutboxContext();

        // Act
        var result = await context.Outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxQueryOutcome.Accepted, result.Outcome);
        await context.Operations.Received(1).ReadAccountOutboxAsync(
            Arg.Is<OutboxQuery>(query => query!.Stage == stage),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A delivered or withdrawn send has left the outbox, so asking for one is a mistake rather than an empty page.</summary>
    [Theory]
    [InlineData(OutgoingEmailStage.Sent)]
    [InlineData(OutgoingEmailStage.Cancelled)]
    public async Task ReadPageAsync_AStageTheOutboxDoesNotHold_IsRefusedAsUnknownWithoutReading(OutgoingEmailStage stage)
    {
        // Arrange
        await using var context = new OutboxContext();

        // Act
        var result = await context.Outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxQueryOutcome.StageUnknown, result.Outcome);
        Assert.Null(result.Page);
        await context.Operations.DidNotReceive().ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A page names who a send is addressed to and its subject, so it takes both the reading and the sending grant.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ReadPageAsync_CallerHoldingOnlyOneOfTheTwoGrants_IsRefused(bool grantsReading, bool grantsSending)
    {
        // Arrange
        await using var context = new OutboxContext(Granting(grantsReading, grantsSending));

        // Act
        var refusal = () => context.Outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage: null,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>A send written down before the subject was kept beside it is named by the subject its own message carries.</summary>
    [Fact]
    public async Task ReadPageAsync_ARowWithoutAStoredSubject_ReadsTheSubjectFromItsMessage()
    {
        // Arrange
        await using var context = new OutboxContext();
        var send = OutgoingEmailId.Create(Guid.CreateVersion7());
        context.Answering(Entry(send, subject: null));
        context.Storing(send);
        context.TextReader
            .ReadWordsAsync(Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(new OutgoingMailText("Quarterly figures", "Hello.", HtmlBody: null));

        // Act
        var result = await context.Outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage: null,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Quarterly figures", Assert.Single(result.Page!.Sends).Subject);
    }

    /// <summary>A failure is stated in the reader's own words beside the code a client keys on.</summary>
    [Fact]
    public async Task ReadPageAsync_ARefusedSend_StatesItsFailureInTheReadersLanguage()
    {
        // Arrange
        await using var context = new OutboxContext();
        var send = OutgoingEmailId.Create(Guid.CreateVersion7());
        context.Answering(Entry(send, subject: "Quarterly figures") with
        {
            Stage = OutgoingEmailStage.Refused,
            LastFailure = MailFathomErrorCode.OutgoingEmailRefused,
        });

        // Act
        var result = await context.Outbox.ReadPageAsync(
            MailAccountSelector.For(Work),
            stage: null,
            pageSize: null,
            cursor: null,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ApplicationTexts.GetText(ApplicationText.OutboxFailureRefused, UserLanguage.English),
            Assert.Single(result.Page!.Sends).FailureStatement);
    }

    /// <summary>Only the accounts this user is assigned are counted; one they are not is left out rather than refused.</summary>
    [Fact]
    public async Task CountAsync_AccountsOnlySomeOfWhichThisUserIsAssigned_CountsTheAssignedOnes()
    {
        // Arrange
        await using var context = new OutboxContext();

        // Act
        await context.Outbox.CountAsync([Work, Unassigned], TestContext.Current.CancellationToken);

        // Assert
        await context.Operations.Received(1).CountAccountOutboxesAsync(
            Arg.Is<IReadOnlyCollection<MailAccountId>>(accounts => accounts.SequenceEqual(new[] { Work })),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A credential that may not read the outbox is not told its size either, and a surface asks before counting.</summary>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    public async Task MayReadOutbox_TheGrantsACallerHolds_IsTrueOnlyForBoth(
        bool grantsReading,
        bool grantsSending,
        bool expected)
    {
        // Arrange
        await using var context = new OutboxContext(Granting(grantsReading, grantsSending));

        // Act
        var mayRead = context.Outbox.MayReadOutbox;

        // Assert
        Assert.Equal(expected, mayRead);
    }

    /// <summary>Counting is reading the outbox, so a caller holding one grant is refused rather than given a number.</summary>
    [Fact]
    public async Task CountAsync_CallerHoldingOnlyTheSendingGrant_IsRefused()
    {
        // Arrange
        await using var context = new OutboxContext(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend));

        // Act
        var refusal = () => context.Outbox.CountAsync([Work], TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>A send from a mailbox the caller is not assigned answers exactly as one nobody made.</summary>
    [Fact]
    public async Task FindAsync_ASendFromAMailboxThisUserIsNotAssigned_AnswersAsOneNobodyMade()
    {
        // Arrange
        await using var context = new OutboxContext(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend));
        var theirs = await context.QueueAsync(Unassigned);

        // Act
        var found = await context.Outbox.FindAsync(theirs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(found);
    }

    /// <summary>A send-only credential still watches its own send go, without being told what it says.</summary>
    [Fact]
    public async Task FindAsync_ASendFromAMailboxThisUserIsAssigned_AnswersWithTheRecordUnderTheSendingGrantAlone()
    {
        // Arrange
        await using var context = new OutboxContext(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend));
        var mine = await context.QueueAsync(Work);

        // Act
        var found = await context.Outbox.FindAsync(mine, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(found);
        Assert.Equal(Work, found!.AccountId);
    }

    /// <summary>A preview is the message the send will transmit: its subject, its body, and its attachments by name.</summary>
    [Fact]
    public async Task ReadPreviewAsync_ASendFromAMailboxThisUserIsAssigned_RendersTheMessageItWillTransmit()
    {
        // Arrange
        await using var context = new OutboxContext();
        var mine = await context.QueueAsync(Work, subject: "Quarterly figures");
        context.Storing(mine);

        // Act
        var preview = await context.Outbox.ReadPreviewAsync(mine, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(preview);
        Assert.Equal("Quarterly figures", preview!.Subject);
        Assert.Equal("Hello.", preview.Body?.PlainText.Text);
        Assert.Equal(2, preview.Attachments.Count);
    }

    /// <summary>A send whose stored message cannot be read is still previewed, with no body rather than a failure.</summary>
    [Fact]
    public async Task ReadPreviewAsync_ASendWhoseMessageIsMissing_AnswersWithTheRecordAndNoBody()
    {
        // Arrange
        await using var context = new OutboxContext();
        var mine = await context.QueueAsync(Work, subject: "Quarterly figures");

        // Act
        var preview = await context.Outbox.ReadPreviewAsync(mine, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(preview);
        Assert.Null(preview!.Body);
        Assert.Empty(preview.Attachments);
    }

    /// <summary>A send from a mailbox the caller is not assigned is not previewed, and nothing of it is rendered.</summary>
    [Fact]
    public async Task ReadPreviewAsync_ASendFromAMailboxThisUserIsNotAssigned_AnswersAsOneNobodyMade()
    {
        // Arrange
        await using var context = new OutboxContext();
        var theirs = await context.QueueAsync(Unassigned);
        context.Storing(theirs);

        // Act
        var preview = await context.Outbox.ReadPreviewAsync(theirs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(preview);
        Assert.Empty(context.Renderer.ObservedRemainingCharacters);
    }

    /// <summary>The message is mail, so a credential that may only send is refused its preview.</summary>
    [Fact]
    public async Task ReadPreviewAsync_CallerHoldingOnlyTheSendingGrant_IsRefused()
    {
        // Arrange
        await using var context = new OutboxContext(AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailSend));
        var mine = await context.QueueAsync(Work);

        // Act
        var refusal = () => context.Outbox.ReadPreviewAsync(mine, TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(refusal);
    }

    /// <summary>Withdrawing a send from an unassigned mailbox reports it unknown rather than reaching the decision.</summary>
    [Fact]
    public async Task CancelAsync_ASendFromAMailboxThisUserIsNotAssigned_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        await using var context = new OutboxContext();
        var theirs = await context.QueueAsync(Unassigned);

        // Act
        var outcome = await context.Outbox.CancelAsync(theirs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown, outcome);
        Assert.Empty(context.Operations.ReceivedCalls());
    }

    /// <summary>Offering an unassigned mailbox's send again reports it unknown rather than reaching the decision.</summary>
    [Fact]
    public async Task RequeueAsync_ASendFromAMailboxThisUserIsNotAssigned_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        await using var context = new OutboxContext();
        var theirs = await context.QueueAsync(Unassigned);

        // Act
        var outcome = await context.Outbox.RequeueAsync(
            theirs,
            refusalRestated: false,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown, outcome);
        Assert.Empty(context.Operations.ReceivedCalls());
    }

    /// <summary>A dismissed send left every outbox a person reads, so offering it again is the operator's rather than theirs.</summary>
    [Fact]
    public async Task RequeueAsync_ADismissedSend_IsRefusedAsAStageThatDoesNotAllowItWithoutDeciding()
    {
        // Arrange
        await using var context = new OutboxContext();
        var mine = await context.QueueAsync(Work);
        context.OutgoingEmails.Arrange(mine, OutgoingEmailStage.Refused);
        context.OutgoingEmails.ArrangeDismissal(mine, SyntheticUser.Deployment);

        // Act
        var outcome = await context.Outbox.RequeueAsync(
            mine,
            refusalRestated: true,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.StageDoesNotAllowIt, outcome);
        Assert.Empty(context.Operations.ReceivedCalls());
    }

    /// <summary>Dismissing an unassigned mailbox's send reports it unknown rather than reaching the decision.</summary>
    [Fact]
    public async Task DismissAsync_ASendFromAMailboxThisUserIsNotAssigned_ReportsItUnknownWithoutDeciding()
    {
        // Arrange
        await using var context = new OutboxContext();
        var theirs = await context.QueueAsync(Unassigned);

        // Act
        var outcome = await context.Outbox.DismissAsync(theirs, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.RecordUnknown, outcome);
        Assert.Empty(context.Operations.ReceivedCalls());
    }

    /// <summary>The dismissal is recorded against the user who made it, which is what the record says of who accepted the failure.</summary>
    [Fact]
    public async Task DismissAsync_ASendFromAMailboxThisUserIsAssigned_RecordsTheDismissalAgainstThisUser()
    {
        // Arrange
        await using var context = new OutboxContext();
        var mine = await context.QueueAsync(Work);
        context.Operations
            .DismissAsync(mine, Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(OutboxDecisionOutcome.Accepted);

        // Act
        var outcome = await context.Outbox.DismissAsync(mine, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutboxDecisionOutcome.Accepted, outcome);
        await context.Operations.Received(1).DismissAsync(
            mine,
            context.Authorization.RequireUser(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A decision that moved a send tells an open client its account's outbox changed, and names nothing else.</summary>
    [Fact]
    public async Task DismissAsync_AnAcceptedDismissal_TellsAnOpenClientTheAccountsOutboxChanged()
    {
        // Arrange
        await using var context = new OutboxContext();
        var mine = await context.QueueAsync(Work);
        context.Operations
            .DismissAsync(mine, Arg.Any<UserId>(), Arg.Any<CancellationToken>())
            .Returns(OutboxDecisionOutcome.Accepted);

        // Act
        await context.Outbox.DismissAsync(mine, TestContext.Current.CancellationToken);
        var published = await context.PublishedSignalsAsync();

        // Assert
        var signal = Assert.Single(published);
        Assert.Equal(ClientSignalKind.OutboxChanged, signal.Kind);
        Assert.Equal(Work, signal.Account);
    }

    /// <summary>A decision the store refused moved nothing, so no client is told anything changed.</summary>
    [Fact]
    public async Task CancelAsync_ADecisionTheStoreRefused_TellsNoClientAnything()
    {
        // Arrange
        await using var context = new OutboxContext();
        var mine = await context.QueueAsync(Work);
        context.Operations
            .CancelAsync(mine, Arg.Any<CancellationToken>())
            .Returns(OutboxDecisionOutcome.StageDoesNotAllowIt);

        // Act
        await context.Outbox.CancelAsync(mine, TestContext.Current.CancellationToken);
        var published = await context.PublishedSignalsAsync();

        // Assert
        Assert.Empty(published);
    }

    private static AccessAuthorization Granting(bool reading, bool sending) =>
        AccessAuthorizations.ForCallerGranted(
        [
            .. reading ? [MailFathomPermission.MailRead] : Array.Empty<MailFathomPermission>(),
            .. sending ? [MailFathomPermission.MailSend] : Array.Empty<MailFathomPermission>(),
        ]);

    private static AccountOutboxEntry Entry(OutgoingEmailId send, string? subject) => new()
    {
        OutgoingEmailId = send,
        AccountId = Work,
        Stage = OutgoingEmailStage.Recorded,
        IsHeldByAttempt = false,
        NamedRecipients = [],
        OtherRecipientCount = 0,
        Subject = subject,
        RecordedAt = DateTimeOffset.UnixEpoch,
        DueAt = null,
        AttemptCount = 0,
        LastFailure = null,
        LastReplyCode = null,
    };

    /// <summary>Assembles the user-facing outbox over stores a test arranges, for a caller holding both grants unless it says otherwise.</summary>
    private sealed class OutboxContext : IAsyncDisposable
    {
        private readonly FakeTimeProvider clock = new(DateTimeOffset.UnixEpoch);
        private readonly RecordingClientSignalChannel signalChannel = new();
        private readonly ClientSignals signals;

        internal OutboxContext(AccessAuthorization? authorization = null)
        {
            this.Authorization = authorization ?? AccessAuthorizations.ForCallerGranted(
                MailFathomPermission.MailRead,
                MailFathomPermission.MailSend);
            this.OutgoingEmails = new InMemoryOutgoingEmailStore(timeProvider: this.clock);
            this.Operations
                .ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>())
                .Returns(new AccountOutboxPage([], NextCursor: null));
            this.signals = new ClientSignals([this.signalChannel], this.clock);

            var languages = Substitute.For<IUserLanguages>();
            languages.LanguageOf(Arg.Any<UserId>()).Returns(UserLanguage.English);

            this.Outbox = new UserOutbox(
                AssignedMailAccountCatalogs.For(
                    this.Authorization,
                    new StubMailAccountAssignments().Assigning(this.Authorization.RequireUser(), Work),
                    SyntheticServedAccount.Of(Work),
                    SyntheticServedAccount.Of(Unassigned)),
                this.OutgoingEmails,
                this.Operations,
                this.ContentStore,
                this.Renderer,
                this.TextReader,
                SensitiveContentEgressGuards.Inactive(),
                new EmailContentReadOptions(),
                languages,
                this.Authorization,
                this.signals,
                this.clock);
        }

        internal AccessAuthorization Authorization { get; }

        internal InMemoryOutgoingEmailStore OutgoingEmails { get; }

        internal IOutboxOperationStore Operations { get; } = Substitute.For<IOutboxOperationStore>();

        internal IEmailContentStore ContentStore { get; } = Substitute.For<IEmailContentStore>();

        internal BoundedBodyEmailContentRenderer Renderer { get; } =
            new("Hello.", attachmentOctetCounts: [120, 4096]);

        internal IOutgoingMailTextReader TextReader { get; } = Substitute.For<IOutgoingMailTextReader>();

        internal UserOutbox Outbox { get; }

        /// <summary>Makes the store answer every page with the one row given.</summary>
        internal void Answering(AccountOutboxEntry entry) =>
            this.Operations
                .ReadAccountOutboxAsync(Arg.Any<OutboxQuery>(), Arg.Any<CancellationToken>())
                .Returns(new AccountOutboxPage([entry], NextCursor: null));

        /// <summary>Holds the message one send will transmit, the way recording it stored it.</summary>
        internal void Storing(OutgoingEmailId send) =>
            this.ContentStore
                .FindOutgoingContentAsync(send, Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(RawMime, RawMime.Length, SHA256.HashData(RawMime.Span)));

        /// <summary>Writes one queued send down for one account.</summary>
        internal async Task<OutgoingEmailId> QueueAsync(MailAccountId account, string? subject = null)
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, "someone@example.test", out var address));

            var opened = await this.OutgoingEmails.OpenAsync(
                Substitute.For<IPersistenceSession>(),
                OutgoingEmailRequest.Create(
                    account,
                    SyntheticUser.Deployment,
                    OutgoingEmailRequester.Command($"mfctl-{Guid.CreateVersion7():N}"),
                    [OutgoingRecipient.Create(address, OutgoingRecipientRole.To)]) with
                {
                    Subject = subject,
                },
                OutgoingEmailPrincipal.Of("test-caller"),
                RawMime.Length,
                TestContext.Current.CancellationToken);

            return opened.Record.Id;
        }

        /// <summary>Lets the publisher's folding window pass and returns what an open client was told.</summary>
        internal async Task<IReadOnlyList<ClientSignal>> PublishedSignalsAsync()
        {
            this.clock.Advance(ClientSignals.FoldingWindow);
            await this.signals.DrainAsync();

            return this.signalChannel.Published;
        }

        public ValueTask DisposeAsync() => this.signals.DisposeAsync();
    }
}
