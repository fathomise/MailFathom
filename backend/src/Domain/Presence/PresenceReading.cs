// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Presence;

/// <summary>A member's presence as the other members of their organization see it.</summary>
/// <param name="State">The state, which is never <see cref="PresenceState.Hidden" />.</param>
/// <param name="LastSeen">When the member was last present, shown only for <see cref="PresenceState.Offline" /> and never for a member who chose to hide.</param>
/// <param name="UsesPhoneApp">Whether the member uses the phone app, whatever their state and never for a member who chose to hide — never the day it rests on.</param>
/// <param name="Message">The member's status message while it has not cleared, or <see langword="null" />.</param>
/// <param name="WorkMode">Where the member works from while it holds, or <see langword="null" />.</param>
public sealed record PresenceReading(
    PresenceState State,
    DateTimeOffset? LastSeen,
    bool UsesPhoneApp,
    StatusMessage? Message = null,
    WorkModeChoice? WorkMode = null);
