// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Jobs.Payloads;

/// <summary>Points one segment of a night's retention walk at the accounts it has not reached yet.</summary>
/// <remarks>
/// The walk is named by the occasion that dispatched it, in the document the enqueue committed, so every execution of
/// one leased segment composes the same keys and a repeated attempt is answered with the jobs already waiting.
/// </remarks>
public sealed record WalkMailRetentionJobPayload : IJobPayload
{
    /// <summary>Gets the walk this job is a segment of, which the occasion that dispatched it named.</summary>
    public required string WalkId { get; init; }

    /// <summary>Gets which segment of the walk this job carries, counted from the one the schedule dispatched.</summary>
    public int Segment { get; init; }

    /// <summary>Gets the account the previous segment stopped at, or <see langword="null" /> to begin with the first.</summary>
    public Guid? ResumeAfter { get; init; }

    /// <inheritdoc />
    [JsonIgnore]
    public JobType JobType => JobType.WalkMailRetention;

    /// <summary>Describes the first segment of one night's walk.</summary>
    /// <param name="walkId">The identity of the walk this occasion begins.</param>
    /// <returns>The payload a schedule dispatches.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="walkId" /> is blank.</exception>
    public static WalkMailRetentionJobPayload FromTheStart(string walkId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walkId);

        return new WalkMailRetentionJobPayload { WalkId = walkId };
    }

    /// <summary>Describes the segment that carries the accounts after the given one.</summary>
    /// <param name="account">The last account this segment read.</param>
    /// <returns>The payload of the next segment of the same walk.</returns>
    public WalkMailRetentionJobPayload ContinuingAfter(Guid account) =>
        this with { Segment = this.Segment + 1, ResumeAfter = account };

    /// <summary>Composes the identity under which this segment is handed on.</summary>
    /// <returns>The idempotency key, which no other segment of any walk shares.</returns>
    public JobIdempotencyKey ToIdempotencyKey() =>
        JobIdempotencyKey.Create($"{JobType.WalkMailRetention.Name}:{this.WalkId}:{this.Segment}");
}
