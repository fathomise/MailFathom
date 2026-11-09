// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Mail.Retention;
using MailFathom.Domain.Accounts;
using NSubstitute;
using Xunit;

namespace MailFathom.Application.UnitTests.Mail.Retention;

/// <summary>Covers what one segment of the walk hands out and what it hands on.</summary>
public sealed class MailRetentionWalkHandlerTests
{
    private const string Walk = "walk-of-the-occasion";

    private static readonly MailAccountId FirstAccount = MailAccountId.Create("0199a0c0-0000-7000-8000-000000000001");
    private static readonly MailAccountId SecondAccount = MailAccountId.Create("0199a0c0-0000-7000-8000-000000000002");
    private static readonly Guid ResumeAfter = Guid.Parse("0199a0c0-0000-7000-8000-000000000002");

    /// <summary>A job naming the account is what an erasure locks and waits for on every replica.</summary>
    [Fact]
    public async Task RunAsync_APageOfAccounts_EnqueuesARunNamingEachAccount()
    {
        // Arrange
        var jobs = JobStoreAccepting();
        var handler = new MailRetentionWalkHandler(jobs, Accounts(new([FirstAccount, SecondAccount], null)));

        // Act
        await handler.RunAsync(WalkMailRetentionJobPayload.FromTheStart(Walk), TestContext.Current.CancellationToken);

        // Assert
        foreach (var account in new[] { FirstAccount, SecondAccount })
        {
            await jobs.Received(1).EnqueueAsync(
                Arg.Is<JobEnqueueRequest>(request => request.JobType == JobType.RetainMailAccount && request.Account == account),
                Arg.Any<CancellationToken>());
        }
    }

    [Fact]
    public async Task RunAsync_TheLastPage_HandsNothingOn()
    {
        // Arrange
        var jobs = JobStoreAccepting();
        var handler = new MailRetentionWalkHandler(jobs, Accounts(new([FirstAccount], null)));

        // Act
        await handler.RunAsync(WalkMailRetentionJobPayload.FromTheStart(Walk), TestContext.Current.CancellationToken);

        // Assert
        await jobs.DidNotReceive().EnqueueAsync(
            Arg.Is<JobEnqueueRequest>(request => request.JobType == JobType.WalkMailRetention),
            Arg.Any<CancellationToken>());
    }

    /// <summary>The next segment resumes after the page and belongs to no account, so an erasure cannot end the walk.</summary>
    [Fact]
    public async Task RunAsync_APageWithMoreBehindIt_HandsTheRestToASegmentNamingNoAccount()
    {
        // Arrange
        var jobs = JobStoreAccepting();
        var handler = new MailRetentionWalkHandler(jobs, Accounts(new([FirstAccount], ResumeAfter)));

        // Act
        await handler.RunAsync(WalkMailRetentionJobPayload.FromTheStart(Walk), TestContext.Current.CancellationToken);

        // Assert
        await jobs.Received(1).EnqueueAsync(
            Arg.Is<JobEnqueueRequest>(request =>
                request.JobType == JobType.WalkMailRetention
                && request.Account == null
                && ((WalkMailRetentionJobPayload)request.Payload).ResumeAfter == ResumeAfter
                && ((WalkMailRetentionJobPayload)request.Payload).Segment == 1),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ASegmentThatResumes_ReadsFromWhereThePreviousOneStopped()
    {
        // Arrange
        var accounts = Accounts(new([], null));
        var handler = new MailRetentionWalkHandler(JobStoreAccepting(), accounts);
        var segment = WalkMailRetentionJobPayload.FromTheStart(Walk).ContinuingAfter(ResumeAfter);

        // Act
        await handler.RunAsync(segment, TestContext.Current.CancellationToken);

        // Assert
        await accounts.Received(1).ReadPageAsync(
            ResumeAfter,
            MailRetentionWalkHandler.AccountsPerSegment,
            Arg.Any<CancellationToken>());
    }

    /// <summary>Reported rather than read as a walk that finished, because the segment is the only record of where it had reached.</summary>
    [Fact]
    public async Task RunAsync_AQueueRefusingAtCapacity_RaisesSoTheSegmentIsRetried()
    {
        // Arrange
        var jobs = Substitute.For<IJobStore>();
        jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(JobEnqueueResult.RefusedAtCapacity()));
        var handler = new MailRetentionWalkHandler(jobs, Accounts(new([FirstAccount], null)));

        // Act, Assert
        await Assert.ThrowsAsync<JobHandOnRefusedAtCapacityException>(
            () => handler.RunAsync(WalkMailRetentionJobPayload.FromTheStart(Walk), TestContext.Current.CancellationToken));
    }

    private static IMailRetentionAccounts Accounts(MailRetentionAccountPage page)
    {
        var accounts = Substitute.For<IMailRetentionAccounts>();
        accounts.ReadPageAsync(Arg.Any<Guid?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(page));

        return accounts;
    }

    private static IJobStore JobStoreAccepting()
    {
        var jobs = Substitute.For<IJobStore>();
        jobs.EnqueueAsync(Arg.Any<JobEnqueueRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(JobEnqueueResult.Created(JobId.Create(Guid.CreateVersion7()))));

        return jobs;
    }
}
