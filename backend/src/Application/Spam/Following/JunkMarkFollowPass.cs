// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Globalization;
using MailFathom.Application.Classification;
using MailFathom.Application.Jobs;
using MailFathom.Application.Jobs.Payloads;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.Spam.Following;

/// <summary>Hands the junk-mark changes synchronization observed to the classification queue.</summary>
/// <remarks>
/// <para>
/// A step of the account's run rather than a call the reconciliation makes, because the queue takes no persistence
/// session: a change is first written down in the transaction that observed it, and this carries it across, so a crash
/// or a full queue delays a change and never loses it. It reads nothing about custody. A change is only ever written
/// down where the source is the truth, or as the marks a new arrival came with.
/// </para>
/// <para>
/// A second replica finding the work done is the ordinary case: the job's key is the email and the instant of the change,
/// so two passes reach one job, and a later change is a later key rather than an answer from a job already finished.
/// </para>
/// </remarks>
public sealed class JunkMarkFollowPass
{
    /// <summary>How long a departure from the junk folder is remembered, which comfortably covers the runs between another client's move and the arrival it causes.</summary>
    private static readonly TimeSpan JunkFolderDepartureMemory = TimeSpan.FromDays(7);

    private readonly IJunkMarkFollowStore store;
    private readonly IJobStore jobs;
    private readonly ClassificationRunOptions options;
    private readonly TimeProvider timeProvider;

    /// <summary>Initializes the pass over what it reads and the queue it writes to.</summary>
    /// <param name="store">Reads the owed changes and records the ones that reached the queue.</param>
    /// <param name="jobs">The durable queue each decision is enqueued into.</param>
    /// <param name="options">Bounds one pass, with the width a classification run's pass has.</param>
    /// <param name="timeProvider">Dates what is forgotten.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public JunkMarkFollowPass(
        IJunkMarkFollowStore store,
        IJobStore jobs,
        ClassificationRunOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.store = store;
        this.jobs = jobs;
        this.options = options;
        this.timeProvider = timeProvider;
    }

    /// <summary>Enqueues the account's owed decisions, a bounded number of batches at a time.</summary>
    /// <param name="account">The account whose changes are carried.</param>
    /// <param name="cancellationToken">Cancels the pass.</param>
    /// <returns>How many changes reached the queue.</returns>
    /// <remarks>A full queue ends the pass with the rest still owed, for the next run to carry.</remarks>
    public async Task<int> RunAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        await this.store.ForgetJunkFolderDeparturesAsync(
            account,
            this.timeProvider.GetUtcNow() - JunkFolderDepartureMemory,
            cancellationToken);

        var carried = 0;

        for (var batch = 0; batch < this.options.MaxBatchesPerPass; batch++)
        {
            var owed = await this.store.GetOwedAsync(account, this.options.BatchSize, cancellationToken);

            foreach (var change in owed)
            {
                var answer = await this.jobs.EnqueueAsync(RequestFor(account, change), cancellationToken);

                if (answer.Outcome is JobEnqueueOutcome.RefusedAtCapacity)
                {
                    return carried;
                }

                await this.store.SettleAsync(change, cancellationToken);
                carried++;
            }

            if (owed.Count < this.options.BatchSize)
            {
                break;
            }
        }

        return carried;
    }

    private static JobEnqueueRequest RequestFor(MailAccountId account, OwedJunkMarkChange change) => JobEnqueueRequest.Create(
        JobIdempotencyKey.Create(string.Create(
            CultureInfo.InvariantCulture,
            $"{change.EmailId.Value:d}@{change.ObservedAt.UtcTicks}")),
        FollowJunkMarksJobPayload.For(account, change.EmailId, change.ObservedAt),
        account);
}
