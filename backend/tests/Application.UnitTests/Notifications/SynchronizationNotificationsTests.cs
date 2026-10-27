// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Notifications;
using MailFathom.Application.Signals;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Notifications;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Notifications;

/// <summary>Covers what a synchronization run tells its user, and what it deliberately says nothing about.</summary>
public sealed class SynchronizationNotificationsTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("work");

    private static readonly MailAccountId SecondAccount =
        MailAccountId.Create("personal");

    private static readonly DateTimeOffset RunInstant = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A condition about a mailbox is a condition about whoever is assigned it, so both mailboxes have a reader.</summary>
    private static readonly StubMailAccountAssignments Assignments = new StubMailAccountAssignments()
        .Assigning(SyntheticUser.Deployment, Account, SecondAccount);

    /// <summary>A few messages are each news of their own, so each is a row naming who sent it and what it is about.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_FiveOrFewerMessages_RecordsOneNotificationPerMessageBySenderAndSubject()
    {
        // Arrange
        var summaries = new InMemoryStoredEmailSummaries();
        var arrived = Arrive(summaries, 2);
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, summaries);

        // Act
        await notifications.ReportArrivedMailAsync(Account, arrived, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                ("Sender 0", "Subject 0", (StoredEmailId?)arrived[0], NotificationCause.MessageArrived),
                ("Sender 1", "Subject 1", arrived[1], NotificationCause.MessageArrived),
            ],
            store.Recorded.Select(notification => (
                notification.Title,
                notification.Body,
                notification.Target.Message,
                notification.Statement!.Cause)));
        Assert.All(store.Recorded, notification =>
        {
            Assert.Equal(NotificationKind.Mail, notification.Kind);
            Assert.Equal(Account.Value, notification.Source);
            Assert.Equal(RunInstant, notification.OccurredAt);
        });
    }

    /// <summary>A sender who gave no name is named by the address they wrote from, which is what a reader recognizes next.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_SenderWithNoDisplayName_IsNamedByTheirAddress()
    {
        // Arrange
        var email = StoredEmailId.Create(Guid.CreateVersion7());
        var summaries = new InMemoryStoredEmailSummaries().With(SyntheticEmailSummaries.Create(
            storedEmailId: email.Value,
            accountId: Account.Value,
            senderAddress: "someone@example.test"));
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, summaries);

        // Act
        await notifications.ReportArrivedMailAsync(Account, [email], TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Equal("someone@example.test", notification.Title);
        Assert.Equal("No subject", notification.Body);
        Assert.Equal(0, notification.Statement!.Counted);
    }

    /// <summary>A message erased before the report read it still arrived, so it is said by no name rather than dropped.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_MessageNoLongerHeld_IsStillReportedByNoSender()
    {
        // Arrange
        var email = StoredEmailId.Create(Guid.CreateVersion7());
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, new InMemoryStoredEmailSummaries());

        // Act
        await notifications.ReportArrivedMailAsync(Account, [email], TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Equal("Unknown sender", notification.Title);
        Assert.Equal(email, notification.Target.Message);
    }

    /// <summary>More than five at once is one arrival to somebody who was away, and nobody is shown names they would not read.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_MoreThanFiveMessages_RecordsOneMergedNotificationWithoutReadingMail()
    {
        // Arrange
        var summaries = new InMemoryStoredEmailSummaries();
        var arrived = Arrive(summaries, 6);
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, summaries);

        // Act
        await notifications.ReportArrivedMailAsync(Account, arrived, TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Equal(NotificationCause.MailArrived, notification.Statement!.Cause);
        Assert.Equal(6, notification.Statement.Counted);
        Assert.Equal("6 new messages arrived.", notification.Body);
        Assert.Equal(NotificationScreen.Mail, notification.Target.Screen);
        Assert.Empty(summaries.Calls);
        Assert.Equal(
            arrived,
            await store.ReadNamedEmailsAsync(SyntheticUser.Deployment, notification.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>The arrival that takes an account past five replaces every row standing for it with one counting them all.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_ArrivalTakingTheAccountPastFive_MergesTheStandingRowsIntoOne()
    {
        // Arrange
        var summaries = new InMemoryStoredEmailSummaries();
        var first = Arrive(summaries, 4);
        var second = Arrive(summaries, 2, from: 4);
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, summaries);
        await notifications.ReportArrivedMailAsync(Account, first, TestContext.Current.CancellationToken);

        // Act
        await notifications.ReportArrivedMailAsync(Account, second, TestContext.Current.CancellationToken);

        // Assert
        var merged = Assert.Single(store.Recorded);
        Assert.Equal(6, merged.Statement!.Counted);
        Assert.Equal(
            [.. first, .. second],
            await store.ReadNamedEmailsAsync(SyntheticUser.Deployment, merged.Id, TestContext.Current.CancellationToken));
    }

    /// <summary>While a merged row stands unread, a later arrival is raised anew as one row counting it too.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_MergedRowStandingUnread_ReplacesItWithOneCountingTheLaterArrival()
    {
        // Arrange
        var summaries = new InMemoryStoredEmailSummaries();
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, summaries);
        await notifications.ReportArrivedMailAsync(Account, Arrive(summaries, 6), TestContext.Current.CancellationToken);
        var earlier = Assert.Single(store.Recorded);

        // Act
        await notifications.ReportArrivedMailAsync(Account, Arrive(summaries, 1, from: 6), TestContext.Current.CancellationToken);

        // Assert
        var later = Assert.Single(store.Recorded);
        Assert.NotEqual(earlier.Id, later.Id);
        Assert.Equal(7, later.Statement!.Counted);
    }

    /// <summary>Reading the merged row ends it, so what arrives next is said a message at a time again.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_MergedRowAlreadyRead_StartsAgainAMessageAtATime()
    {
        // Arrange
        var summaries = new InMemoryStoredEmailSummaries();
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, summaries);
        await notifications.ReportArrivedMailAsync(Account, Arrive(summaries, 6), TestContext.Current.CancellationToken);
        await store.SetReadAsync(
            SyntheticUser.Deployment,
            Assert.Single(store.Recorded).Id,
            isRead: true,
            TestContext.Current.CancellationToken);

        // Act
        await notifications.ReportArrivedMailAsync(Account, Arrive(summaries, 1, from: 6), TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [NotificationCause.MailArrived, NotificationCause.MessageArrived],
            store.Recorded.Select(notification => notification.Statement!.Cause));
    }

    /// <summary>Five is counted per account, so one busy account never merges another's mail into its count.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_ArrivalsInTwoAccounts_FoldsEachAccountOnItsOwn()
    {
        // Arrange
        var summaries = new InMemoryStoredEmailSummaries();
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, summaries);
        await notifications.ReportArrivedMailAsync(Account, Arrive(summaries, 5), TestContext.Current.CancellationToken);

        // Act
        await notifications.ReportArrivedMailAsync(
            SecondAccount,
            Arrive(summaries, 1, from: 5, account: SecondAccount),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(6, store.Recorded.Count);
        Assert.All(store.Recorded, notification =>
            Assert.Equal(NotificationCause.MessageArrived, notification.Statement!.Cause));
    }

    /// <summary>A message reported twice is one arrival, so the second report says nothing.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_MessageAlreadyStanding_RecordsNothingASecondTime()
    {
        // Arrange
        var summaries = new InMemoryStoredEmailSummaries();
        var arrived = Arrive(summaries, 2);
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store, summaries);
        await notifications.ReportArrivedMailAsync(Account, arrived, TestContext.Current.CancellationToken);

        // Act
        var recorded = await notifications.ReportArrivedMailAsync(Account, arrived, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(recorded);
        Assert.Equal(2, store.Recorded.Count);
    }

    /// <summary>An empty run is the ordinary case and is not an event anybody was away from the screen for.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_RunThatCommittedNothing_RecordsNothing()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        var recorded = await notifications.ReportArrivedMailAsync(Account, [], TestContext.Current.CancellationToken);

        // Assert
        Assert.False(recorded);
        Assert.Empty(store.Recorded);
    }

    /// <summary>A refused credential is refused again on every run, and a person who has not read the first statement gains nothing from a second.</summary>
    [Fact]
    public async Task ReportRefusedCredentialAsync_ConditionRepeatedWhileUnread_RecordsItOnce()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        var first = await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);
        var second = await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(first);
        Assert.False(second);
        Assert.Single(store.Recorded);
    }

    /// <summary>The condition is one account's, so a second account being refused is its own statement rather than a repeat.</summary>
    [Fact]
    public async Task ReportRefusedCredentialAsync_SecondAccountRefused_RecordsItsOwnStatement()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);
        await notifications.ReportRefusedCredentialAsync(SecondAccount, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, store.Recorded.Count);
        Assert.Equal(
            [Account.Value, SecondAccount.Value],
            store.Recorded.Select(notification => notification.Source));
    }

    /// <summary>A refused credential is the person's to repair, so the statement leads to where they repair it.</summary>
    [Fact]
    public async Task ReportRefusedCredentialAsync_RefusedAccount_LeadsToTheSettingsScreen()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Equal(NotificationKind.System, notification.Kind);
        Assert.Equal(NotificationScreen.Settings, notification.Target.Screen);
    }

    /// <summary>The condition and its numbers are what a client says the row in its own language, so every raise carries them.</summary>
    /// <remarks>
    /// Stated as one test over the three producers rather than three, because the claim is about the set: a condition
    /// added later that composed a sentence and no statement would leave a client with English it cannot translate,
    /// and only reading them together says so.
    /// </remarks>
    [Fact]
    public async Task Reporting_EachConditionARunObserves_StatesItAsACauseAndItsCounts()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportArrivedMailAsync(
            Account,
            [.. Enumerable.Range(0, 40).Select(_ => StoredEmailId.Create(Guid.CreateVersion7()))],
            TestContext.Current.CancellationToken);
        await notifications.ReportIncompleteRunAsync(Account, 2, 5, TestContext.Current.CancellationToken);
        await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (NotificationCause.MailArrived, (int?)40, (int?)null),
                (NotificationCause.SynchronizationIncomplete, 2, 5),
                (NotificationCause.CredentialRefused, null, null),
            ],
            store.Recorded.Select(notification =>
                (notification.Statement?.Cause, notification.Statement?.Counted, notification.Statement?.OutOf)));
    }

    /// <summary>An incomplete run and a refused credential are different conditions, so one never suppresses the other.</summary>
    [Fact]
    public async Task ReportIncompleteRunAsync_AccountAlreadyReportedAsRefused_RecordsItsOwnCondition()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportRefusedCredentialAsync(Account, TestContext.Current.CancellationToken);
        await notifications.ReportIncompleteRunAsync(Account, 2, 5, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, store.Recorded.Count);
        Assert.Contains(store.Recorded, notification => notification.Body.Contains(
            "2 of 5 folders",
            StringComparison.Ordinal));
    }

    /// <summary>A run that finished everything it scheduled is not news.</summary>
    [Fact]
    public async Task ReportIncompleteRunAsync_RunThatFinishedEveryFolder_RecordsNothing()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        var recorded = await notifications.ReportIncompleteRunAsync(
            Account,
            failedFolderCount: 0,
            scheduledFolderCount: 5,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(recorded);
        Assert.Empty(store.Recorded);
    }

    /// <summary>An incomplete run is something MailFathom keeps working on, so there is nothing for the person to open.</summary>
    [Fact]
    public async Task ReportIncompleteRunAsync_IncompleteRun_LeadsNowhere()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = CreateNotifications(store);

        // Act
        await notifications.ReportIncompleteRunAsync(Account, 2, 5, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(NotificationTargetKind.Nothing, Assert.Single(store.Recorded).Target.Kind);
    }

    /// <summary>A caller with no list of arrivals has miscounted rather than reported a run with nothing to say.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_NoListOfArrivals_IsRefused()
    {
        // Arrange
        var notifications = CreateNotifications(new InMemoryNotificationStore());

        // Act
        var refusal = await Assert.ThrowsAsync<ArgumentNullException>(
            () => notifications.ReportArrivedMailAsync(Account, null!, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("arrivedEmails", refusal.ParamName);
    }

    /// <summary>
    /// Nothing bounds a configured account identifier, and both places one reaches are bounded, so an outsized one is
    /// still reported rather than silently disabling every notification that account would ever produce.
    /// </summary>
    [Fact]
    public async Task ReportRefusedCredentialAsync_AnAccountIdentifierPastEveryBound_IsStillRecorded()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var outsized = MailAccountId.Create(new string('w', 400));
        var notifications = new SynchronizationNotifications(
            new NotificationRaiser(store, ClientSignalPublishers.ReachingNobody),
            new StubMailAccountAssignments().Assigning(SyntheticUser.Deployment, outsized),
            new InMemoryStoredEmailSummaries(),
            new FakeTimeProvider(RunInstant));

        // Act
        await notifications.ReportRefusedCredentialAsync(outsized, TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Null(notification.Source);
        Assert.StartsWith("credential-refused:", notification.DeduplicationKey.Value, StringComparison.Ordinal);
        Assert.True(notification.DeduplicationKey.Value.Length <= NotificationDeduplicationKey.MaximumLength);
    }

    /// <summary>A row written while somebody has the client open is said out loud, with what the bell has to draw.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_WithAClientListening_SignalsTheRaisedNotificationAndTheUnreadCount()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider(RunInstant);
        await using var signals = new ClientSignals([channel], clock);
        var summaries = new InMemoryStoredEmailSummaries();
        var notifications = new SynchronizationNotifications(
            new NotificationRaiser(store, signals),
            Assignments,
            summaries,
            clock);

        // Act
        await notifications.ReportArrivedMailAsync(Account, Arrive(summaries, 1), TestContext.Current.CancellationToken);

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        var signal = Assert.Single(channel.Published);
        Assert.Equal(ClientSignalKind.NotificationRaised, signal.Kind);
        Assert.Equal(SyntheticUser.Deployment, signal.User);
        Assert.Equal(NotificationKind.Mail, signal.NotificationKind);
        Assert.Equal(1, signal.Count);
        Assert.Equal(Assert.Single(store.Recorded).Body, signal.SecondLine);
    }

    /// <summary>A notification the store recognized as one already raised says nothing to a client either.</summary>
    [Fact]
    public async Task ReportArrivedMailAsync_ARowTheStoreDeduplicated_SignalsNothingASecondTime()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var channel = new RecordingClientSignalChannel();
        var clock = new FakeTimeProvider(RunInstant);
        await using var signals = new ClientSignals([channel], clock);
        var summaries = new InMemoryStoredEmailSummaries();
        var arrived = Arrive(summaries, 1);
        var notifications = new SynchronizationNotifications(
            new NotificationRaiser(store, signals),
            Assignments,
            summaries,
            clock);

        // Act
        await notifications.ReportArrivedMailAsync(Account, arrived, TestContext.Current.CancellationToken);
        await notifications.ReportArrivedMailAsync(Account, arrived, TestContext.Current.CancellationToken);

        clock.Advance(ClientSignals.FoldingWindow);
        await signals.DrainAsync();

        // Assert
        Assert.Single(store.Recorded);
        Assert.Single(channel.Published);
    }

    private static SynchronizationNotifications CreateNotifications(
        INotificationStore store,
        InMemoryStoredEmailSummaries? summaries = null) =>
        new(
            new NotificationRaiser(store, ClientSignalPublishers.ReachingNobody),
            Assignments,
            summaries ?? new InMemoryStoredEmailSummaries(),
            new FakeTimeProvider(RunInstant));

    /// <summary>Stores messages numbered from <paramref name="from" />, each with a sender's name and a subject, and names them as one run's arrival.</summary>
    private static StoredEmailId[] Arrive(
        InMemoryStoredEmailSummaries summaries,
        int count,
        int from = 0,
        MailAccountId? account = null) =>
    [
        .. Enumerable.Range(from, count).Select(number =>
        {
            var email = StoredEmailId.Create(Guid.CreateVersion7());

            summaries.With(SyntheticEmailSummaries.Create(
                storedEmailId: email.Value,
                accountId: (account ?? Account).Value,
                subject: $"Subject {number}",
                senderAddress: $"sender{number}@example.test") with
            {
                SenderDisplayName = $"Sender {number}",
            });

            return email;
        }),
    ];
}
