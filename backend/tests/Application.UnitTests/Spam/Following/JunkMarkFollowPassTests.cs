// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Spam.Following;
using MailFathom.Application.Spam.Runs;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Spam.Following;

public sealed class JunkMarkFollowPassTests
{
    private static readonly MailAccountId Account = MailAccountId.Create("acct-1");

    private static readonly DateTimeOffset Now = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    private static readonly JobId SomeJob = JobId.Create(Guid.Parse("0199a0c0-0000-7000-8000-00000000000a"));

    private static readonly OwedJunkMarkChange FirstChange =
        new(StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000001")), Now.AddMinutes(-5));

    private static readonly OwedJunkMarkChange SecondChange =
        new(StoredEmailId.Create(Guid.Parse("0199a0c0-0000-7000-8000-000000000002")), Now.AddMinutes(-4));

    private readonly IJunkMarkFollowStore store = Substitute.For<IJunkMarkFollowStore>();

    private readonly IJobStore jobs = Substitute.For<IJobStore>();

    /// <summary>A change that reached the queue is the job's now, so it is no longer owed.</summary>
    [Fact]
    public async Task RunAsync_AnOwedChange_EnqueuesItAndSettlesIt()
    {
        // Arrange
        this.Owing(FirstChange);
        this.Answering(JobEnqueueResult.Created(SomeJob));

        // Act
        var carried = await this.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, carried);
        await this.jobs.Received(1).EnqueueAsync(
            Arg.Is<JobEnqueueRequest>(request => request.Payload is FollowJunkMarksJobPayload
                && ((FollowJunkMarksJobPayload)request.Payload).ChangeObservedAt == FirstChange.ObservedAt),
            Arg.Any<CancellationToken>());
        await this.store.Received(1).SettleAsync(FirstChange, Arg.Any<CancellationToken>());
    }

    /// <summary>A job already there, whatever its state, is the same change asked for by a second replica or a retried pass.</summary>
    [Fact]
    public async Task RunAsync_TheQueueAlreadyHoldsTheJob_SettlesTheChange()
    {
        // Arrange
        this.Owing(FirstChange);
        this.Answering(JobEnqueueResult.AlreadyEnqueued(SomeJob));

        // Act
        await this.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).SettleAsync(FirstChange, Arg.Any<CancellationToken>());
    }

    /// <summary>A full queue delays a change and never loses it: the observation has nothing left to differ from next time.</summary>
    [Fact]
    public async Task RunAsync_AFullQueue_StopsAndLeavesTheChangeOwed()
    {
        // Arrange
        this.Owing(FirstChange, SecondChange);
        this.Answering(JobEnqueueResult.RefusedAtCapacity());

        // Act
        var carried = await this.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(0, carried);
        await this.jobs.Received(1).EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>());
        await this.store.DidNotReceive().SettleAsync(Arg.Any<OwedJunkMarkChange>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The queue keeps a finished job's key, so a later change to the same message needs a key of its own or it would never run.</summary>
    [Fact]
    public async Task RunAsync_TwoChangesToOneMessage_EnqueueUnderDifferentKeys()
    {
        // Arrange
        var keys = new List<JobIdempotencyKey>();
        var later = FirstChange with { ObservedAt = FirstChange.ObservedAt.AddHours(1) };
        this.jobs.EnqueueAsync(Arg.Do<JobEnqueueRequest>(request => keys.Add(request.Key)), Arg.Any<CancellationToken>())
            .Returns(JobEnqueueResult.Created(SomeJob));

        // Act
        this.Owing(FirstChange);
        await this.Pass().RunAsync(Account, TestContext.Current.CancellationToken);
        this.Owing(later);
        await this.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, keys.Distinct().Count());
    }

    /// <summary>A departure only has to outlast the runs between a move and the arrival it causes, so it is not kept.</summary>
    [Fact]
    public async Task RunAsync_Always_ForgetsDeparturesOlderThanAWeek()
    {
        // Arrange
        this.Owing();

        // Act
        await this.Pass().RunAsync(Account, TestContext.Current.CancellationToken);

        // Assert
        await this.store.Received(1).ForgetJunkFolderDeparturesAsync(Account, Now.AddDays(-7), Arg.Any<CancellationToken>());
    }

    private void Owing(params OwedJunkMarkChange[] changes) =>
        this.store.GetOwedAsync(Account, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<OwedJunkMarkChange>>(changes));

    private void Answering(JobEnqueueResult answer) =>
        this.jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>()).Returns(answer);

    private JunkMarkFollowPass Pass() =>
        new(this.store, this.jobs, new SpamClassificationRunOptions { BatchSize = 50, MaxBatchesPerPass = 1 }, new FakeTimeProvider(Now));
}
