// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Domain.Presence;

/// <summary>Resolves a <see cref="PresenceDuration" /> to the instant it runs out, which a chosen state, a status message, and a work mode for today share.</summary>
internal static class PresenceDurations
{
    /// <summary>Reads the instant a while ends, in the member's own time zone where it is a period of the calendar.</summary>
    /// <param name="duration">The while.</param>
    /// <param name="madeAt">When it was chosen.</param>
    /// <param name="zone">The member's own time zone, which is where <em>today</em> and <em>this week</em> end.</param>
    /// <returns>The instant the while runs out.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="duration" /> names no while.</exception>
    internal static DateTimeOffset EndOf(PresenceDuration duration, DateTimeOffset madeAt, UserTimeZone zone) =>
        duration switch
        {
            PresenceDuration.ThirtyMinutes => madeAt.AddMinutes(30),
            PresenceDuration.OneHour => madeAt.AddHours(1),
            PresenceDuration.TwoHours => madeAt.AddHours(2),
            PresenceDuration.Today => LocalMidnight(zone.Reading(madeAt).Date.AddDays(1), zone),
            PresenceDuration.ThisWeek => LocalMidnight(zone.Reading(madeAt).Date.AddDays(DaysToMonday(zone.Reading(madeAt).DayOfWeek)), zone),
            _ => throw new ArgumentOutOfRangeException(nameof(duration), duration, "The duration is not one a choice can last."),
        };

    private static int DaysToMonday(DayOfWeek day) => day == DayOfWeek.Sunday ? 1 : 8 - (int)day;

    /// <summary>Reads the instant a local midnight falls at, moving past a midnight that a daylight-saving change skips.</summary>
    /// <remarks>Some zones move their clocks at midnight itself, so the wall-clock instant can be one that never happens; the day then ends at the first local time that does.</remarks>
    private static DateTimeOffset LocalMidnight(DateTime localDate, UserTimeZone zone)
    {
        var local = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);

        while (zone.Zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone.Zone), TimeSpan.Zero);
    }
}
