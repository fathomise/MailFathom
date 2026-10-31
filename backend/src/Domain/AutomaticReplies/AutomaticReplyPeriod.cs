// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

namespace MailFathom.Domain.AutomaticReplies;

/// <summary>When a user's automatic reply answers, without what it answers with or where — the part of it a colleague may be told.</summary>
/// <param name="StartsAt">The instant answering starts.</param>
/// <param name="EndsAt">The instant answering stops, which is after <paramref name="StartsAt" />.</param>
/// <param name="IsOn">Whether the reply is switched on; one switched off covers no instant whatever the period says.</param>
/// <remarks>The period is half-open, answering from <paramref name="StartsAt" /> up to but not at <paramref name="EndsAt" />.</remarks>
public sealed record AutomaticReplyPeriod(DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool IsOn)
{
    /// <summary>Answers whether the reply answers at an instant.</summary>
    /// <param name="at">The instant.</param>
    /// <returns><see langword="true" /> while the reply is switched on and in its period.</returns>
    public bool Covers(DateTimeOffset at) => this.IsOn && this.StartsAt <= at && at < this.EndsAt;

    /// <summary>Answers until when the user is out of office, as their colleagues are told it.</summary>
    /// <param name="at">The instant the colleague asks at.</param>
    /// <returns>The end of the period while the reply covers <paramref name="at" />, and <see langword="null" /> otherwise.</returns>
    public DateTimeOffset? OutOfOfficeUntil(DateTimeOffset at) => this.Covers(at) ? this.EndsAt : null;
}
