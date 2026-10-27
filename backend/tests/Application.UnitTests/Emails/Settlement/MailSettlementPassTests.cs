// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Settlement;
using MailFathom.Application.Notifications;
using MailFathom.Application.Signals;
using MailFathom.Application.Spam;
using MailFathom.Application.Spam.Gating;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Spam;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
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
        Assert.Equal(new MailSettlementReport(SettledCount: 1, AnnouncedCount: 1, StillSettlingCount: 1), report);
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
        Assert.Equal(new MailSettlementReport(SettledCount: 1, AnnouncedCount: 0, StillSettlingCount: 0), report);
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

    private MailSettlementPass CreatePass(ClientSignals signals) =>
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
            this.clock);

    private async Task DrainAsync(ClientSignals signals)
    {
        this.clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();
    }
}
