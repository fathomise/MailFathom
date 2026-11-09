// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Execution;
using MailFathom.Application.Jobs.Payloads;

namespace MailFathom.Application.Mail.Retention;

/// <summary>Carries one segment of a night's retention walk: a page of accounts, each handed a job of its own.</summary>
/// <remarks>
/// <para>
/// The segment names no account, so erasing one cannot take the rest of the walk with it. What runs against an account
/// is a job naming that account, because a job row is what an erasure locks and waits for across every replica, and
/// an account that keeps failing fails its own job rather than holding up every account after it.
/// </para>
/// <para>
/// Running a segment twice is the same as running it once: every key is composed from the payload the enqueue committed,
/// so a repeated attempt is answered with the jobs already waiting.
/// </para>
/// </remarks>
public sealed class MailRetentionWalkHandler : IJobHandler
{
    /// <summary>How many accounts one segment hands out, which bounds one attempt however many accounts a deployment serves.</summary>
    public const int AccountsPerSegment = 100;

    private readonly IJobStore jobs;
    private readonly IMailRetentionAccounts accounts;

    /// <summary>Initializes the handler.</summary>
    /// <param name="jobs">Enqueues each account's run and the segment after this one.</param>
    /// <param name="accounts">Reads the accounts the walk visits.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailRetentionWalkHandler(IJobStore jobs, IMailRetentionAccounts accounts)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(accounts);

        this.jobs = jobs;
        this.accounts = accounts;
    }

    /// <inheritdoc />
    public JobType JobType => JobType.WalkMailRetention;

    /// <inheritdoc />
    /// <exception cref="ArgumentException">Thrown when the payload is not the contract this job type names.</exception>
    /// <exception cref="JobHandOnRefusedAtCapacityException">Thrown when the queue refused an account's run or the next segment.</exception>
    public async Task RunAsync(IJobPayload payload, CancellationToken cancellationToken)
    {
        if (payload is not WalkMailRetentionJobPayload segment)
        {
            throw new ArgumentException(
                $"A '{JobType.WalkMailRetention}' job carries a payload naming a place in the walk over the accounts.",
                nameof(payload));
        }

        var page = await this.accounts.ReadPageAsync(segment.ResumeAfter, AccountsPerSegment, cancellationToken);

        foreach (var account in page.Accounts)
        {
            var run = RetainMailAccountJobPayload.For(segment.WalkId, account);

            await this.EnqueueAsync(JobEnqueueRequest.Create(run.ToIdempotencyKey(), run, account), cancellationToken);
        }

        if (page.ResumeAfter is { } resumeAfter)
        {
            var next = segment.ContinuingAfter(resumeAfter);

            await this.EnqueueAsync(JobEnqueueRequest.Create(next.ToIdempotencyKey(), next, account: null), cancellationToken);
        }
    }

    private async Task EnqueueAsync(JobEnqueueRequest request, CancellationToken cancellationToken)
    {
        var enqueued = await this.jobs.EnqueueAsync(request, cancellationToken);

        if (enqueued.Outcome is JobEnqueueOutcome.RefusedAtCapacity)
        {
            throw new JobHandOnRefusedAtCapacityException(JobType.WalkMailRetention);
        }
    }
}
