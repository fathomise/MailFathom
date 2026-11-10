// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Coordination;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Folders;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Mutations;
using MailFathom.Application.Mail.Mutations.Local;
using MailFathom.Application.Mail.Retention;
using MailFathom.Application.Persistence;
using MailFathom.Application.UnitTests.TestDoubles;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Retention;

/// <summary>Covers the bounds and the skip of one account's nightly run.</summary>
public sealed class MailAccountRetentionHandlerTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("0199a0c0-0000-7000-8000-000000000001");

    private static RetainMailAccountJobPayload Run => RetainMailAccountJobPayload.For("walk-of-the-occasion", Account);

    /// <summary>A short batch means nothing else could be taken now, so the run ends rather than asking again.</summary>
    [Fact]
    public async Task RunAsync_ABatchBelowTheLimit_RemovesOnceAndRecordsWhatItRemoved()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.RetentionReader.GetTombstoneRetention(Account).Returns(MailTombstoneRetention.Disabled);
        fixture.Threads.RemoveEmptiedAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(7);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Threads.Received(1).RemoveEmptiedAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            MailAccountRetentionHandler.ConversationsRemovedPerTransaction,
            Arg.Any<CancellationToken>());
        await fixture.Session.Received(2).CommitAsync(Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(0, 0, 7, Arg.Any<TimeSpan>());
    }

    /// <summary>The duration published is the run's own, measured across the removal.</summary>
    [Fact]
    public async Task RunAsync_ARunThatTakesTime_RecordsThatDuration()
    {
        // Arrange
        var fixture = new Fixture();
        var took = TimeSpan.FromSeconds(42);
        fixture.Threads.RemoveEmptiedAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fixture.Clock.Advance(took);

                return Task.FromResult(0);
            });

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        fixture.Telemetry.Received(1).RecordAccountRun(0, 0, 0, took);
    }

    /// <summary>Each batch is a transaction of its own, and what the night's bound leaves waits for the next occasion.</summary>
    [Fact]
    public async Task RunAsync_FullBatchesUpToTheNightsBound_StopsAtTheBound()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.RetentionReader.GetTombstoneRetention(Account).Returns(MailTombstoneRetention.Disabled);
        fixture.Threads.RemoveEmptiedAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<int>(2)));

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        var expectedBatches = MailAccountRetentionHandler.MaximumConversationsRemovedPerNight
            / MailAccountRetentionHandler.ConversationsRemovedPerTransaction;

        await fixture.Threads.Received(expectedBatches)
            .RemoveEmptiedAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<int>(), Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(
            0,
            0,
            MailAccountRetentionHandler.MaximumConversationsRemovedPerNight,
            Arg.Any<TimeSpan>());
    }

    /// <summary>A commit that lost counts nothing, and what it held waits for the next occasion.</summary>
    [Fact]
    public async Task RunAsync_ACommitThatConflicted_StopsWithoutCountingTheBatch()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.RetentionReader.GetTombstoneRetention(Account).Returns(MailTombstoneRetention.Disabled);
        fixture.Threads.RemoveEmptiedAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<int>(2)));
        fixture.Session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.ConcurrencyConflict);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Threads.Received(1)
            .RemoveEmptiedAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<int>(), Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(0, 0, 0, Arg.Any<TimeSpan>());
    }

    /// <summary>What the message step retired reaches the run's published counts.</summary>
    [Fact]
    public async Task RunAsync_AnAccountRetiringMail_PublishesHowManyItRetired()
    {
        // Arrange
        var fixture = new Fixture();
        var email = StoredEmailId.Create(Guid.CreateVersion7());
        var resolution = MailFolderResolution.FirstBindingOf(MailFolderAlias.Create("INBOX"), RemoteFolderPath.Create("INBOX"));
        var occurrence = EmailOccurrenceId.Create(Account, resolution.Id, ImapUidValidity.Create(1), ImapUid.Create(9));
        fixture.MessageRetention.GetMessageRetention(Account)
            .Returns(new MailMessageRetention(IsEnabled: true, TimeSpan.FromDays(30), []));
        fixture.ReturnsCustody(MailAccountCustodyPhase.Mirrored);
        fixture.AgedMail.ReadAsync(
                Arg.Any<IPersistenceSession>(),
                Account,
                Arg.Any<MailMessageRetention>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<StoredEmailId?>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AgedStoredMail>>(
                [new AgedStoredMail(email, MailFolderId.Create(Guid.CreateVersion7()), resolution.Alias, occurrence)]));

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        fixture.Telemetry.Received(1).RecordAccountRun(Arg.Any<int>(), 1, Arg.Any<int>(), Arg.Any<TimeSpan>());
    }

    /// <summary>An account withheld after the walk handed it out is left alone on this replica.</summary>
    [Fact]
    public async Task RunAsync_AnAccountBeingWithheld_RemovesNothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Accounts.IsWithheld(Account).Returns(true);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Sessions.DidNotReceive().BeginSessionAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>An account stating nothing has nothing erased, and its emptied conversations still go.</summary>
    [Fact]
    public async Task RunAsync_TombstoneRetentionSwitchedOff_ErasesNothingAndStillRemovesConversations()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.RetentionReader.GetTombstoneRetention(Account).Returns(MailTombstoneRetention.Disabled);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.AssertNothingErasedAsync();
        await fixture.Threads.Received(1).RemoveEmptiedAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The age is read from the configuration as it stands, so it reaches what was recorded before it was turned on.</summary>
    [Fact]
    public async Task RunAsync_TombstoneRetentionOn_ErasesWhatBecameATombstoneBeforeThatAgeAndRecordsIt()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Mirrored);
        fixture.AnyErasure()
            .Returns(3);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Eraser.Received(1).EraseAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            fixture.Retention,
            fixture.Clock.GetUtcNow() - fixture.Retention.After,
            isSourceDrained: false,
            MailAccountRetentionHandler.TombstonesErasedPerTransaction,
            Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(3, 0, 0, Arg.Any<TimeSpan>());
    }

    /// <summary>A held account's source still gives up what MailFathom erases, through the drain and never from this pass.</summary>
    [Fact]
    public async Task RunAsync_AHeldAccount_ErasesAndSaysItsSourceIsDrained()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Held);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Eraser.Received(1).EraseAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            fixture.Retention,
            Arg.Any<DateTimeOffset>(),
            isSourceDrained: true,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A mailbox being put back loses nothing underneath the restore, and the conversation step is unaffected.</summary>
    [Fact]
    public async Task RunAsync_ARestoringAccount_ErasesNothingAndStillRemovesConversations()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Restoring);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.AssertNothingErasedAsync();
        await fixture.Threads.Received(1).RemoveEmptiedAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An account the database holds no row for has never stored anything to erase.</summary>
    [Fact]
    public async Task RunAsync_AnAccountWithNoCustodyRow_ErasesNothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Custody.ReadAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<CancellationToken>())
            .Returns((MailAccountCustodyState?)null);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.AssertNothingErasedAsync();
    }

    /// <summary>Each batch commits alone, and what the night's bound leaves waits for the next occasion.</summary>
    [Fact]
    public async Task RunAsync_FullBatchesOfTombstones_StopsAtTheNightsBound()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Mirrored);
        fixture.AnyErasure()
            .Returns(call => Task.FromResult(call.ArgAt<int>(5)));

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        var expectedBatches = MailAccountRetentionHandler.MaximumTombstonesErasedPerNight
            / MailAccountRetentionHandler.TombstonesErasedPerTransaction;

        await fixture.Eraser.Received(expectedBatches).EraseAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<MailTombstoneRetention>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(
            MailAccountRetentionHandler.MaximumTombstonesErasedPerNight,
            0,
            0,
            Arg.Any<TimeSpan>());
    }

    /// <summary>A run spans many batches, and an account that starts being restored partway through loses nothing further.</summary>
    [Fact]
    public async Task RunAsync_AnAccountThatStartsRestoringBetweenBatches_ErasesNoFurtherBatch()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Mirrored, MailAccountCustodyPhase.Restoring);
        fixture.AnyErasure()
            .Returns(MailAccountRetentionHandler.TombstonesErasedPerTransaction);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Eraser.Received(1).EraseAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<MailTombstoneRetention>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(
            MailAccountRetentionHandler.TombstonesErasedPerTransaction,
            0,
            0,
            Arg.Any<TimeSpan>());
    }

    /// <summary>An account that starts being held partway through owes the drain a record from the next batch on.</summary>
    [Fact]
    public async Task RunAsync_AnAccountThatBecomesHeldBetweenBatches_DrainsItsSourceFromThenOn()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Mirrored, MailAccountCustodyPhase.Held);
        fixture.AnyErasure()
            .Returns(MailAccountRetentionHandler.TombstonesErasedPerTransaction, 0);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Eraser.Received(1).EraseAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<MailTombstoneRetention>(),
            Arg.Any<DateTimeOffset>(),
            isSourceDrained: false,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        await fixture.Eraser.Received(1).EraseAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<MailTombstoneRetention>(),
            Arg.Any<DateTimeOffset>(),
            isSourceDrained: true,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A tombstone that moved under the batch costs that batch and no other step, and the next occasion repeats it.</summary>
    [Fact]
    public async Task RunAsync_ATombstoneBatchThatConflicted_CountsNothingAndStillRemovesConversations()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Mirrored);
        fixture.AnyErasure()
            .Returns(4);
        fixture.Session.CommitAsync(Arg.Any<CancellationToken>())
            .Returns(PersistenceCommitResult.ConcurrencyConflict, PersistenceCommitResult.Committed);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Threads.Received(1).RemoveEmptiedAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(0, 0, 0, Arg.Any<TimeSpan>());
    }

    /// <summary>A failure reaches the job's own retry and log rather than being swallowed, so the next occasion repeats the pass.</summary>
    [Fact]
    public async Task RunAsync_AnErasureThatFails_LeavesTheFailureToTheJob()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Mirrored);
        fixture.AnyErasure()
            .ThrowsAsync(new InvalidOperationException("The database went away."));

        // Act, Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Handler.RunAsync(
            Run,
            TestContext.Current.CancellationToken));
    }

    /// <summary>A row an older value kept readable is settled under what the account states now, even with tombstone erasure off.</summary>
    [Theory]
    [InlineData(MailAccountCustodyPhase.Mirrored, false)]
    [InlineData(MailAccountCustodyPhase.Held, true)]
    public async Task RunAsync_AnAccountWithKeptRows_SettlesThemUnderItsCurrentValue(MailAccountCustodyPhase phase, bool isSourceDrained)
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(phase);
        fixture.RetentionReader.GetTombstoneRetention(Account).Returns(MailTombstoneRetention.Disabled);
        fixture.DeleteDispositions.GetAuthoredDeleteDisposition(Account).Returns(AuthoredDeleteEmailDisposition.EraseLocalCopy);
        fixture.AnySettlement().Returns(3);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Eraser.Received(1).SettleKeptAfterAuthoredDeleteAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            AuthoredDeleteEmailDisposition.EraseLocalCopy,
            isSourceDrained,
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(3, 0, 0, Arg.Any<TimeSpan>());
    }

    /// <summary>What becomes a tombstone was hidden, not erased, so the run's figure of erased rows does not count it.</summary>
    [Fact]
    public async Task RunAsync_RowsSettledToTombstones_AreNotCountedAsErased()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Mirrored);
        fixture.RetentionReader.GetTombstoneRetention(Account).Returns(MailTombstoneRetention.Disabled);
        fixture.DeleteDispositions.GetAuthoredDeleteDisposition(Account).Returns(AuthoredDeleteEmailDisposition.RetainTombstone);
        fixture.AnySettlement().Returns(3);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        fixture.Telemetry.Received(1).RecordAccountRun(0, 0, 0, Arg.Any<TimeSpan>());
    }

    /// <summary>A restore is writing to the source, so what it might still need is left alone until it ends.</summary>
    [Fact]
    public async Task RunAsync_AnAccountBeingRestored_SettlesNothing()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.ReturnsCustody(MailAccountCustodyPhase.Restoring);
        fixture.RetentionReader.GetTombstoneRetention(Account).Returns(MailTombstoneRetention.Disabled);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.AnySettlementReceived(0);
    }

    [Fact]
    public async Task RunAsync_APayloadOfAnotherJob_IsRefused()
    {
        // Arrange
        var fixture = new Fixture();

        // Act, Assert
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Handler.RunAsync(
            WalkMailRetentionJobPayload.FromTheStart("walk"),
            TestContext.Current.CancellationToken));
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            this.Sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(this.Session);
            this.Session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
            this.Handler = new MailAccountRetentionHandler(
                this.Accounts,
                this.Sessions,
                this.Threads,
                this.RetentionReader,
                this.Custody,
                this.Eraser,
                this.DeleteDispositions,
                new MailMessageRetirement(
                    this.MessageRetention,
                    this.Sessions,
                    this.Custody,
                    this.AgedMail,
                    this.DeleteDispositions,
                    Substitute.For<IMailFolderMappingReader>(),
                    MailboxChangeSubmissions.Over(new InMemoryMailboxMutationRecordStore()),
                    Substitute.For<ILocalEmailStateStore>(),
                    this.Leases,
                    this.Clock),
                this.Telemetry,
                this.Clock);
            this.RetentionReader.GetTombstoneRetention(Account).Returns(this.Retention);
            this.MessageRetention.GetMessageRetention(Account).Returns(MailMessageRetention.Disabled);
            this.Leases.ReadEveryHeldAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<IReadOnlyList<WorkLease>>([]));
        }

        public IMailRetentionAccounts Accounts { get; } = Substitute.For<IMailRetentionAccounts>();

        public IPersistenceSessionFactory Sessions { get; } = Substitute.For<IPersistenceSessionFactory>();

        public IPersistenceSession Session { get; } = Substitute.For<IPersistenceSession>();

        public IEmailThreadStore Threads { get; } = Substitute.For<IEmailThreadStore>();

        public IMailRetentionTelemetry Telemetry { get; } = Substitute.For<IMailRetentionTelemetry>();

        public FakeTimeProvider Clock { get; } = new();

        public MailAccountRetentionHandler Handler { get; }

        public IMailTombstoneRetentionReader RetentionReader { get; } = Substitute.For<IMailTombstoneRetentionReader>();

        public IMailMessageRetentionReader MessageRetention { get; } = Substitute.For<IMailMessageRetentionReader>();

        public IAgedStoredMailReader AgedMail { get; } = Substitute.For<IAgedStoredMailReader>();

        public IWorkLeaseStore Leases { get; } = Substitute.For<IWorkLeaseStore>();

        public IMailAccountCustodyStore Custody { get; } = Substitute.For<IMailAccountCustodyStore>();

        public IStoredTombstoneEraser Eraser { get; } = Substitute.For<IStoredTombstoneEraser>();

        public IAuthoredDeleteEmailDispositionReader DeleteDispositions { get; } = Substitute.For<IAuthoredDeleteEmailDispositionReader>();

        public MailTombstoneRetention Retention { get; } = new(IsEnabled: true, TimeSpan.FromDays(30), ["ARCHIVE"]);

        public void ReturnsCustody(MailAccountCustodyPhase phase, params MailAccountCustodyPhase[] thereafter) =>
            this.Custody.ReadAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<CancellationToken>())
                .Returns(StateIn(phase), [.. thereafter.Select(StateIn)]);

        public Task<int> AnyErasure() => this.Eraser.EraseAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<MailTombstoneRetention>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        public Task<int> AssertNothingErasedAsync() => this.Eraser.DidNotReceive().EraseAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<MailTombstoneRetention>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        public Task<int> AnySettlement() => this.Eraser.SettleKeptAfterAuthoredDeleteAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<AuthoredDeleteEmailDisposition>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        public Task<int> AnySettlementReceived(int times) => this.Eraser.Received(times).SettleKeptAfterAuthoredDeleteAsync(
            Arg.Any<IPersistenceSession>(),
            Account,
            Arg.Any<AuthoredDeleteEmailDisposition>(),
            Arg.Any<bool>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());

        private static MailAccountCustodyState StateIn(MailAccountCustodyPhase phase) =>
            new(MailAccountCustody.MirrorSource, phase);
    }
}
