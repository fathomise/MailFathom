// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Synchronization.Administration;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace MailFathom.Application.UnitTests.Synchronization.Administration;

/// <summary>Covers what the ledger reports about a process's synchronization runs.</summary>
public sealed class MailSynchronizationRunLedgerTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 15, 10, 0, 0, TimeSpan.Zero);
    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailFolderIdentity Inbox = new(Work, MailFolderAlias.Create("inbox"));

    /// <summary>An account nothing has recorded is a supported reading rather than an absence a caller has to interpret.</summary>
    [Fact]
    public void ReadAccount_WithoutAnyRun_ReportsTheAccountAsNotStarted()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));

        // Act
        var state = ledger.ReadAccount(Work);

        // Assert
        Assert.Equal(MailAccountRunState.NotStarted, state);
    }

    /// <summary>The three working phases, each reported as the supervisor reaches it.</summary>
    [Fact]
    public void RecordRun_ThroughOneCycle_ReportsThePhaseTheSupervisorIsIn()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));

        // Act
        ledger.RecordRunQueued(Work);
        var queued = ledger.ReadAccount(Work).Phase;
        ledger.RecordRunStarted(Work);
        var running = ledger.ReadAccount(Work).Phase;
        ledger.RecordNextRunDue(Work, TimeSpan.FromMinutes(5), consecutiveFailureCount: 0);
        var waiting = ledger.ReadAccount(Work).Phase;

        // Assert
        Assert.Equal(
            [MailAccountRunPhase.WaitingForRunSlot, MailAccountRunPhase.Running, MailAccountRunPhase.WaitingForNextRun],
            new[] { queued, running, waiting });
    }

    /// <summary>The instant is what a status surface reads, so the delay is anchored to the ledger's own clock rather than left to whoever asks.</summary>
    [Fact]
    public void RecordNextRunDue_ReportsTheInstantTheWaitEndsAndTheFailuresItGrewFrom()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));

        // Act
        ledger.RecordNextRunDue(Work, TimeSpan.FromMinutes(20), consecutiveFailureCount: 3);

        // Assert
        var state = ledger.ReadAccount(Work);
        Assert.Equal(Start + TimeSpan.FromMinutes(20), state.NextRunDueAt);
        Assert.Equal(3, state.ConsecutiveFailureCount);
    }

    /// <summary>A run that finished stays readable across the wait that follows it, which is what "how did the last one end" means.</summary>
    [Fact]
    public void RecordRunEnded_KeepsTheReportWhileTheAccountWaitsForItsNextRun()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));

        // Act
        ledger.RecordRunEnded(Work, scheduledFolderCount: 4, failedFolderCount: 1, mutationConvergenceFailed: false);
        ledger.RecordNextRunDue(Work, TimeSpan.FromMinutes(10), consecutiveFailureCount: 1);

        // Assert
        var lastRun = ledger.ReadAccount(Work).LastRun;
        Assert.NotNull(lastRun);
        Assert.Equal(new MailAccountRunReport(Start, 4, 1, MutationConvergenceFailed: false) { FailedSince = Start }, lastRun);
        Assert.True(lastRun.Failed);
    }

    /// <summary>A convergence pass that failed fails the run on its own, which no folder count would show.</summary>
    [Fact]
    public void RecordRunEnded_WithFailedConvergenceAndNoFailedFolder_ReportsTheRunAsFailed()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));

        // Act
        ledger.RecordRunEnded(Work, scheduledFolderCount: 2, failedFolderCount: 0, mutationConvergenceFailed: true);

        // Assert
        Assert.True(ledger.ReadAccount(Work).LastRun?.Failed);
    }

    /// <summary>A folder no run of this process has taken a turn for is absent rather than reported empty.</summary>
    [Fact]
    public void ReadFolder_WithoutAnyTurn_ReportsNothing()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));

        // Act
        var report = ledger.ReadFolder(Inbox);

        // Assert
        Assert.Null(report);
    }

    /// <summary>What a folder stored, skipped, and left outstanding, stamped with the ledger's clock.</summary>
    [Fact]
    public void RecordFolderSynchronized_ReportsWhatTheTurnStoredAndWhenItEnded()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var ledger = new MailSynchronizationRunLedger(clock);

        // Act
        clock.Advance(TimeSpan.FromSeconds(30));
        ledger.RecordFolderSynchronized(
            Inbox,
            storedEmailCount: 40,
            skippedOversizedEmailCount: 2,
            unreadableMimeEmailCount: 1,
            hasMoreEmails: true);

        // Assert
        Assert.Equal(
            MailFolderRunReport.Synchronized(Start + TimeSpan.FromSeconds(30), 40, 2, 1, hasMoreEmails: true),
            ledger.ReadFolder(Inbox));
    }

    /// <summary>A folder that never opened stored nothing rather than storing none, which is what the second factory holds.</summary>
    [Theory]
    [InlineData(MailFolderRunOutcome.AliasUnresolved)]
    [InlineData(MailFolderRunOutcome.AliasAmbiguous)]
    [InlineData(MailFolderRunOutcome.DeferredAfterMailServerUnavailable)]
    [InlineData(MailFolderRunOutcome.UnexpectedFailure)]
    public void RecordFolderUnsynchronized_ReportsTheOutcomeWithNoCounts(MailFolderRunOutcome outcome)
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));

        // Act
        ledger.RecordFolderUnsynchronized(Inbox, outcome);

        // Assert
        Assert.Equal(MailFolderRunReport.Unsynchronized(outcome, Start), ledger.ReadFolder(Inbox));
    }

    /// <summary>A folder that did synchronize is described by the other factory, so the counts cannot be silently dropped.</summary>
    [Fact]
    public void RecordFolderUnsynchronized_WithASynchronizedOutcome_IsRefused()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));

        // Act
        void Record() => ledger.RecordFolderUnsynchronized(Inbox, MailFolderRunOutcome.Synchronized);

        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(Record);
    }

    /// <summary>A folder's report is the last one, because a status surface reports the most recent turn rather than a history.</summary>
    [Fact]
    public void RecordFolderRun_Twice_ReportsTheLaterTurn()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var ledger = new MailSynchronizationRunLedger(clock);

        // Act
        ledger.RecordFolderSynchronized(Inbox, 10, 0, 0, hasMoreEmails: false);
        clock.Advance(TimeSpan.FromMinutes(5));
        ledger.RecordFolderUnsynchronized(Inbox, MailFolderRunOutcome.DeferredAfterConcurrencyConflict);

        // Assert
        Assert.Equal(
            MailFolderRunReport.Unsynchronized(
                MailFolderRunOutcome.DeferredAfterConcurrencyConflict,
                Start + TimeSpan.FromMinutes(5)),
            ledger.ReadFolder(Inbox));
    }

    /// <summary>
    /// A folder unreachable turn after turn has been so since the first of them, which is what the client's freshness
    /// line measures from, and a turn that ends otherwise starts the instant again.
    /// </summary>
    [Fact]
    public void RecordFolderRun_TheSameOutcomeAgain_KeepsTheInstantItBeganAndADifferentOneRestartsIt()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var ledger = new MailSynchronizationRunLedger(clock);
        ledger.RecordFolderUnsynchronized(Inbox, MailFolderRunOutcome.DeferredAfterMailServerUnavailable);
        clock.Advance(TimeSpan.FromMinutes(5));

        // Act
        ledger.RecordFolderUnsynchronized(Inbox, MailFolderRunOutcome.DeferredAfterMailServerUnavailable);
        var stillUnreachable = ledger.ReadFolder(Inbox)?.OutcomeSince;
        clock.Advance(TimeSpan.FromMinutes(5));
        ledger.RecordFolderSynchronized(Inbox, 1, 0, 0, hasMoreEmails: false);

        // Assert
        Assert.Equal(
            ((DateTimeOffset?)Start, (DateTimeOffset?)(Start + TimeSpan.FromMinutes(10))),
            (stillUnreachable, ledger.ReadFolder(Inbox)?.OutcomeSince));
    }

    /// <summary>A folder behind turn after turn has been behind since the first, and stops being behind with the turn that caught up.</summary>
    [Fact]
    public void RecordFolderSynchronized_BehindTwiceThenCaughtUp_ReportsWhenItFellBehindThenNothing()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var ledger = new MailSynchronizationRunLedger(clock);
        ledger.RecordFolderSynchronized(Inbox, 200, 0, 0, hasMoreEmails: true);
        clock.Advance(TimeSpan.FromMinutes(1));

        // Act
        ledger.RecordFolderSynchronized(Inbox, 200, 0, 0, hasMoreEmails: true);
        var stillBehind = ledger.ReadFolder(Inbox)?.BehindSince;
        clock.Advance(TimeSpan.FromMinutes(1));
        ledger.RecordFolderSynchronized(Inbox, 12, 0, 0, hasMoreEmails: false);

        // Assert
        Assert.Equal(((DateTimeOffset?)Start, (DateTimeOffset?)null), (stillBehind, ledger.ReadFolder(Inbox)?.BehindSince));
    }

    /// <summary>An account's runs failing one after another have failed since the first, and a run that succeeds clears it.</summary>
    [Fact]
    public void RecordRunEnded_FailingTwiceThenSucceeding_ReportsWhenTheFailuresBeganThenNothing()
    {
        // Arrange
        var clock = new FakeTimeProvider(Start);
        var ledger = new MailSynchronizationRunLedger(clock);
        ledger.RecordRunEnded(Work, scheduledFolderCount: 1, failedFolderCount: 1, mutationConvergenceFailed: false);
        clock.Advance(TimeSpan.FromMinutes(2));

        // Act
        ledger.RecordRunEnded(Work, scheduledFolderCount: 1, failedFolderCount: 1, mutationConvergenceFailed: false);
        var stillFailing = ledger.ReadAccount(Work).LastRun?.FailedSince;
        clock.Advance(TimeSpan.FromMinutes(2));
        ledger.RecordRunEnded(Work, scheduledFolderCount: 1, failedFolderCount: 0, mutationConvergenceFailed: false);

        // Assert
        Assert.Equal(((DateTimeOffset?)Start, (DateTimeOffset?)null), (stillFailing, ledger.ReadAccount(Work).LastRun?.FailedSince));
    }

    /// <summary>One supervisor's failures never reach another account, which is the whole reason a supervisor is per account.</summary>
    [Fact]
    public void RecordRun_ForOneAccount_LeavesAnotherAccountUntouched()
    {
        // Arrange
        var ledger = new MailSynchronizationRunLedger(new FakeTimeProvider(Start));
        var personal = MailAccountId.Create("personal");

        // Act
        ledger.RecordNextRunDue(Work, TimeSpan.FromMinutes(30), consecutiveFailureCount: 6);

        // Assert
        Assert.Equal(MailAccountRunState.NotStarted, ledger.ReadAccount(personal));
    }
}
