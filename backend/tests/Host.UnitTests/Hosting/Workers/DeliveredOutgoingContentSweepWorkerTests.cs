// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Delivery;
using MailFathom.Host.Hosting.Workers;
using MailFathom.Host.UnitTests.TestDoubles;
using MailFathom.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Hosting.Workers;

/// <summary>Covers the loop that releases delivered sends' stored messages once per interval.</summary>
/// <remarks>
/// What the worker owns is the interval and the isolation, not which payloads a run releases — that is asserted where
/// the sweep lives. A loop that ended on the first failed run would keep every later straggler's bytes for good.
/// </remarks>
public sealed class DeliveredOutgoingContentSweepWorkerTests
{
    /// <summary>Guards against a hung worker. No assertion depends on how long a run actually takes.</summary>
    private static readonly TimeSpan DeadlockGuard = TimeSpan.FromSeconds(30);

    private static readonly OutgoingEmailId Delivered = OutgoingEmailId.Create(Guid.Parse("019a4f00-0000-7000-8000-000000000003"));

    /// <summary>A run is reported as a count, and nothing that names a send reaches the log.</summary>
    [Fact]
    public async Task ExecuteAsync_ARunThatReleasedAPayload_ReportsTheCountWithoutNamingTheSend()
    {
        // Arrange
        var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contents = Substitute.For<IEmailContentStore>();
        contents.FindDeliveredOutgoingContentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Delivered]);
        contents
            .ReleaseDeliveredOutgoingContentAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<IReadOnlyCollection<OutgoingEmailId>>(),
                Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                released.TrySetResult();

                return 1;
            });
        using var worker = CreateWorker(contents, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, released.Task);
        await WaitForMessageAsync(logger);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        var reported = Assert.Single(logger.Messages);
        Assert.Contains("stored messages of 1 delivered sends", reported, StringComparison.Ordinal);
        Assert.DoesNotContain(Delivered.ToString(), reported, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A failed run is reported and the worker stays alive, so a later interval releases what this one could not.</summary>
    [Fact]
    public async Task ExecuteAsync_ARunThatFailed_ReportsItAndRunsAgainOnALaterInterval()
    {
        // Arrange
        var secondRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runs = 0;
        var contents = Substitute.For<IEmailContentStore>();
        contents.FindDeliveredOutgoingContentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<OutgoingEmailId>>(_ =>
            {
                if (Interlocked.Increment(ref runs) >= 2)
                {
                    secondRun.TrySetResult();
                }

                throw new InvalidOperationException("the database is unavailable");
            });
        using var worker = CreateWorker(contents, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, secondRun.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.False(worker.ExecuteTask!.IsFaulted);
        Assert.Contains(logger.Messages, message => message.Contains("release of delivered sends' stored messages failed", StringComparison.Ordinal));
    }

    /// <summary>Shutdown is not a failure: a rolling restart would otherwise read as a release that broke on every replica.</summary>
    [Fact]
    public async Task ExecuteAsync_TheHostStopsWhileARunIsUnderWay_ReportsNoFailure()
    {
        // Arrange
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contents = Substitute.For<IEmailContentStore>();
        contents.FindDeliveredOutgoingContentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => BlockedUntilCancelledAsync(entered, call.ArgAt<CancellationToken>(2)));
        using var worker = CreateWorker(contents, out var timeProvider, out var logger);

        // Act
        await worker.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(timeProvider, entered.Task);
        await worker.StopAsync(CancellationToken.None);

        // Assert
        Assert.Empty(logger.Messages);
    }

    /// <summary>Moves the clock on until the worker has reached what the test is waiting for.</summary>
    /// <remarks>
    /// A loop rather than one advance, because the wait on the next interval is created after the run before it returns,
    /// and an advance that arrives before that wait exists is simply lost.
    /// </remarks>
    private static async Task AdvanceUntilAsync(FakeTimeProvider timeProvider, Task reached)
    {
        const int advanceAttempts = 200;
        var runObservationWindow = TimeSpan.FromMilliseconds(20);

        for (var attempt = 0; attempt < advanceAttempts && !reached.IsCompleted; attempt++)
        {
            timeProvider.Advance(DeliveredOutgoingContentSweepWorker.Interval);

            await Task.WhenAny(reached, Task.Delay(runObservationWindow, TestContext.Current.CancellationToken));
        }

        await reached.WaitAsync(DeadlockGuard, TestContext.Current.CancellationToken);
    }

    /// <summary>Waits for the count the run logs after its last batch committed, which follows the release it was waiting on.</summary>
    private static async Task WaitForMessageAsync(RecordingLogger<DeliveredOutgoingContentSweepWorker> logger)
    {
        const int observationAttempts = 1500;
        var observationWindow = TimeSpan.FromMilliseconds(20);

        for (var attempt = 0; attempt < observationAttempts && logger.Messages.Count == 0; attempt++)
        {
            await Task.Delay(observationWindow, TestContext.Current.CancellationToken);
        }
    }

    private static async Task<IReadOnlyList<OutgoingEmailId>> BlockedUntilCancelledAsync(
        TaskCompletionSource entered,
        CancellationToken cancellationToken)
    {
        entered.TrySetResult();

        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);

        throw new InvalidOperationException("The database answered after all.");
    }

    /// <summary>Composes the worker over the real sweep, a substituted content store, a committing session, and a lease nothing competes for.</summary>
    private static DeliveredOutgoingContentSweepWorker CreateWorker(
        IEmailContentStore contents,
        out FakeTimeProvider timeProvider,
        out RecordingLogger<DeliveredOutgoingContentSweepWorker> logger)
    {
        logger = new RecordingLogger<DeliveredOutgoingContentSweepWorker>();
        timeProvider = new FakeTimeProvider();

        var session = Substitute.For<IPersistenceSession>();
        session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(session);

        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton(contents);
        services.AddSingleton(new OptimisticConcurrencyRetryPolicy(sessions, new PersistenceConcurrencyOptions(), timeProvider));
        services.AddSingleton<IWorkLeaseRunner>(new GrantingLeaseRunner());
        services.AddSingleton(AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process));
        services.AddScoped<DeliveredOutgoingContentSweep>();

        var serviceProvider = services.BuildServiceProvider();

        return new DeliveredOutgoingContentSweepWorker(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            logger,
            timeProvider);
    }

    /// <summary>Stands in for the lease table on a replica nothing competes with.</summary>
    private sealed class GrantingLeaseRunner : IWorkLeaseRunner
    {
        public async Task<bool> TryRunUnderLeaseAsync(
            WorkScope scope,
            Func<CancellationToken, Task> work,
            CancellationToken cancellationToken)
        {
            await work(cancellationToken);

            return true;
        }
    }
}
