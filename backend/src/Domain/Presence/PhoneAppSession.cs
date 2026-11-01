// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Presence;

/// <summary>One session a member signed in to the phone app with, as far as whether they use the app depends on it.</summary>
/// <param name="LastOpenedOn">The UTC day the app was last opened on this session.</param>
/// <param name="ExpiresAt">When the session stops authenticating, after which it says nothing about the member.</param>
/// <remarks>
/// A session signed out of is gone rather than held here, and so is one past its expiry once it is swept, so whether
/// the app is still installed is read from a session still standing: uninstalling without signing out leaves one only
/// until it expires or the seven days pass.
/// </remarks>
public sealed record PhoneAppSession(DateOnly LastOpenedOn, DateTimeOffset ExpiresAt)
{
    /// <summary>How many days, counting the day of the read, an opening keeps the member marked as using the app.</summary>
    public const int OpeningDays = 7;

    /// <summary>Says whether the session still marks the member as using the phone app at an instant.</summary>
    /// <param name="instant">The time of the read.</param>
    /// <returns><see langword="true" /> while the session authenticates and the app was opened on it within <see cref="OpeningDays" /> days.</returns>
    public bool MarksAt(DateTimeOffset instant) =>
        instant <= this.ExpiresAt
        && this.LastOpenedOn > DateOnly.FromDateTime(instant.UtcDateTime).AddDays(-OpeningDays);
}
