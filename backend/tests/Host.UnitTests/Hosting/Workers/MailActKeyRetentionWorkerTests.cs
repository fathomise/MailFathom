// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Mail.Mutations.Authoring.Idempotency;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the loop that deletes the keys of people's mailbox acts once their day has passed.</summary>
/// <remarks>
/// What the worker owns is the interval and the isolation; which keys are due is the retention's and the store's. The
/// isolation is the part worth a test here: a loop that ended on the first failed pass would leave a person's keys in
/// the database until the process was restarted.
/// </remarks>
public sealed class MailActKeyRetentionWorkerTests
{
    /// <summary>A key is held for its whole day, so a worker that swept on startup would count nothing it should not.</summary>
    [Fact]
    public async Task ExecuteAsync_BeforeTheFirstIntervalElapses_DeletesNothing()
    {
        // Arrange
        var store = Substitute.For<IMailActKeyStore>();
        var timeProvider = new FakeTimeProvider();
        using var worker = WorkerOver(store, timeProvider, out _);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        await store.DidNotReceive().EraseRecordedBeforeAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A failed pass is reported and the worker stays alive, so a later interval deletes what this one could not.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatFailed_ReportsItAndAsksAgainOnALaterInterval()
    {
        // Arrange
        var passes = 0;
        var store = Substitute.For<IMailActKeyStore>();
        store.EraseRecordedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<int>(_ =>
            {
                Interlocked.Increment(ref passes);

                throw new InvalidOperationException("the database is unavailable");
            });

        var timeProvider = new FakeTimeProvider();
        using var worker = WorkerOver(store, timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, () => Volatile.Read(ref passes) >= 2);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.Contains(
            logger.Messages,
            message => message.Contains("due to be deleted failed", StringComparison.Ordinal));
    }

    /// <summary>A pass is reported as a count, and nothing naming a person or a key reaches the log.</summary>
    [Fact]
    public async Task ExecuteAsync_APassThatDeletedKeys_ReportsTheCount()
    {
        // Arrange
        var deleted = false;
        var store = Substitute.For<IMailActKeyStore>();
        store.EraseRecordedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                deleted = true;

                return Task.FromResult(3);
            });

        var timeProvider = new FakeTimeProvider();
        using var worker = WorkerOver(store, timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, () => deleted);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(logger.Messages, message => message.Contains("Deleted 3 mailbox act keys", StringComparison.Ordinal));
    }

    /// <summary>
    /// A bulk act writes a key per message, so a day can leave more than one call deletes; a pass asks again until a call
    /// comes back short rather than leaving the rest for a later interval.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_ABacklogLargerThanOneCall_IsDrainedInOnePass()
    {
        // Arrange
        var remaining = (MailActKeyRetention.MaximumEntriesErasedPerPass * 2) + 3;
        var store = Substitute.For<IMailActKeyStore>();
        store.EraseRecordedBeforeAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var erased = Math.Min(remaining, call.ArgAt<int>(1));
                remaining -= erased;

                return Task.FromResult(erased);
            });

        var timeProvider = new FakeTimeProvider();
        using var worker = WorkerOver(store, timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, () => logger.Messages.Any(message => message.Contains("Deleted", StringComparison.Ordinal)));
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Contains(
            logger.Messages,
            message => message.Contains(
                $"Deleted {(MailActKeyRetention.MaximumEntriesErasedPerPass * 2) + 3} mailbox act keys",
                StringComparison.Ordinal));
    }

    /// <summary>Moves the clock on until the worker has reached what the test is waiting for.</summary>
    /// <remarks>
    /// A loop rather than one advance, because the wait on the next interval is created after the pass before it
    /// returns, and an advance that arrives before that wait exists is simply lost. The attempt count is what bounds a
    /// worker that never gets there, so a hang is reported as a failed assertion rather than as a suite that stopped.
    /// </remarks>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, Func<bool> reached)
    {
        const int advanceAttempts = 200;
        var passObservationWindow = TimeSpan.FromMilliseconds(20);

        for (var attempt = 0; attempt < advanceAttempts && !reached(); attempt++)
        {
            timeProvider.Advance(MailActKeyRetentionWorker.Interval);

            await Task.Delay(passObservationWindow, TestContext.Current.CancellationToken);
        }

        Assert.True(reached(), "The worker never reached what the test was waiting for.");
    }

    /// <summary>Composes the worker over a container holding the retention, exactly as the host resolves one per pass.</summary>
    private static MailActKeyRetentionWorker WorkerOver(
        IMailActKeyStore store,
        FakeTimeProvider timeProvider,
        out RecordingLogger<MailActKeyRetentionWorker> logger)
    {
        logger = new RecordingLogger<MailActKeyRetentionWorker>();

        var services = new ServiceCollection();
        services.AddSingleton(new MailActKeyRetention(store, timeProvider));

        var serviceProvider = services.BuildServiceProvider();

        return new MailActKeyRetentionWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            timeProvider);
    }
}
