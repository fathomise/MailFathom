// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Presence;

/// <summary>One of the six states a member of an organization can be seen in.</summary>
/// <remarks>
/// <see cref="Active" />, <see cref="Away" />, and <see cref="Offline" /> are also what a member's clients produce on
/// their own; every one of the six can be chosen. <see cref="Hidden" /> is the member's own name for a state everybody
/// else reads as <see cref="Offline" />.
/// </remarks>
public enum PresenceState
{
    /// <summary>No client of the member's is open, or they chose to look absent.</summary>
    Offline = 0,

    /// <summary>A client of the member's is open and in use.</summary>
    Active = 1,

    /// <summary>Every open client of the member's has been idle for ten minutes.</summary>
    Away = 2,

    /// <summary>Busy, by the member's choice.</summary>
    Busy = 3,

    /// <summary>Not to be disturbed, by the member's choice.</summary>
    DoNotDisturb = 4,

    /// <summary>Present but seen by everybody else as <see cref="Offline" />, with no last-seen time.</summary>
    Hidden = 5,
}
