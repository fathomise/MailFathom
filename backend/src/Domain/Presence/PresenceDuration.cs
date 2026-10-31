// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Presence;

/// <summary>How long a chosen presence lasts before the member is back to the presence their clients produce.</summary>
/// <remarks><see cref="PresenceChoice.Made" /> resolves each one to an instant in the member's own time zone.</remarks>
public enum PresenceDuration
{
    /// <summary>Thirty minutes from the choice.</summary>
    ThirtyMinutes = 0,

    /// <summary>One hour from the choice.</summary>
    OneHour = 1,

    /// <summary>Two hours from the choice.</summary>
    TwoHours = 2,

    /// <summary>Until the next midnight in the member's time zone.</summary>
    Today = 3,

    /// <summary>Until the midnight that ends Sunday in the member's time zone, the end of the ISO 8601 week.</summary>
    ThisWeek = 4,
}
