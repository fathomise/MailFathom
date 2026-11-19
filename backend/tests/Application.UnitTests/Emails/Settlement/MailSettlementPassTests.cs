// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.AutomaticReplies;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Emails.Settlement;
using MailFathom.Application.Emails.Summaries;
using MailFathom.Application.Mail.Delivery.Composition;
using MailFathom.Application.Notifications;
using MailFathom.Application.Signals;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.Tasks;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.Domain.Tasks;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Emails.Settlement;

/// <summary>Covers what one settlement pass settles, what it tells a client and the user, and what it leaves to a competing run.</summary>
public sealed class MailSettlementPassTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailAccountId Account = MailAccountId.Create("primary");

    private static readonly MailFolderAlias Inbox = MailFolderAlias.Create("INBOX");

    private readonly FakeTimeProvider clock = new(Now);
    private readonly RecordingClientSignalChannel channel = new();
    private readonly InMemoryNotificationStore notificationStore = new();
    private readonly IMailSettlementTelemetry telemetry = Substitute.For<IMailSettlementTelemetry>();
    private readonly InMemoryMailSettlementStore store = new();

    /// <summary>A message is announced once, in the folder it settled in, and one still being placed is not announced at all.</summary>
    [Fact]
    public async Task RunAsync_ASettledAndAStillSettlingMessage_SettlesAndAnnouncesTheFirstAlone()
    {
        // Arrange
        var ready = Email(1);
        var waiting = Email(2) with { AwaitsPlacement = true };
        this.store.Holding(ready, waiting);
        await using var signals = new ClientSignals([this.channel], this.clock);

        // Act
        var report = await this.CreatePass(signals).RunAsync(Account, TestContext.Current.CancellationToken);
        await this.DrainAsync(signals);

        // Assert
        Assert.Equal((1, 1, 1), (report.SettledCount, report.AnnouncedCount, report.StillSettlingCount));
        Assert.Empty(report.FollowUpFailures);
        Assert.Equal([ready.Id], this.store.Settled);
        var signal = Assert.Single(this.channel.Published);
        Assert.Equal(ClientSignalKind.MailArrived, signal.Kind);
        Assert.Equal(1, signal.Count);
        Assert.Equal([(StoredEmailId?)ready.Id], this.notificationStore.Recorded.Select(static notification => notification.Target.Message));
    }

    /// <summary>A message filed into junk settles and reaches a screen, and the user is told nothing about it.</summary>
    [Fact]
    public async Task RunAsync_AMessageSettledInJunk_SignalsTheArrivalAndRaisesNoNotification()
    {
        // Arrange
        this.store.Holding(Email(1, SpamVerdict.Spam) with { FolderRole = MailFolderSpecialUse.Junk });
        await using var signals = new ClientSignals([this.channel], this.clock);

        // Act
        var report = await this.CreatePass(signals).RunAsync(Account, TestContext.Current.CancellationToken);
        await this.DrainAsync(signals);

        // Assert
        Assert.Equal((1, 0, 0), (report.SettledCount, report.AnnouncedCount, report.StillSettlingCount));
        Assert.Empty(report.FollowUpFailures);
        Assert.Single(this.channel.Published);
        Assert.Empty(this.notificationStore.Recorded);
    }

    /// <summary>The run that settled a message is the one that announces it, so two replicas raise one signal and one notification.</summary>
    [Fact]
    public async Task RunAsync_AMessageACompetingRunSettledFirst_IsNeitherSignalledNorAnnouncedAgain()
    {
        // Arrange
        var email = Email(1);
        this.store.Holding(email).SettledElsewhere(email.Id);
        await using var signals = new ClientSignals([this.channel], this.clock);

        // Act
        var report = await this.CreatePass(signals).RunAsync(Account, TestContext.Current.CancellationToken);
        await this.DrainAsync(signals);

        // Assert
        Assert.Equal(0, report.SettledCount);
        Assert.Empty(this.channel.Published);
        Assert.Empty(this.notificationStore.Recorded);
        this.telemetry.DidNotReceive().RecordSettled(Arg.Any<MailSettlement>());
    }

    /// <summary>A message shown before a stage finished is counted as released, so a stalled stage is visible to an operator.</summary>
    [Fact]
    public async Task RunAsync_AMessagePastTheLongestWait_IsSettledAndCountedAsReleased()
    {
        // Arrange
        var stuck = Email(1) with
        {
            AwaitsPlacement = true,
            StoredAt = Now - SpamClassificationScope.DefaultMaximumClassificationWait - MailSettlementPass.MaximumPlacementWait,
        };
        this.store.Holding(stuck);

        // Act
        await this.CreatePass(ClientSignalPublishers.ReachingNobody).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([stuck.Id], this.store.Settled);
        this.telemetry.Received(1).RecordSettled(MailSettlement.ReleasedAfterWaiting);
    }

    /// <summary>The walk reaches past its first batch, so a large arrival settles in one run rather than a batch per run.</summary>
    [Fact]
    public async Task RunAsync_MoreSettlingMessagesThanOneBatch_SettlesEveryOneOfThem()
    {
        // Arrange
        SettlingEmail[] arrival = [.. Enumerable.Range(1, 450).Select(number => Email(number) with { FolderRole = null, ArrivedInInbox = false })];
        this.store.Holding(arrival);

        // Act
        var report = await this.CreatePass(ClientSignalPublishers.ReachingNobody).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(arrival.Length, report.SettledCount);
        Assert.Equal(arrival.Select(static email => email.Id.Value).Order(), this.store.Settled.Select(static email => email.Value).Order());
    }

    /// <summary>A full batch of mail still waiting does not stop the walk, so what settles behind it is reached in the same run.</summary>
    [Fact]
    public async Task RunAsync_MoreThanABatchStillSettlingAheadOfReadyMail_SettlesTheMailBehindIt()
    {
        // Arrange
        var waitingCount = MailSettlementPass.BatchSize + 50;
        SettlingEmail[] waiting = [.. Enumerable.Range(1, waitingCount).Select(number => Email(number) with { AwaitsPlacement = true })];
        SettlingEmail[] ready = [.. Enumerable.Range(waitingCount + 1, 10).Select(number => Email(number))];
        this.store.Holding([.. waiting, .. ready]);

        // Act
        var report = await this.CreatePass(ClientSignalPublishers.ReachingNobody).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(ready.Select(static email => email.Id.Value), this.store.Settled.Select(static email => email.Value));
        Assert.Equal(waitingCount, report.StillSettlingCount);
    }

    /// <summary>One run reads a bounded number of batches, and whatever lies past them waits for the next run.</summary>
    [Fact]
    public async Task RunAsync_MoreSettlingMailThanOneRunReads_StopsAtTheBatchBound()
    {
        // Arrange
        var bound = MailSettlementPass.BatchSize * MailSettlementPass.MaxBatchesPerPass;
        this.store.Holding([.. Enumerable.Range(1, bound + 5).Select(number => Email(number) with { AwaitsPlacement = true })]);

        // Act
        var report = await this.CreatePass(ClientSignalPublishers.ReachingNobody).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(bound, report.StillSettlingCount);
    }

    /// <summary>A settled message is never read again, so a batch is announced as it settles rather than after a walk that may not finish.</summary>
    [Fact]
    public async Task RunAsync_AWalkThatFailsAfterABatchSettled_HasAlreadyAnnouncedThatBatch()
    {
        // Arrange
        SettlingEmail[] arrival = [.. Enumerable.Range(1, MailSettlementPass.BatchSize + 1).Select(number => Email(number))];
        this.store.Holding(arrival).FailingAfter(reads: 1);
        await using var signals = new ClientSignals([this.channel], this.clock);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => this.CreatePass(signals).RunAsync(Account, TestContext.Current.CancellationToken));
        await this.DrainAsync(signals);

        // Assert
        Assert.Equal(MailSettlementPass.BatchSize, this.store.Settled.Count);
        Assert.Equal(MailSettlementPass.BatchSize, Assert.Single(this.channel.Published).Count);
        Assert.NotEmpty(this.notificationStore.Recorded);
    }

    /// <summary>
    /// What settles while the account's user's automatic reply runs is answered once settled, and junk that settles
    /// beside it is not answered at all.
    /// </summary>
    [Fact]
    public async Task RunAsync_ArrivalsSettlingWhileAnAutomaticReplyRuns_AnswersTheInboxArrivalAndNotTheJunk()
    {
        // Arrange
        var world = new AutomaticReplyWorld(this.clock, periodAround: Now);
        await world.AnswerAsync();
        var inbox = Email(1) with { Id = world.Arrive("anna@example.test") };
        var junk = Email(2, SpamVerdict.Spam) with { Id = world.Arrive("spammer@example.test") };
        this.store.Holding(inbox, junk);
        await using var signals = new ClientSignals([this.channel], this.clock);

        // Act
        await this.CreatePass(signals, world.Responder).RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("anna@example.test", Assert.Single(Assert.Single(world.Outgoing.OpenRequests).Recipients).Address.Address);
    }

    /// <summary>The run that settled a message is the one that answers it, so a competing replica's settlement leaves this run nothing to send.</summary>
    [Fact]
    public async Task RunAsync_AnInboxArrivalACompetingRunSettledFirst_IsNotAnsweredHere()
    {
        // Arrange
        var world = new AutomaticReplyWorld(this.clock, periodAround: Now);
        await world.AnswerAsync();
        var inbox = Email(1) with { Id = world.Arrive("anna@example.test") };
        this.store.Holding(inbox).SettledElsewhere(inbox.Id);

        // Act
        await this.CreatePass(ClientSignalPublishers.ReachingNobody, world.Responder)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(world.Composed);
        Assert.Empty(world.Outgoing.OpenRequests);
    }

    /// <summary>A reply the user was waiting on closes the wait as it settles, from the same arrivals the automatic reply sees.</summary>
    [Fact]
    public async Task RunAsync_AReplySettlesInAThreadAWaitIsAnchoredTo_ClosesTheWait()
    {
        // Arrange
        var (inbox, summaries, tasks) = await this.ArrangeReplyToAWaitAsync();
        await using var signals = new ClientSignals([this.channel], this.clock);

        // Act
        await this.CreatePass(signals, awaitedReplies: this.AwaitedRepliesOver(summaries, tasks, signals))
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(inbox.Id, Assert.Single(tasks.Held).Answer?.Message);
    }

    /// <summary>A failed answering is reported with the type of what failed and does not stop the waits that settled mail closes.</summary>
    [Fact]
    public async Task RunAsync_TheAutomaticReplyFails_StillClosesTheWait()
    {
        // Arrange
        var (inbox, summaries, tasks) = await this.ArrangeReplyToAWaitAsync();
        var world = new AutomaticReplyWorld(this.clock, periodAround: Now) { FailsReadingContent = true };
        await world.AnswerAsync();
        world.Summaries.With((await summaries.FindAsync(inbox.Id, TestContext.Current.CancellationToken))!);
        await using var signals = new ClientSignals([this.channel], this.clock);

        // Act
        var report = await this.CreatePass(signals, world.Responder, this.AwaitedRepliesOver(summaries, tasks, signals))
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, report.SettledCount);
        Assert.Equal(inbox.Id, Assert.Single(tasks.Held).Answer?.Message);
        this.telemetry.Received(1).RecordFollowUpFailed(MailSettlementFollowUp.AutomaticReply);
        Assert.Equal(
            new MailSettlementFollowUpFailure(MailSettlementFollowUp.AutomaticReply, nameof(InvalidOperationException)),
            Assert.Single(report.FollowUpFailures));
    }

    /// <summary>A failure closing waits is reported with its error code and neither fails the pass nor undoes the settlement it follows.</summary>
    [Fact]
    public async Task RunAsync_ClosingTheWaitsFails_StillSettlesAndReportsTheBatch()
    {
        // Arrange
        var inbox = Email(1);
        this.store.Holding(inbox);
        var failing = Substitute.For<IStoredEmailSummaryReader>();
        failing.ReadSummariesAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(PrincipalNotAuthorizedException.NoUser());
        await using var signals = new ClientSignals([this.channel], this.clock);
        var awaited = this.AwaitedRepliesOver(failing, new InMemoryPersonalTaskStore(), signals);

        // Act
        var report = await this.CreatePass(signals, awaitedReplies: awaited)
            .RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal((1, 1, 0), (report.SettledCount, report.AnnouncedCount, report.StillSettlingCount));
        Assert.Equal([inbox.Id], this.store.Settled);
        this.telemetry.Received(1).RecordFollowUpFailed(MailSettlementFollowUp.AwaitedReplies);
        Assert.Equal(
            new MailSettlementFollowUpFailure(
                MailSettlementFollowUp.AwaitedReplies,
                PrincipalNotAuthorizedException.NoUser().ErrorCode.ToString()),
            Assert.Single(report.FollowUpFailures));
    }

    /// <summary>A follow-up cut short by the pass being cancelled is the cancellation reaching the caller, never a follow-up failure.</summary>
    [Fact]
    public async Task RunAsync_CancelledWhileClosingTheWaits_FaultsWithTheCancellationAndRecordsNoFailure()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        this.store.Holding(Email(1));
        var interrupted = Substitute.For<IStoredEmailSummaryReader>();
        interrupted.ReadSummariesAsync(Arg.Any<IReadOnlyList<StoredEmailId>>(), Arg.Any<CancellationToken>())
            .Returns<Task<IReadOnlyDictionary<StoredEmailId, EmailSummary>>>(_ =>
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            });
        await using var signals = new ClientSignals([this.channel], this.clock);
        var awaited = this.AwaitedRepliesOver(interrupted, new InMemoryPersonalTaskStore(), signals);

        // Act
        var run = () => this.CreatePass(signals, awaitedReplies: awaited).RunAsync(Account, cancellation.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(run);
        this.telemetry.DidNotReceive().RecordFollowUpFailed(Arg.Any<MailSettlementFollowUp>());
    }

    private static SettlingEmail Email(int number, SpamVerdict? verdict = SpamVerdict.NotSpam) =>
        new(
            StoredEmailId.Create(Guid.Parse($"0199a0c0-0000-7000-8000-{number:D12}")),
            Account,
            Inbox,
            Now,
            StoredEmailContentAvailability.Available,
            verdict,
            AreRulesFinished: true,
            IsTombstoned: false,
            AwaitsPlacement: false,
            IsSeen: false,
            ArrivedInInbox: true,
            MailFolderSpecialUse.Inbox);

    private MailSettlementPass CreatePass(ClientSignals signals, AutomaticReplyResponder? responder = null, AwaitedReplies? awaitedReplies = null) =>
        new(
            this.store,
            new DerivedWorkGate(
                new StubSpamClassificationSettingsReader(SpamClassificationSettings.Create(isEnabled: true, [Inbox]), [Account]),
                StubDeploymentMailFolders.None,
                this.clock),
            signals,
            new SynchronizationNotifications(
                new NotificationRaiser(this.notificationStore, ClientSignalPublishers.ReachingNobody),
                new StubMailAccountAssignments().Assigning(SyntheticUser.Deployment, Account),
                new InMemoryStoredEmailSummaries(),
                this.clock),
            this.telemetry,
            responder ?? new AutomaticReplyWorld(this.clock).Responder,
            awaitedReplies ?? this.NoAwaitedReplies(signals),
            this.clock);

    private AwaitedReplies NoAwaitedReplies(ClientSignals signals) =>
        this.AwaitedRepliesOver(new InMemoryStoredEmailSummaries(), new InMemoryPersonalTaskStore(), signals);

    private AwaitedReplies AwaitedRepliesOver(IStoredEmailSummaryReader summaries, IPersonalTaskStore tasks, ClientSignals signals)
    {
        var identities = Substitute.For<IOutgoingSenderIdentityReader>();
        identities.FindSenderIdentity(Account).Returns(
            OutgoingSenderIdentity.Create(Account, AutomaticReplyWorld.Address(AutomaticReplyWorld.OwnAddress)));

        return new AwaitedReplies(
            summaries,
            Substitute.For<IEmailContentStore>(),
            Substitute.For<IArrivedMailEnvelopeReader>(),
            tasks,
            identities,
            signals);
    }

    private async Task<(SettlingEmail Inbox, InMemoryStoredEmailSummaries Summaries, InMemoryPersonalTaskStore Tasks)> ArrangeReplyToAWaitAsync()
    {
        var thread = EmailThreadId.Create(new Guid("0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92"));
        var inbox = Email(1);
        var summaries = new InMemoryStoredEmailSummaries().With(
            SyntheticEmailSummaries.Create(
                receivedAt: Now,
                storedEmailId: inbox.Id.Value,
                accountId: Account.Value,
                senderAddress: "buyer@example.test",
                threadId: thread.Value));
        var tasks = new InMemoryPersonalTaskStore();

        await tasks.AddAsync(
            PersonalTask.Compose(
                PersonalTaskId.Create(Guid.CreateVersion7(Now)),
                SyntheticUser.Deployment,
                "Hear back about the tender",
                dueOn: null,
                TaskAnnouncement.Silent,
                PersonalTaskOrigin.Asserted,
                sourceMessage: null,
                new TaskCommitment(PersonalTaskDirection.Awaited, null, null, TaskAnchor.Thread(thread, "Tender"), null),
                Now.AddHours(-1)),
            TestContext.Current.CancellationToken);
        this.store.Holding(inbox);

        return (inbox, summaries, tasks);
    }

    private async Task DrainAsync(ClientSignals signals)
    {
        this.clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();
    }
}
