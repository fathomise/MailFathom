// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Application.Jobs.Payloads;
using MailFathom.Application.Jobs.Scheduling;

namespace MailFathom.Application.Mail.Retention;

/// <summary>Declares the one nightly retention walk a deployment runs.</summary>
/// <remarks>
/// The walk's identity is minted per read and travels in the document the enqueue committed. The queue keys an occasion
/// on the schedule and the occurrence, so every replica proposes one occasion and exactly one proposal becomes it.
/// </remarks>
public sealed class MailRetentionScheduleSource : IScheduledJobSource
{
    /// <summary>The identity the walk's durable schedule state is keyed by.</summary>
    internal const string ScheduleIdentity = "mail-retention";

    private readonly JobRecurrence recurrence;

    /// <summary>Initializes the source over the recurrence the deployment configured.</summary>
    /// <param name="recurrence">The occasions a walk is dispatched on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="recurrence" /> is <see langword="null" />.</exception>
    public MailRetentionScheduleSource(JobRecurrence recurrence)
    {
        ArgumentNullException.ThrowIfNull(recurrence);

        this.recurrence = recurrence;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduledJob>> ReadSchedulesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ScheduledJob> declared =
        [
            new ScheduledJob(
                JobScheduleId.Create(ScheduleIdentity),
                WalkMailRetentionJobPayload.FromTheStart(Guid.CreateVersion7().ToString()),
                this.recurrence),
        ];

        return Task.FromResult(declared);
    }
}
