// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { weekdays, type ColleagueWorkingHours, type Weekday } from '@mailfathom/client-backend';
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
    readonly yours: ConvertedColleagueHours | null;

    /** Whether it is outside the hours where the colleague is now, or `null` where their zone is not known. */
    readonly outside: boolean | null;
}

/** A colleague's working hours on the reader's clock. */
export interface ConvertedColleagueHours {
    /** The days they start on in the reader's week, or `null` where those are the colleague's own days. */
    readonly days: string | null;

    /** The hours on the reader's clock. */
    readonly hours: string;

    /** Whether the hours run past the reader's midnight, so they end on the day after the one they start on. */
    readonly intoNextDay: boolean;
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
        days: wordDays(hours.days, locale, run),
        own: wordedRun(hours.start, hours.end),
        yours:
            clock === null || clock.minutesAhead === 0
                ? null
                : convert(hours, clock.minutesAhead, wordedRun, (days) => wordDays(days, locale, run)),
        outside: wall === null ? null : !isWithin(hours, wall.weekday, wall.minute),
    };
}

// The hours moved onto the reader's clock, and with them the days, where the move crosses the reader's midnight: nine
// to five in Sydney is three to eleven the evening before in Los Angeles.
function convert(
    hours: ColleagueWorkingHours,
    minutesAhead: number,
    wordHours: (start: number, end: number) => string,
    wordShiftedDays: (days: readonly Weekday[]) => string,
): ConvertedColleagueHours {
    const start = hours.start - minutesAhead;
    const end = hours.end - minutesAhead;
    const shift = Math.floor(start / minutesPerDay);

    return {
        days: shift === 0 ? null : wordShiftedDays(hours.days.map((day) => shiftedDay(day, shift))),
        hours: wordHours(start, end),
        intoNextDay: Math.floor((end - 1) / minutesPerDay) !== shift,
    };
}

function shiftedDay(day: Weekday, shift: number): Weekday {
    const index = (weekdays.indexOf(day) + shift + weekdays.length) % weekdays.length;

    return weekdays[index] ?? day;
}

function isWithin(hours: ColleagueWorkingHours, weekday: number, minute: number): boolean {
    const day = weekdays[(weekday + 6) % 7];

    return day !== undefined && hours.days.includes(day) && hours.start <= minute && minute < hours.end;
}

// The days as runs of consecutive weekdays, Monday first unless a run carries on over the weekend — *Sun–Thu* rather than
// *Mon–Thu, Sun*: a run of three or more is said from its first to its last.
function wordDays(days: readonly Weekday[], locale: Locale, run: (first: string, last: string) => string): string {
    const format = new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' });
    const dayName = (index: number): string =>
        format.format(firstMonday + (index % weekdays.length) * minutesPerDay * 60_000);
    const worked = (index: number): boolean => {
        const day = weekdays[index % weekdays.length];

        return day !== undefined && days.includes(day);
    };
    const opening = weekdays.findIndex((_, index) => worked(index) && !worked(index + weekdays.length - 1));
    const first = opening === -1 ? 0 : opening;
    const indices = weekdays.flatMap((_, offset) => (worked(first + offset) ? [first + offset] : []));
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
