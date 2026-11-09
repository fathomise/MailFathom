// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Emails.Threads;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Retention;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Accounts;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
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
        fixture.Telemetry.Received(1).RecordAccountRun(7, Arg.Any<TimeSpan>());
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
        fixture.Telemetry.Received(1).RecordAccountRun(0, took);
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
        fixture.Telemetry.Received(1).RecordAccountRun(0, Arg.Any<TimeSpan>());
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
                this.Telemetry,
                this.Clock);
        }

        public IMailRetentionAccounts Accounts { get; } = Substitute.For<IMailRetentionAccounts>();

        public IPersistenceSessionFactory Sessions { get; } = Substitute.For<IPersistenceSessionFactory>();

        public IPersistenceSession Session { get; } = Substitute.For<IPersistenceSession>();

        public IEmailThreadStore Threads { get; } = Substitute.For<IEmailThreadStore>();

        public IMailRetentionTelemetry Telemetry { get; } = Substitute.For<IMailRetentionTelemetry>();

        public FakeTimeProvider Clock { get; } = new();

        public MailAccountRetentionHandler Handler { get; }
    }
}
