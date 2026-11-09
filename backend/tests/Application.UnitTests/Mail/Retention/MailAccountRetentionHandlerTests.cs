// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Accounts.Custody;
using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Retention;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
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
        await fixture.Session.Received(1).CommitAsync(Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(0, 7, Arg.Any<TimeSpan>());
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
        fixture.Telemetry.Received(1).RecordAccountRun(0, 0, took);
    }

    /// <summary>Each batch is a transaction of its own, and what the night's bound leaves waits for the next occasion.</summary>
    [Fact]
    public async Task RunAsync_FullBatchesUpToTheNightsBound_StopsAtTheBound()
    {
        // Arrange
        var fixture = new Fixture();
        fixture.Threads.RemoveEmptiedAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<int>(2)));

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        var expectedBatches = MailAccountRetentionHandler.MaximumConversationsRemovedPerNight
            / MailAccountRetentionHandler.ConversationsRemovedPerTransaction;

        await fixture.Sessions.Received(expectedBatches).BeginSessionAsync(Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(
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
        fixture.Threads.RemoveEmptiedAsync(Arg.Any<IPersistenceSession>(), Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(call.ArgAt<int>(2)));
        fixture.Session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.ConcurrencyConflict);

        // Act
        await fixture.Handler.RunAsync(Run, TestContext.Current.CancellationToken);

        // Assert
        await fixture.Sessions.Received(1).BeginSessionAsync(Arg.Any<CancellationToken>());
        fixture.Telemetry.Received(1).RecordAccountRun(0, 0, Arg.Any<TimeSpan>());
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
        fixture.Telemetry.Received(1).RecordAccountRun(3, 0, Arg.Any<TimeSpan>());
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
        fixture.Custody.ReadAsync(Account, Arg.Any<CancellationToken>()).Returns((MailAccountCustodyState?)null);

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
            Arg.Any<TimeSpan>());
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
        fixture.Telemetry.Received(1).RecordAccountRun(0, 0, Arg.Any<TimeSpan>());
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
                this.Telemetry,
                this.Clock);
            this.RetentionReader.GetTombstoneRetention(Account).Returns(this.Retention);
        }

        public IMailRetentionAccounts Accounts { get; } = Substitute.For<IMailRetentionAccounts>();

        public IPersistenceSessionFactory Sessions { get; } = Substitute.For<IPersistenceSessionFactory>();

        public IPersistenceSession Session { get; } = Substitute.For<IPersistenceSession>();

        public IEmailThreadStore Threads { get; } = Substitute.For<IEmailThreadStore>();

        public IMailRetentionTelemetry Telemetry { get; } = Substitute.For<IMailRetentionTelemetry>();

        public FakeTimeProvider Clock { get; } = new();

        public MailAccountRetentionHandler Handler { get; }

        public IMailTombstoneRetentionReader RetentionReader { get; } = Substitute.For<IMailTombstoneRetentionReader>();

        public IMailAccountCustodyStore Custody { get; } = Substitute.For<IMailAccountCustodyStore>();

        public IStoredTombstoneEraser Eraser { get; } = Substitute.For<IStoredTombstoneEraser>();

        public MailTombstoneRetention Retention { get; } = new(IsEnabled: true, TimeSpan.FromDays(30), ["ARCHIVE"]);

        public void ReturnsCustody(MailAccountCustodyPhase phase) =>
            this.Custody.ReadAsync(Account, Arg.Any<CancellationToken>())
                .Returns(new MailAccountCustodyState(MailAccountCustody.MirrorSource, phase));

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
    }
}
