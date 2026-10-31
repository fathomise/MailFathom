// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Presence;

namespace MailFathom.Application.Presence;

/// <summary>Decides when this process next removes expired client rows: at most once per client lifetime.</summary>
/// <remarks>
/// One process's schedule, and deliberately so: the sweep is housekeeping that a row's own expiry already makes
/// unnecessary for correctness, so several replicas sweeping in the same minute cost a few empty deletes and nothing
/// else. Registered as a singleton, because a schedule per request would sweep on every renewal.
/// </remarks>
public sealed class PresenceSweepSchedule
{
    private long nextSweepTicks;

    /// <summary>Claims the sweep when it is due, and moves the next one a client lifetime later.</summary>
    /// <param name="now">The time of the renewal asking.</param>
    /// <returns><see langword="true" /> for exactly one caller once the sweep is due.</returns>
    public bool TryClaimAt(DateTimeOffset now)
    {
        var due = Interlocked.Read(ref this.nextSweepTicks);

        return now.UtcTicks >= due
            && Interlocked.CompareExchange(ref this.nextSweepTicks, (now + MemberPresence.ClientLifetime).UtcTicks, due) == due;
    }
}
