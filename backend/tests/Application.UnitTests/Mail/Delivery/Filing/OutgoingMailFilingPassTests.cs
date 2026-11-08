// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Security.Cryptography;
using System.Text;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Mail.Delivery.Outbox;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Persistence;
using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Delivery;
using MailFathom.Domain.Delivery.Filing;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Filing;

/// <summary>Covers what a pass puts into the mailbox's own folders, and what it deliberately does not.</summary>
public sealed class OutgoingMailFilingPassTests
{
    private static readonly MailAccountId Account =
        MailAccountId.Create("work");

    private static readonly DateTimeOffset RanAt = new(2026, 8, 19, 9, 0, 0, TimeSpan.Zero);

    /// <summary>The message as the outbox stored it, carrying the organization's signature, which the filed copy has to keep.</summary>
    private static readonly byte[] StoredMime = Encoding.ASCII.GetBytes(
        "MailFathom-Organization-Signature: v=1; a=ed25519-sha256; b=AAAA\r\n"
        + "Message-ID: <mint-1@mailfathom.invalid>\r\nSubject: synthetic\r\n\r\nbody\r\n");

    private static readonly ReadOnlyMemory<byte> RawMime = StoredMime;

    /// <summary>An account that asked for no sent copy gets none, and nothing reaches its mail server over it.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AnAccountThatFilesNoSentCopy_AppendsNothing()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(OutgoingMailFilingOutcome.NotRequested, result.Outcome);
        Assert.Empty(context.Filing.Filings.Read(delivered));
        Assert.Equal([delivered], context.Released);
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A held account's sent copy is filed locally by its delivery, so settling the send appends nothing to the drained source.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AHeldAccount_AppendsNothingToTheSource()
    {
        // Arrange
        var context = new FilingContext(holdsAccount: true);
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        Assert.Empty(context.Filing.Filings.Read(delivered));
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A delivered message the account files reaches the sent folder as read, from the stored bytes.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ADeliveredMessage_AppendsTheStoredMimeToTheSentFolderAsSeen()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.AppendAnswer = new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(42), ImapUid.Create(7)),
            "mint-1@mailfathom.invalid");
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(OutgoingMailFilingOutcome.Filed, result.Outcome);

        await context.Filing.WriteSession.Received(1).AppendAsync(
            Arg.Is<ReadOnlyMemory<byte>>(mime => mime.ToArray().SequenceEqual(StoredMime)),
            AppendedMailFlags.Seen,
            RanAt,
            Arg.Any<CancellationToken>());

        var filed = Assert.Single(context.Filing.Filings.Read(delivered));
        Assert.Equal(OutgoingMailFiling.Sent, filed.Filing);
        Assert.Equal(OutgoingMailFilingStage.Confirmed, filed.Stage);
        Assert.Equal(ImapUid.Create(7), filed.Placement.Uid);
        Assert.Equal("mint-1@mailfathom.invalid", filed.InternetMessageId);
    }

    /// <summary>The attempt that ends a sent copy carries how long after the delivery it ended, which is the wait measured.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ACopyFiledAfterTheDelivery_CarriesTheWaitSinceTheDeliveryWasAcknowledged()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var delivered = await context.DeliverAsync();
        context.Advance(TimeSpan.FromSeconds(90));

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(OutgoingMailFilingOutcome.Filed, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(90), result.WaitedAfterDelivery);
    }

    /// <summary>A copy already filed is answered from the record, so asking again measures no second wait for it.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AskedAgainAfterTheCopyWasFiled_CarriesNoSecondWait()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.FileSentCopyAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(OutgoingMailFilingOutcome.AlreadyFiled, result.Outcome);
        Assert.Null(result.WaitedAfterDelivery);
    }

    /// <summary>An account that files no sent copy has no copy waiting, so there is no wait to measure.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AnAccountThatFilesNoSentCopy_CarriesNoWait()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(Assert.Single(results).WaitedAfterDelivery);
    }

    /// <summary>A restoring account files its sent copy on the source as a mirrored one does, from the same stored, signed bytes.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ARestoringAccount_AppendsTheStoredMimeToTheSentFolder()
    {
        // Arrange
        var context = new FilingContext(holdsAccount: true);
        context.Held!.Folders.Phase = MailAccountCustodyPhase.Restoring;
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.AppendAnswer = new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(42), ImapUid.Create(7)),
            "mint-1@mailfathom.invalid");
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.Filed, Assert.Single(results).Outcome);
        await context.Filing.WriteSession.Received(1).AppendAsync(
            Arg.Is<ReadOnlyMemory<byte>>(mime => mime.ToArray().SequenceEqual(StoredMime)),
            AppendedMailFlags.Seen,
            RanAt,
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The one ending nothing repeats. The server took the append and never said so, and a second one would put a
    /// second copy of the user's message in their sent folder with nothing afterwards telling the two apart.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AnAppendTheServerNeverAnswered_IsNeverAppendedAgain()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.WriteSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new MailboxUnavailableException(
                Account,
                MailFolderAlias.Create("sent"),
                new TimeoutException("The append was issued and the server never answered.")));
        var delivered = await context.DeliverAsync();

        // Act
        var first = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);
        var second = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.OutcomeUnknown, Assert.Single(first).Outcome);
        Assert.Equal(OutgoingMailFilingOutcome.OutcomeUnknown, Assert.Single(second).Outcome);

        await context.Filing.WriteSession.Received(1).AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A copy that could not be filed leaves the delivery exactly as delivered. The record says the message was sent,
    /// says why the copy is not in the folder, and nothing offers the message to anybody again.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AFailedAppendAfterADelivery_LeavesTheSendDeliveredAndNotFiled()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.FileSentCopies(Account);
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        var result = Assert.Single(results);
        Assert.Equal(OutgoingMailFilingOutcome.DestinationUnavailable, result.Outcome);

        var record = context.Store.Read(delivered);
        Assert.Equal(OutgoingEmailStage.Sent, record.Stage);
        Assert.Null(record.LastFailure);
        Assert.Equal(MailFathomErrorCode.OutgoingEmailFilingDestinationUnavailable, record.LastFilingFailure);
        Assert.Empty(record.Filings);
        Assert.Equal(1, record.AttemptCount);
        Assert.Equal([delivered], context.Released);
    }

    /// <summary>
    /// A copy that failed is settled too, because nothing files a delivered send's copy again: the pass is asked once,
    /// right after the delivery, so bytes kept for a second attempt would be kept for one that never comes.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AFilingThatFailed_ReleasesTheStoredMime()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var delivered = await context.DeliverAsync();
        context.Content
            .FindOutgoingContentAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
            .Returns((StoredEmailContent?)null);

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.Failed, Assert.Single(results).Outcome);
        Assert.Equal([delivered], context.Released);
    }

    /// <summary>
    /// A host that stopped says nothing about the copy. The filing never reached the mail server, so the shutdown is
    /// raised for the caller that already tells one from a failure rather than written onto the record as one — which
    /// would leave a delivered send permanently recorded as one whose copy could not be filed.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_TheHostStopsBeforeTheAppendIsIssued_RecordsNoFilingFailure()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var delivered = await context.DeliverAsync();

        using var shutdown = new CancellationTokenSource();
        context.Content
            .FindOutgoingContentAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
            .Returns<StoredEmailContent?>(_ =>
            {
                shutdown.Cancel();

                throw new OperationCanceledException(shutdown.Token);
            });

        // Act
        var stopped = await Assert.ThrowsAsync<OperationCanceledException>(
            () => context.Filing.Pass.SettleFiledCopiesAsync(delivered, shutdown.Token));

        // Assert
        Assert.Equal(shutdown.Token, stopped.CancellationToken);

        var record = context.Store.Read(delivered);
        Assert.Equal(OutgoingEmailStage.Sent, record.Stage);
        Assert.Null(record.LastFilingFailure);
        Assert.Empty(record.Filings);
        Assert.Empty(context.Released);
        await context.Filing.WriteSession.DidNotReceiveWithAnyArgs().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A send nothing has settled yet has nothing to say about its copies.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ASendThatIsStillQueued_DoesNothing()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var queued = await context.EnqueueAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            queued,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        Assert.Empty(context.Released);
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A message that reached nobody is not a message to say <em>sent</em> about in the user's own folder.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ARefusedSend_FilesNoSentCopy()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var refused = await context.EnqueueAsync();
        await context.SettleAsync(refused, OutgoingEmailStage.Refused);

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            refused,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        Assert.Empty(context.Released);
        await context.Filing.WriteSession.DidNotReceive().AppendAsync(
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<AppendedMailFlags>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// A mirrored send's stored message is released once the server has confirmed its sent copy, and not before: the
    /// append is the last thing that reads the bytes.
    /// </summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ADeliveredMessageWhoseCopyIsConfirmed_ReleasesItsStoredMimeAfterTheAppend()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.AppendAnswer = new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(42), ImapUid.Create(7)),
            "mint-1@mailfathom.invalid");
        var delivered = await context.DeliverAsync();

        // Act
        await context.Filing.Pass.SettleFiledCopiesAsync(delivered, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([delivered], context.Released);
        Assert.Equal([OutgoingMailFilingStage.Confirmed], context.FilingStagesAtRelease);
    }

    /// <summary>A held account's sent copy was prepared from the stored bytes before its delivery committed, so nothing reads them afterwards.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AHeldAccount_ReleasesTheStoredMimeItsLocalCopyWasPreparedFrom()
    {
        // Arrange
        var context = new FilingContext(holdsAccount: true);
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        var delivered = await context.DeliverAsync();

        // Act
        await context.Filing.Pass.SettleFiledCopiesAsync(delivered, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([delivered], context.Released);
    }

    /// <summary>A restoring account files on the source as a mirrored one does, so its stored message is released after the same confirmed append.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_ARestoringAccount_ReleasesItsStoredMimeAfterTheAppend()
    {
        // Arrange
        var context = new FilingContext(holdsAccount: true);
        context.Held!.Folders.Phase = MailAccountCustodyPhase.Restoring;
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.AppendAnswer = new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(42), ImapUid.Create(7)),
            "mint-1@mailfathom.invalid");
        var delivered = await context.DeliverAsync();

        // Act
        await context.Filing.Pass.SettleFiledCopiesAsync(delivered, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([delivered], context.Released);
        Assert.Equal([OutgoingMailFilingStage.Confirmed], context.FilingStagesAtRelease);
    }

    /// <summary>An append the server never answered is never issued again, so it is as settled as a confirmed one and needs the bytes no more.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AnAppendTheServerNeverAnswered_ReleasesTheStoredMime()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.WriteSession
            .AppendAsync(
                Arg.Any<ReadOnlyMemory<byte>>(),
                Arg.Any<AppendedMailFlags>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new MailboxUnavailableException(
                Account,
                MailFolderAlias.Create("sent"),
                new TimeoutException("The append was issued and the server never answered.")));
        var delivered = await context.DeliverAsync();

        // Act
        await context.Filing.Pass.SettleFiledCopiesAsync(delivered, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([delivered], context.Released);
        Assert.Equal([OutgoingMailFilingStage.Issued], context.FilingStagesAtRelease);
    }

    /// <summary>A release that could not be committed leaves the payload for the sweep and changes nothing the filing reports.</summary>
    [Fact]
    public async Task SettleFiledCopiesAsync_AReleaseThatFails_StillReportsTheFiledCopy()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        context.Filing.FileSentCopies(Account);
        context.Filing.AppendAnswer = new AppendedMailCopy(
            RemoteEmailPlacement.Reported(ImapUidValidity.Create(42), ImapUid.Create(7)),
            "mint-1@mailfathom.invalid");
        context.Content
            .ReleaseDeliveredOutgoingContentAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<IReadOnlyCollection<OutgoingEmailId>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The database refused the release."));
        var delivered = await context.DeliverAsync();

        // Act
        var results = await context.Filing.Pass.SettleFiledCopiesAsync(
            delivered,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.Filed, Assert.Single(results).Outcome);
    }

    /// <summary>The provider filed its own copy beside MailFathom's, so the provider's goes and the one MailFathom stored and appended stays.</summary>
    [Fact]
    public async Task RemoveProviderSentCopiesAsync_AProviderCopyRecordedBesideTheAppendedOne_RemovesItAndSettlesTheFiling()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.FileSentCopyAsync();
        await context.RecordProviderCopyAsync(delivered);
        context.Filing.WriteSession
            .RemoveProviderSentCopyAsync(ImapUidValidity.Create(42), ImapUid.Create(8), "mint-1@mailfathom.invalid", Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var results = await context.Filing.Pass.RemoveProviderSentCopiesAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.ProviderCopyRemoved, Assert.Single(results).Outcome);
        await context.Filing.WriteSession.DidNotReceiveWithAnyArgs().WithdrawAppendedAsync(
            Arg.Any<ImapUidValidity>(),
            Arg.Any<ImapUid>(),
            Arg.Any<CancellationToken>());

        var filed = Assert.Single(context.Filing.Filings.Read(delivered));
        Assert.Equal(OutgoingMailFilingStage.Confirmed, filed.Stage);
        Assert.False(filed.AwaitsProviderCopyRemoval);
    }

    /// <summary>The message at the recorded UID is no longer the provider's copy of the send, so it is kept rather than removed.</summary>
    [Fact]
    public async Task RemoveProviderSentCopiesAsync_AMessageTheServerNoLongerReportsAsTheCopy_IsKept()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.FileSentCopyAsync();
        await context.RecordProviderCopyAsync(delivered);
        context.Filing.WriteSession
            .RemoveProviderSentCopyAsync(Arg.Any<ImapUidValidity>(), Arg.Any<ImapUid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var results = await context.Filing.Pass.RemoveProviderSentCopiesAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.ProviderCopyKept, Assert.Single(results).Outcome);
        Assert.False(Assert.Single(context.Filing.Filings.Read(delivered)).AwaitsProviderCopyRemoval);
    }

    /// <summary>
    /// A removal the server refuses in a way it would repeat — the folder renumbered, or no message-scoped expunge — settles
    /// the row with the copy kept, so no later pass asks again.
    /// </summary>
    /// <param name="refusal">Which refusal the server answers with.</param>
    [Theory]
    [InlineData(nameof(MailboxFolderRecreatedException))]
    [InlineData(nameof(MailboxMutationUnsupportedException))]
    public async Task RemoveProviderSentCopiesAsync_ARemovalTheServerRefuses_KeepsTheCopyAndSettlesTheFiling(string refusal)
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.FileSentCopyAsync();
        await context.RecordProviderCopyAsync(delivered);
        var sent = MailFolderAlias.Create("sent");
        Exception refused = refusal == nameof(MailboxFolderRecreatedException)
            ? new MailboxFolderRecreatedException(Account, sent, ImapUidValidity.Create(42), ImapUidValidity.Create(43))
            : new MailboxMutationUnsupportedException(Account, sent, "provider copy removal", "UIDPLUS");
        context.Filing.WriteSession
            .RemoveProviderSentCopyAsync(Arg.Any<ImapUidValidity>(), Arg.Any<ImapUid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(refused);

        // Act
        var results = await context.Filing.Pass.RemoveProviderSentCopiesAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.ProviderCopyKept, Assert.Single(results).Outcome);
        var settled = Assert.Single(context.Filing.Filings.Read(delivered));
        Assert.False(settled.AwaitsProviderCopyRemoval);
        Assert.NotNull(settled.ProviderCopySettledAt);
    }

    /// <summary>A removal the server could not serve is left owed, so the next pass asks again rather than giving up on one failure.</summary>
    [Fact]
    public async Task RemoveProviderSentCopiesAsync_ARemovalTheServerCouldNotServe_IsLeftOwed()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.FileSentCopyAsync();
        await context.RecordProviderCopyAsync(delivered);
        context.Filing.WriteSession
            .RemoveProviderSentCopyAsync(Arg.Any<ImapUidValidity>(), Arg.Any<ImapUid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("The mail server closed the connection."));

        // Act
        var results = await context.Filing.Pass.RemoveProviderSentCopiesAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.Failed, Assert.Single(results).Outcome);
        Assert.True(Assert.Single(context.Filing.Filings.Read(delivered)).AwaitsProviderCopyRemoval);
    }

    /// <summary>
    /// A removal still owed long after the append is given up without asking the server again: by then the copy has stood
    /// beside the person's own long enough for another client to have acted on it.
    /// </summary>
    [Fact]
    public async Task RemoveProviderSentCopiesAsync_ARemovalStillOwedPastTheDeadline_KeepsTheCopyWithoutAskingTheServer()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.FileSentCopyAsync();
        await context.RecordProviderCopyAsync(delivered);
        context.Advance(TimeSpan.FromMinutes(31));

        // Act
        var results = await context.Filing.Pass.RemoveProviderSentCopiesAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(OutgoingMailFilingOutcome.ProviderCopyKept, Assert.Single(results).Outcome);
        Assert.False(Assert.Single(context.Filing.Filings.Read(delivered)).AwaitsProviderCopyRemoval);
        await context.Filing.WriteSession.DidNotReceiveWithAnyArgs().RemoveProviderSentCopyAsync(
            Arg.Any<ImapUidValidity>(),
            Arg.Any<ImapUid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account whose sends nobody recorded a provider copy of asks its mail server nothing.</summary>
    [Fact]
    public async Task RemoveProviderSentCopiesAsync_NoProviderCopyRecorded_AsksTheServerNothing()
    {
        // Arrange
        var context = new FilingContext();
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        await context.FileSentCopyAsync();

        // Act
        var results = await context.Filing.Pass.RemoveProviderSentCopiesAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        await context.Filing.WriteSession.DidNotReceiveWithAnyArgs().RemoveProviderSentCopyAsync(
            Arg.Any<ImapUidValidity>(),
            Arg.Any<ImapUid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A held account's source is drained, so a provider copy recorded while it was mirrored is the drain's and nothing is removed here.</summary>
    [Fact]
    public async Task RemoveProviderSentCopiesAsync_AHeldAccount_RemovesNothingFromTheSource()
    {
        // Arrange
        var context = new FilingContext(holdsAccount: true);
        context.Held!.Folders.Phase = MailAccountCustodyPhase.Mirrored;
        context.Filing.Map(Account, MailFolderSpecialUse.Sent, "sent", "INBOX.Sent");
        var delivered = await context.FileSentCopyAsync();
        await context.RecordProviderCopyAsync(delivered);
        context.Held.Folders.Phase = MailAccountCustodyPhase.Held;

        // Act
        var results = await context.Filing.Pass.RemoveProviderSentCopiesAsync(
            Account,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(results);
        await context.Filing.WriteSession.DidNotReceiveWithAnyArgs().RemoveProviderSentCopyAsync(
            Arg.Any<ImapUidValidity>(),
            Arg.Any<ImapUid>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Arranges an outbox whose sends a test settles by hand, beside the filing the pass performs.</summary>
    private sealed class FilingContext
    {
        private readonly FakeTimeProvider clock = new(RanAt);

        internal FilingContext(bool holdsAccount = false)
        {
            this.Store = new InMemoryOutgoingEmailStore(timeProvider: this.clock);

            this.Content = ContentStores.Substituted();
            this.Content
                .FindOutgoingContentAsync(Arg.Any<OutgoingEmailId>(), Arg.Any<CancellationToken>())
                .Returns(new StoredEmailContent(RawMime, RawMime.Length, SHA256.HashData(RawMime.Span)));
            this.Content
                .ReleaseDeliveredOutgoingContentAsync(
                    Arg.Any<IPersistenceSession>(),
                    Arg.Any<IReadOnlyCollection<OutgoingEmailId>>(),
                    Arg.Any<CancellationToken>())
                .Returns(call => this.RecordRelease(call.ArgAt<IReadOnlyCollection<OutgoingEmailId>>(1)));

            var settings = MailOutboxSettings.Create(
                maxDeliveriesPerPass: 10,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromMinutes(7),
                maxAttempts: 5,
                TimeSpan.FromMinutes(1),
                TimeSpan.FromHours(1),
                TimeSpan.FromHours(8));

            this.Held = holdsAccount ? new HeldLocalMailbox(Account, this.clock) : null;

            this.Filing = new OutgoingMailFilingHarness(
                this.Store,
                this.Content,
                settings,
                this.clock,
                this.Held);
        }

        /// <summary>Gets the account's local mailbox where the context was built holding it, whose phase a test may move.</summary>
        internal HeldLocalMailbox? Held { get; }

        internal InMemoryOutgoingEmailStore Store { get; }

        /// <summary>Gets the store the appended bytes come from, which is the last step before anything is written down.</summary>
        internal IEmailContentStore Content { get; }

        internal OutgoingMailFilingHarness Filing { get; }

        /// <summary>Gets every send whose stored message the pass asked to release, in the order it asked.</summary>
        internal List<OutgoingEmailId> Released { get; } = [];

        /// <summary>Gets the stage of each filed copy of a released send at the moment its release was asked for.</summary>
        internal List<OutgoingMailFilingStage> FilingStagesAtRelease { get; } = [];

        /// <summary>Moves the clock on, which is how a send that was waiting becomes one a claim may take.</summary>
        internal void Advance(TimeSpan delay) => this.clock.Advance(delay);

        /// <summary>Writes down one send, optionally one that is waiting for an instant still ahead.</summary>
        internal async Task<OutgoingEmailId> EnqueueAsync(TimeSpan? availableIn = null)
        {
            Assert.True(EmailAddress.TryCreate(displayName: null, "anna@example.test", out var recipient));

            var request = OutgoingEmailRequest.Create(
                Account,
                SyntheticUser.Deployment,
                OutgoingEmailRequester.Command($"mfctl-{Guid.CreateVersion7()}"),
                [OutgoingRecipient.Create(recipient, OutgoingRecipientRole.To)]);

            var queued = this.Store.Publish(request, RawMime.Length).Id;

            if (availableIn is { } delay)
            {
                await this.DeferAsync(queued, this.clock.GetUtcNow() + delay);
            }

            return queued;
        }

        /// <summary>Carries one send through delivery and files the sent copy the account asked for.</summary>
        /// <param name="copy">What the server answered the append with, which defaults to one naming the placement.</param>
        /// <returns>The send, with its sent copy already filed.</returns>
        internal async Task<OutgoingEmailId> FileSentCopyAsync(AppendedMailCopy? copy = null)
        {
            this.Filing.FileSentCopies(Account);

            this.Filing.AppendAnswer = copy ?? new AppendedMailCopy(
                RemoteEmailPlacement.Reported(ImapUidValidity.Create(42), ImapUid.Create(7)),
                "mint-1@mailfathom.invalid");

            var delivered = await this.DeliverAsync();
            await this.Filing.Pass.SettleFiledCopiesAsync(delivered, TestContext.Current.CancellationToken);

            return delivered;
        }

        /// <summary>Records what synchronization records on meeting the provider's copy of a filed send at the next UID.</summary>
        internal Task RecordProviderCopyAsync(OutgoingEmailId delivered) =>
            this.Filing.Filings.RecordProviderCopyAsync(
                Substitute.For<IPersistenceSession>(),
                delivered,
                OutgoingMailFiling.Sent,
                ImapUidValidity.Create(42),
                ImapUid.Create(8),
                TestContext.Current.CancellationToken);

        /// <summary>Writes down one send that a submission server has already accepted.</summary>
        internal async Task<OutgoingEmailId> DeliverAsync()
        {
            var queued = await this.EnqueueAsync();
            await this.MarkDeliveredAsync(queued);

            return queued;
        }

        /// <summary>Carries one send through a claim and a transmission the server accepted.</summary>
        internal Task MarkDeliveredAsync(OutgoingEmailId outgoingEmailId) =>
            this.SettleAsync(outgoingEmailId, OutgoingEmailStage.Sent);

        /// <summary>Carries one send to a terminal stage, through the transitions the store permits.</summary>
        internal async Task SettleAsync(OutgoingEmailId outgoingEmailId, OutgoingEmailStage stage)
        {
            var lease = await this.ClaimAsync(outgoingEmailId);
            var session = Substitute.For<IPersistenceSession>();

            if (stage == OutgoingEmailStage.Sent)
            {
                await this.Store.RecordTransmissionBegunAsync(
                    session,
                    lease,
                    outgoingEmailId,
                    deliveryNotificationCarried: null,
                    transmittedAt: DateTimeOffset.UnixEpoch,
                    TestContext.Current.CancellationToken);
            }

            await this.Store.AdvanceAsync(
                session,
                lease,
                outgoingEmailId,
                stage,
                replyCode: 250,
                TestContext.Current.CancellationToken);
        }

        private int RecordRelease(IReadOnlyCollection<OutgoingEmailId> released)
        {
            this.Released.AddRange(released);
            this.FilingStagesAtRelease.AddRange(released
                .SelectMany(this.Filing.Filings.Read)
                .Select(filing => filing.Stage));

            return released.Count;
        }

        private async Task DeferAsync(OutgoingEmailId outgoingEmailId, DateTimeOffset availableAt)
        {
            var lease = await this.ClaimAsync(outgoingEmailId);

            await this.Store.DeferAsync(
                Substitute.For<IPersistenceSession>(),
                lease,
                outgoingEmailId,
                availableAt,
                failure: null,
                TestContext.Current.CancellationToken);
        }

        private async Task<OutgoingEmailLease> ClaimAsync(OutgoingEmailId outgoingEmailId)
        {
            var claimed = await this.Store.ClaimAsync(
                OutgoingEmailClaimRequest.Create(Account, batchSize: 10, TimeSpan.FromMinutes(10)),
                TestContext.Current.CancellationToken);

            return claimed.Single(send => send.Record.Id == outgoingEmailId).Lease;
        }
    }
}
