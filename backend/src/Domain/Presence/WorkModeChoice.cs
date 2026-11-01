// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Domain.Presence;

/// <summary>Where a member said they work from, and whether for today alone.</summary>
/// <param name="Mode">Office or remote.</param>
/// <param name="EndsAt">The member's next midnight where they said it for today, or <see langword="null" /> where it lasts until changed.</param>
public sealed record WorkModeChoice(WorkMode Mode, DateTimeOffset? EndsAt)
{
    /// <summary>Makes the choice a member stated, ending <em>today</em> in their own time zone.</summary>
    /// <param name="mode">Office or remote.</param>
    /// <param name="todayOnly"><see langword="true" /> for today alone, <see langword="false" /> until changed.</param>
    /// <param name="madeAt">When the member chose.</param>
    /// <param name="zone">The member's own time zone.</param>
    /// <returns>The choice.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zone" /> is <see langword="null" />.</exception>
    public static WorkModeChoice Made(WorkMode mode, bool todayOnly, DateTimeOffset madeAt, UserTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return new WorkModeChoice(mode, todayOnly ? PresenceDurations.EndOf(PresenceDuration.Today, madeAt, zone) : null);
    }

    /// <summary>Says whether the choice still holds at an instant.</summary>
    /// <param name="instant">The time of the read.</param>
    /// <returns><see langword="true" /> while today has not ended for a choice made for today alone.</returns>
    public bool IsInEffectAt(DateTimeOffset instant) => this.EndsAt is not { } end || instant < end;
}
