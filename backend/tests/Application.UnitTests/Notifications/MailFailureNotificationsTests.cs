// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Notifications;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Drafts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.Domain.Notifications;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Notifications;

/// <summary>Covers what a person is told when a send, a mailbox change, or a draft's server copy fails after the request answered.</summary>
public sealed class MailFailureNotificationsTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("work");

    private static readonly UserId Colleague = UserId.Create(Guid.NewGuid());

    private static readonly DateTimeOffset DecidedAt = new(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

    private static readonly MailFolderResolution Inbox = MailFolderResolution.FirstBindingOf(
        MailFolderAlias.Create("inbox"),
        RemoteFolderPath.Create("INBOX", '/'));

    /// <summary>A send belongs to the account, so everybody assigned it is told, each once, with the subject as the body.</summary>
    [Fact]
    public async Task ReportRefusedSendAsync_ASendWithASubject_TellsEveryAssignedUserOnceLeadingToTheSend()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var notifications = Create(store);
        var send = RefusedSend() with { Subject = "Contract addendum" };

        // Act
        await notifications.ReportRefusedSendAsync(send, TestContext.Current.CancellationToken);
        await notifications.ReportRefusedSendAsync(send, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                (SyntheticUser.Deployment, "Contract addendum", (OutgoingEmailId?)send.Id, 1),
                (Colleague, "Contract addendum", send.Id, 1),
            ],
            store.Recorded.Select(notification => (
                notification.User,
                notification.Body,
                notification.Target.OutgoingEmail,
                notification.Statement!.Counted)));
        Assert.All(store.Recorded, notification =>
        {
            Assert.Equal(NotificationCause.SendRefused, notification.Statement!.Cause);
            Assert.Equal(NotificationKind.Mail, notification.Kind);
            Assert.Equal(Account.Value, notification.Source);
            Assert.Equal(DecidedAt, notification.OccurredAt);
        });
    }

    /// <summary>A send with no subject counts none, which is what tells a client to word the missing subject itself.</summary>
    [Fact]
    public async Task ReportRefusedSendAsync_ASendWithoutASubject_CountsNoSubject()
    {
        // Arrange
        var store = new InMemoryNotificationStore();

        // Act
        await Create(store).ReportRefusedSendAsync(RefusedSend(), TestContext.Current.CancellationToken);

        // Assert
        Assert.All(store.Recorded, notification => Assert.Equal(0, notification.Statement!.Counted));
    }

    /// <summary>A change somebody asked for names the act and leads to both the change and its message.</summary>
    [Fact]
    public async Task ReportAbandonedChangeAsync_APersonsChange_NamesTheActAndLeadsToTheChangeAndTheMessage()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var change = Abandoned(MailboxMutationRequest.SetFlagged(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Occurrence(),
            MailboxMutationRequester.Command("client-flag-1"),
            isFlagged: false));

        // Act
        var kept = await Create(store).ReportAbandonedChangeAsync(change, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(kept);
        Assert.All(store.Recorded, notification =>
        {
            Assert.Equal(NotificationCause.MailboxChangeAbandoned, notification.Statement!.Cause);
            Assert.Equal((int)MailboxChangeAct.Unflag, notification.Statement.Counted);
            Assert.Equal(change.Id, notification.Target.MailboxChange);
            Assert.Equal(change.Request.StoredEmailId, notification.Target.Message);
        });
        Assert.Equal(2, store.Recorded.Count);
    }

    /// <summary>A rule's or a classifier's change is nobody's act in person, so nobody is told it was given up.</summary>
    [Fact]
    public async Task ReportAbandonedChangeAsync_ARulesChange_TellsNobody()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var change = Abandoned(MailboxMutationRequest.SetSeen(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Occurrence(),
            MailboxMutationRequester.Rule("mark-newsletters-read", "2"),
            isSeen: true));

        // Act
        var kept = await Create(store).ReportAbandonedChangeAsync(change, TestContext.Current.CancellationToken);

        // Assert
        Assert.False(kept);
        Assert.Empty(store.Recorded);
    }

    /// <summary>The act folds the command and the value it was asked with into the one thing a person did.</summary>
    [Theory]
    [InlineData(true, MailboxChangeAct.MarkRead)]
    [InlineData(false, MailboxChangeAct.MarkUnread)]
    public void ActOf_ASeenChange_IsReadOrUnreadByTheValueAskedFor(bool isSeen, MailboxChangeAct expected)
    {
        // Arrange
        var request = MailboxMutationRequest.SetSeen(
            StoredEmailId.Create(Guid.CreateVersion7()),
            Occurrence(),
            MailboxMutationRequester.Command("client-seen-1"),
            isSeen);

        // Act, Assert
        Assert.Equal(expected, MailFailureNotifications.ActOf(request));
    }

    /// <summary>A draft is its writer's alone, so only they are told, whoever else is assigned the account.</summary>
    [Fact]
    public async Task ReportDraftCopyNotKeptAsync_ADraft_TellsItsWriterAloneLeadingToTheDraft()
    {
        // Arrange
        var store = new InMemoryNotificationStore();
        var draft = Draft(Colleague, "Q4 supplier review");

        // Act
        await Create(store).ReportDraftCopyNotKeptAsync(draft, TestContext.Current.CancellationToken);

        // Assert
        var notification = Assert.Single(store.Recorded);
        Assert.Equal(Colleague, notification.User);
        Assert.Equal("Q4 supplier review", notification.Body);
        Assert.Equal(NotificationCause.DraftCopyNotKept, notification.Statement!.Cause);
        Assert.Equal(1, notification.Statement.Counted);
        Assert.Equal(draft.Id, notification.Target.MailDraft);
    }

    private static MailFailureNotifications Create(InMemoryNotificationStore store) => new(
        new NotificationRaiser(store, ClientSignalPublishers.ReachingNobody),
        new StubMailAccountAssignments()
            .Assigning(SyntheticUser.Deployment, Account)
            .Assigning(Colleague, Account),
        new FakeTimeProvider(DecidedAt));

    private static OutgoingEmailRecord RefusedSend()
    {
        Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

        return new InMemoryOutgoingEmailStore(timeProvider: new FakeTimeProvider(DecidedAt)).Publish(
            OutgoingEmailRequest.Create(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command("client-send-1"),
                [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To)]),
            mimeByteLength: 128);
    }

    private static EmailOccurrenceId Occurrence() => EmailOccurrenceId.Create(
        Account,
        Inbox.Id,
        ImapUidValidity.Create(7U),
        ImapUid.Create(42U));

    private static MailboxMutationRecord Abandoned(MailboxMutationRequest request) => new()
    {
        Id = MailboxMutationRecordId.Create(Guid.CreateVersion7()),
        Request = request,
        Stage = MailboxMutationStage.Abandoned,
        Placement = RemoteEmailPlacement.NotReported(),
        RequiresSourceRemoval = false,
        IsAudited = false,
        AttemptCount = 5,
        RecordedAt = DecidedAt,
        StageChangedAt = DecidedAt,
        LastFailure = null,
        PlacementObservedAt = null,
        SourceRemovalObservedAt = null,
    };

    private static MailDraftRecord Draft(UserId writer, string subject) => new()
    {
        Id = MailDraftId.Create(Guid.CreateVersion7()),
        AccountId = Account,
        User = writer,
        Author = OutgoingEmailRequester.Command("client-draft-1"),
        Recipients = [],
        Subject = subject,
        MimeByteLength = 128,
        Revision = 1,
        ComposedAt = DecidedAt,
        RevisedAt = DecidedAt,
        DiscardedAt = null,
        PromotedTo = null,
        Copies = [],
        Attachments = [],
        Divergence = null,
        LastFailure = null,
    };
}
