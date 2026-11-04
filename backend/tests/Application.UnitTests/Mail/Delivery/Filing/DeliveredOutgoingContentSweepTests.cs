// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Access;
using MailFathom.Application.Coordination;
using MailFathom.Application.EmailContent.Storage;
using MailFathom.Application.Mail.Delivery.Filing;
using MailFathom.Application.Persistence;
using MailFathom.Domain.Access;
using MailFathom.Domain.Delivery;
using MailFathom.TestSupport;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Delivery.Filing;

/// <summary>
/// Covers the sweep that releases what the filing pass did not. What it has to hold is that only a send delivered long
/// enough ago is ever asked about, that a backlog is worked down in bounded batches each committed on its own, and that
/// a replica refused the lease releases nothing.
/// </summary>
public sealed class DeliveredOutgoingContentSweepTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly OutgoingEmailId First = OutgoingEmailId.Create(Guid.Parse("019a4f00-0000-7000-8000-000000000001"));

    private static readonly OutgoingEmailId Second = OutgoingEmailId.Create(Guid.Parse("019a4f00-0000-7000-8000-000000000002"));

    /// <summary>What the run reports is what the conditional statement removed, not what the read named.</summary>
    [Fact]
    public async Task RunAsync_DeliveredSendsStillHoldingAPayload_ReleasesThemAndCountsWhatTheStatementRemoved()
    {
        // Arrange
        var contents = ContentNaming([First, Second]);
        contents
            .ReleaseDeliveredOutgoingContentAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<IReadOnlyCollection<OutgoingEmailId>>(),
                Arg.Any<CancellationToken>())
            .Returns(1);

        var sweep = Sweeping(contents, new GrantingLeaseRunner());

        // Act
        var released = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, released);
        await contents.Received(1).ReleaseDeliveredOutgoingContentAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Is<IReadOnlyCollection<OutgoingEmailId>>(named => named.SequenceEqual(new[] { First, Second })),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A send delivered more recently than the floor may still have its sent copy being appended.</summary>
    [Fact]
    public async Task RunAsync_Always_AsksOnlyForSendsDeliveredBeforeTheAgeFloor()
    {
        // Arrange
        var contents = ContentNaming([]);
        var sweep = Sweeping(contents, new GrantingLeaseRunner());

        // Act
        await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        await contents.Received(1).FindDeliveredOutgoingContentAsync(
            Now - DeliveredOutgoingContentSweep.MinimumDeliveredAge,
            DeliveredOutgoingContentSweep.BatchSize,
            Arg.Any<CancellationToken>());
        await contents.DidNotReceiveWithAnyArgs().ReleaseDeliveredOutgoingContentAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<IReadOnlyCollection<OutgoingEmailId>>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>A backlog larger than a batch is worked down one committed batch at a time, and no further than one run's bound.</summary>
    [Fact]
    public async Task RunAsync_ABacklogOfFullBatches_StopsAtTheRunsBound()
    {
        // Arrange
        OutgoingEmailId[] fullBatch = [.. Enumerable.Range(0, DeliveredOutgoingContentSweep.BatchSize)
            .Select(_ => OutgoingEmailId.Create(Guid.CreateVersion7()))];
        var contents = ContentNaming(fullBatch);
        contents
            .ReleaseDeliveredOutgoingContentAsync(
                Arg.Any<IPersistenceSession>(),
                Arg.Any<IReadOnlyCollection<OutgoingEmailId>>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyCollection<OutgoingEmailId>>(1).Count);

        var sweep = Sweeping(contents, new GrantingLeaseRunner());

        // Act
        var released = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(DeliveredOutgoingContentSweep.BatchSize * DeliveredOutgoingContentSweep.MaximumBatchesPerRun, released);
        await contents.Received(DeliveredOutgoingContentSweep.MaximumBatchesPerRun).FindDeliveredOutgoingContentAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Any<int>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AReplicaRefusedTheLease_ReleasesNothingAndSaysSo()
    {
        // Arrange
        var contents = ContentNaming([First]);
        var runner = Substitute.For<IWorkLeaseRunner>();
        runner.TryRunUnderLeaseAsync(Arg.Any<WorkScope>(), Arg.Any<Func<CancellationToken, Task>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var sweep = Sweeping(contents, runner);

        // Act
        var released = await sweep.RunAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(released);
        await contents.DidNotReceiveWithAnyArgs().ReleaseDeliveredOutgoingContentAsync(
            Arg.Any<IPersistenceSession>(),
            Arg.Any<IReadOnlyCollection<OutgoingEmailId>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ACallerRatherThanTheProcess_IsRefused()
    {
        // Arrange
        var sweep = new DeliveredOutgoingContentSweep(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            ContentNaming([]),
            CommittingPolicy(),
            new GrantingLeaseRunner(),
            new FakeTimeProvider(Now));

        // Assert
        await Assert.ThrowsAsync<PrincipalNotAuthorizedException>(
            () => sweep.RunAsync(TestContext.Current.CancellationToken));
    }

    private static IEmailContentStore ContentNaming(IReadOnlyList<OutgoingEmailId> delivered)
    {
        var contents = Substitute.For<IEmailContentStore>();
        contents.FindDeliveredOutgoingContentAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(delivered);

        return contents;
    }

    private static OptimisticConcurrencyRetryPolicy CommittingPolicy()
    {
        var session = Substitute.For<IPersistenceSession>();
        session.CommitAsync(Arg.Any<CancellationToken>()).Returns(PersistenceCommitResult.Committed);
        var sessions = Substitute.For<IPersistenceSessionFactory>();
        sessions.BeginSessionAsync(Arg.Any<CancellationToken>()).Returns(session);

        return new OptimisticConcurrencyRetryPolicy(sessions, new PersistenceConcurrencyOptions(), new FakeTimeProvider(Now));
    }

    private static DeliveredOutgoingContentSweep Sweeping(IEmailContentStore contents, IWorkLeaseRunner leases) =>
        new(
            AccessAuthorizations.ForPrincipal(AuthorizedPrincipal.Process),
            contents,
            CommittingPolicy(),
            leases,
            new FakeTimeProvider(Now));

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
