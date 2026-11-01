// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Presence;

namespace MailFathom.Application.Presence;

/// <summary>A member's presence as they see it themselves.</summary>
/// <param name="State">Their state, <see cref="PresenceState.Hidden" /> included.</param>
/// <param name="Choice">The choice still in effect, or <see langword="null" /> where their presence is the one their clients produce.</param>
/// <param name="Message">Their status message while it has not cleared, or <see langword="null" />.</param>
/// <param name="WorkMode">Where they said they work from while it holds, or <see langword="null" />.</param>
public sealed record OwnPresenceReading(
    PresenceState State,
    PresenceChoice? Choice,
    StatusMessage? Message = null,
    WorkModeChoice? WorkMode = null)
{
    /// <summary>Reads a member's presence as they see it themselves.</summary>
    /// <param name="presence">What it is worked out from.</param>
    /// <param name="instant">The time of the read.</param>
    /// <returns>The reading.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="presence" /> is <see langword="null" />.</exception>
    public static OwnPresenceReading Of(MemberPresence presence, DateTimeOffset instant)
    {
        ArgumentNullException.ThrowIfNull(presence);

        return new OwnPresenceReading(
            presence.OwnStateAt(instant),
            presence.ChoiceInEffectAt(instant),
            presence.MessageInEffectAt(instant),
            presence.WorkModeInEffectAt(instant));
    }
}
