// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Scheduling;

namespace MailFathom.Application.Agent.Conversations;

/// <summary>The time a proposed message is to leave at, which the person asked for, together with the zone they asked in.</summary>
/// <param name="Instant">When it leaves.</param>
/// <param name="ZoneId">The IANA zone the person's profile reads their days in, which is where the time was named.</param>
/// <remarks>
/// Carried as its two parts rather than as a <see cref="ZonedInstant" />, because a stored act is read back by a
/// serializer that cannot construct that type, for the reason <see cref="AgentMessageSending" /> carries its account as
/// text.
/// </remarks>
public sealed record AgentSendTime(DateTimeOffset Instant, string ZoneId)
{
    /// <summary>Reads the time as the due time a send is held until.</summary>
    /// <returns>The instant and its zone.</returns>
    /// <exception cref="ArgumentException">Thrown when the zone identifier is blank or longer than <see cref="ZonedInstant.MaximumZoneIdLength" />.</exception>
    public ZonedInstant ToDueTime() => ZonedInstant.Restore(this.Instant, this.ZoneId);
}
