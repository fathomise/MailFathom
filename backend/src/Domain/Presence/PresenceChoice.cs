// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Domain.Presence;

/// <summary>The presence a member chose, how long they chose it for, and when the choice ends.</summary>
/// <param name="State">The state chosen, which may be any of the six.</param>
/// <param name="Duration">The while the member picked, or <see langword="null" /> where it lasts until changed. Kept beside <paramref name="EndsAt" /> because the status menu names the while it was chosen for — <em>Busy · 1 hour</em> — which the instant alone cannot say.</param>
/// <param name="EndsAt">The instant the choice runs out, or <see langword="null" /> where it lasts until changed.</param>
/// <remarks>
/// A choice that runs out is not cleared by anybody: whoever reads it compares <paramref name="EndsAt" /> with the time
/// of the read, so the member is back to the presence their clients produce whether or not any process was running
/// when it passed.
/// </remarks>
public sealed record PresenceChoice(PresenceState State, PresenceDuration? Duration, DateTimeOffset? EndsAt)
{
    /// <summary>Makes the choice a member stated, resolving its duration in their own time zone.</summary>
    /// <param name="state">The state chosen.</param>
    /// <param name="duration">How long it lasts, or <see langword="null" /> for until changed.</param>
    /// <param name="madeAt">When the member chose.</param>
    /// <param name="zone">The member's own time zone, which is where <em>today</em> and <em>this week</em> end.</param>
    /// <returns>The choice, or <see langword="null" /> where choosing returns the member to automatic presence.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="zone" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// <b><see cref="PresenceState.Active" /> until changed is not a choice.</b> The status menu has no way back to the
    /// presence a member's clients produce other than picking a state, and <see cref="PresenceState.Active" /> is the
    /// one those clients already produce for somebody at their keyboard, so picking it clears the choice rather than
    /// pinning <see cref="PresenceState.Active" /> on somebody who then walks away. With a duration it is a choice like
    /// the other five.
    /// </remarks>
    public static PresenceChoice? Made(
        PresenceState state,
        PresenceDuration? duration,
        DateTimeOffset madeAt,
        UserTimeZone zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        if (duration is not { } lasting)
        {
            return state == PresenceState.Active ? null : new PresenceChoice(state, Duration: null, EndsAt: null);
        }

        return new PresenceChoice(state, lasting, PresenceDurations.EndOf(lasting, madeAt, zone));
    }

    /// <summary>Says whether the choice still holds at an instant.</summary>
    /// <param name="instant">The time of the read.</param>
    /// <returns><see langword="true" /> while the choice has not run out.</returns>
    public bool IsInEffectAt(DateTimeOffset instant) => this.EndsAt is not { } end || instant < end;
}
