// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { weekdays, type ColleagueWorkingHours } from '@mailfathom/client-backend';
import { wordInstant } from '../localization/instants';
import type { Locale } from '../localization/locale';
import { colleagueClock, wallClockAt } from './colleagueClock';

// A colleague's working hours as the card beside their contact words them: the days and hours on their own clock —
// *Mon–Fri 9:00–17:00* — the same hours on the reader's where the two clocks differ, and whether it is outside them
// where they are now. The hours carry no zone of their own; they are always read in the zone the colleague states, so
// without one nothing can be converted and nobody can say whether it is outside them.

/** Working hours worded for the card. */
export interface WordedColleagueHours {
    /** The days worked, a run of three or more consecutive days folded into one: *Mon–Fri*, *Mon, Wed*. */
    readonly days: string;

    /** The hours on the colleague's own clock. */
    readonly own: string;

    /** The same hours on the reader's clock, or `null` where the two clocks read alike or the zone is not known. */
    readonly yours: string | null;

    /** Whether it is outside the hours where the colleague is now, or `null` where their zone is not known. */
    readonly outside: boolean | null;
}

const minutesPerDay = 24 * 60;

// 1 January 2024 was a Monday, so the n-th day after it is the n-th entry of `weekdays`.
const firstMonday = Date.UTC(2024, 0, 1);

/**
 * Words a colleague's working hours, converting them to the reader's clock where the colleague's zone is known.
 *
 * @param hours The hours, on the colleague's own clock.
 * @param zone The IANA zone the colleague states, or `null` where they state none.
 * @param readerZone The zone the reader's record states, or `null` for this runtime's own.
 * @param now The instant it is read at, passed rather than read so a test pins it.
 * @param run Words a run between two worded ends — two days or two times — as the language joins one.
 */
export function wordColleagueHours(
    hours: ColleagueWorkingHours,
    zone: string | null,
    readerZone: string | null,
    now: number,
    locale: Locale,
    run: (first: string, last: string) => string,
): WordedColleagueHours {
    const clock = zone === null ? null : colleagueClock(zone, readerZone, now, locale);
    const wall = zone === null || clock === null ? null : wallClockAt(zone, now);
    const wordedRun = (start: number, end: number): string => run(wordTime(start, locale), wordTime(end, locale));

    return {
        days: wordDays(hours, locale, run),
        own: wordedRun(hours.start, hours.end),
        yours:
            clock === null || clock.minutesAhead === 0
                ? null
                : wordedRun(hours.start - clock.minutesAhead, hours.end - clock.minutesAhead),
        outside: wall === null ? null : !isWithin(hours, wall.weekday, wall.minute),
    };
}

function isWithin(hours: ColleagueWorkingHours, weekday: number, minute: number): boolean {
    const day = weekdays[(weekday + 6) % 7];

    return day !== undefined && hours.days.includes(day) && hours.start <= minute && minute < hours.end;
}

// The days as runs of consecutive weekdays, Monday first: a run of three or more is said from its first to its last.
function wordDays(hours: ColleagueWorkingHours, locale: Locale, run: (first: string, last: string) => string): string {
    const format = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' });
    const dayName = (index: number): string => format.format(firstMonday + index * minutesPerDay * 60_000);
    const indices = weekdays.flatMap((weekday, index) => (hours.days.includes(weekday) ? [index] : []));
    const runs = indices.reduce<number[][]>((found, index) => {
        const last = found.at(-1);

        if (last?.at(-1) === index - 1) {
            last.push(index);
        } else {
            found.push([index]);
        }

        return found;
    }, []);
    const worded = runs.flatMap((days) =>
        days.length >= 3 ? [run(dayName(days[0] ?? 0), dayName(days.at(-1) ?? 0))] : days.map(dayName),
    );

    return new Intl.ListFormat(locale, { type: 'unit', style: 'short' }).format(worded);
}

// A time of day, wrapped onto the clock, spelled as every other screen spells a clock time.
function wordTime(minute: number, locale: Locale): string {
    const onTheClock = ((minute % minutesPerDay) + minutesPerDay) % minutesPerDay;

    return wordInstant(new Date(firstMonday + onTheClock * 60_000).toISOString(), locale, 'time', 'UTC') ?? '';
}
