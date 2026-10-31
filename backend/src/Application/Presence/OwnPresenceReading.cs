// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Presence;

namespace MailFathom.Application.Presence;

/// <summary>A member's presence as they see it themselves.</summary>
/// <param name="State">Their state, <see cref="PresenceState.Hidden" /> included.</param>
/// <param name="Choice">The choice still in effect, or <see langword="null" /> where their presence is the one their clients produce.</param>
public sealed record OwnPresenceReading(PresenceState State, PresenceChoice? Choice);
