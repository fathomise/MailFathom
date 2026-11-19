// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Tasks;

/// <summary>Sets time aside for a task, separately from the day it is due.</summary>
/// <remarks>
/// A due day says when the thing is owed; a plan says when the person intends to do it. The two are independent, so a
/// task may carry either, both, or neither.
/// </remarks>
public sealed record TaskPlan
{
    /// <summary>The shortest block of time a plan may set aside.</summary>
    public const int ShortestMinutes = 5;

    /// <summary>The longest block of time a plan may set aside, which is half a day.</summary>
    public const int LongestMinutes = 720;

    private TaskPlan(DateTimeOffset startAt, int minutes)
    {
        this.StartAt = startAt;
        this.Minutes = minutes;
    }

    /// <summary>Gets the instant the planned block begins.</summary>
    public DateTimeOffset StartAt { get; }

    /// <summary>Gets how long the block lasts, in minutes.</summary>
    public int Minutes { get; }

    /// <summary>Sets a block of time aside.</summary>
    /// <param name="startAt">The instant the block begins.</param>
    /// <param name="minutes">How long it lasts, between <see cref="ShortestMinutes" /> and <see cref="LongestMinutes" />.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="minutes" /> is outside that range.</exception>
    public static TaskPlan Create(DateTimeOffset startAt, int minutes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(minutes, ShortestMinutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minutes, LongestMinutes);

        return new TaskPlan(startAt, minutes);
    }
}
