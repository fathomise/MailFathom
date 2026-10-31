// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.Presence;

/// <summary>One client a member keeps open, as its last renewal left it.</summary>
/// <param name="Activity">Whether a person was using it at that renewal.</param>
/// <param name="ExpiresAt">When it stops counting unless renewed again.</param>
public sealed record OpenPresenceClient(ClientActivity Activity, DateTimeOffset ExpiresAt)
{
    /// <summary>Says whether the client still counts as open at an instant.</summary>
    /// <param name="instant">The time of the read.</param>
    /// <returns><see langword="true" /> before its expiry.</returns>
    public bool IsOpenAt(DateTimeOffset instant) => instant < this.ExpiresAt;
}
