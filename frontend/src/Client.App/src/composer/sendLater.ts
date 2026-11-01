// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { calendarDayIn, dayFrom, zoneOffsetAt } from '../localization/instants';
import type { Locale } from '../localization/locale';

const aMinute = 60_000;
const aDay = 86_400_000;

// When a message is to leave, as the person named it: a date and a time on the clock of the zone their profile sets,
// rather than an instant. The design draws every time that way — the ready times are *08:00 tomorrow*, the picker is a
// date field and a time field — so a wall-clock reading is what the composer holds, and the instant it names is
// worked out once, in that zone, at the moment the message is handed over.
//
// The zone is the reader's record's, never this device's, so a person travelling still schedules in the zone the rest
// of their mail is read in. `null` is the runtime's own zone, which is what stands until that record has answered.

/** The ready times *Send later* offers, in the order the menu lists them. */
export const readyTimeKeys = ['tomorrowMorning', 'tomorrowAfternoon', 'mondayMorning'] as const;

export type ReadyTimeKey = (typeof readyTimeKeys)[number];

/** A time somebody chose, as their own clock reads it: `yyyy-mm-dd` and `hh:mm`. */
export interface SendTime {
    readonly date: string;
    readonly time: string;
}

/** A time chosen for a message, and which ready time it is where it is one. */
export interface ChosenSendTime extends SendTime {
    readonly readyTime: ReadyTimeKey | null;
}

/** How a time is said: on *Send*, beside a ready time in the menu, or inside a sentence. */
export interface SendTimeWording {
    readonly short: string;
    readonly date: string;
    readonly long: string;
}

/** What a sentence about a time needs from the catalogue, which owns the words around the pieces. */
export type SendTimeSentence = (shape: keyof SendTimeWording, today: boolean, pieces: SendTimePieces) => string;

export interface SendTimePieces {
    readonly weekday: string;
    readonly day: string;
    readonly time: string;
}

/**
 * The ready times as they stand at one instant: 08:00 the next day, 13:00 the next day, and 08:00 the next Monday — a
 * week on from a Monday, so the three never name the same morning twice except on a Sunday, when tomorrow is Monday.
 *
 * @param now The instant the menu opens at, passed rather than read so a test pins it.
 * @param zone The zone the reader's record states, or `null` for the runtime's own.
 */
export function readyTimes(now: number, zone: string | null): Readonly<Record<ReadyTimeKey, SendTime>> {
    const today = calendarDayIn(now, zone);
    const tomorrow = dayFrom(today, 1);
    const weekday = new Date(`${today}T00:00:00Z`).getUTCDay();

    return {
        tomorrowMorning: { date: tomorrow, time: '08:00' },
        tomorrowAfternoon: { date: tomorrow, time: '13:00' },
        mondayMorning: { date: dayFrom(today, (8 - weekday) % 7 || 7), time: '08:00' },
    };
}

/** Which ready time a date and time are, where they are one — a picked time matching one is taken as that one. */
export function readyTimeOf(chosen: SendTime, now: number, zone: string | null): ReadyTimeKey | null {
    const ready = readyTimes(now, zone);

    return readyTimeKeys.find((key) => ready[key].date === chosen.date && ready[key].time === chosen.time) ?? null;
}

/**
 * The instant a time on the zone's clock names, or `null` where it is not a date and a time, or the zone is one this
 * runtime does not know.
 *
 * It answers the two cases a clock creates as the deployment's `ZonedInstant.Resolve` does, so the time a person picked
 * is the one the service would have resolved: a reading the clock shows twice is taken at the earlier of the two, and
 * one it skips over at the instant the gap ends. The offsets a day either side are the two the reading can stand
 * under, since no zone changes its clock twice within one.
 */
export function instantOf(chosen: SendTime, zone: string | null): number | null {
    const wall = Date.parse(`${chosen.date}T${chosen.time}:00Z`);
    const before = Number.isNaN(wall) ? null : zoneOffsetAt(zone, wall - aDay);
    const after = Number.isNaN(wall) ? null : zoneOffsetAt(zone, wall + aDay);

    if (before === null || after === null) {
        return null;
    }

    const readings = [wall - before * aMinute, wall - after * aMinute].filter(
        (at) => zoneOffsetAt(zone, at) === (wall - at) / aMinute,
    );

    if (readings.length > 0) {
        return Math.min(...readings);
    }

    // Skipped over: the clock stood under `before` until the change and under `after` from it, so the change is the
    // first minute between the two readings that already stands under `after`.
    let standsBefore = wall - after * aMinute;
    let standsAfter = wall - before * aMinute;

    while (standsAfter - standsBefore > aMinute) {
        const middle = standsBefore + Math.floor((standsAfter - standsBefore) / 2 / aMinute) * aMinute;

        if (zoneOffsetAt(zone, middle) === after) {
            standsAfter = middle;
        } else {
            standsBefore = middle;
        }
    }

    return standsAfter;
}

/** Whether a time is at or before now, which the deployment refuses and the picker says before it is asked. */
export function hasGone(chosen: SendTime, now: number, zone: string | null): boolean {
    const at = instantOf(chosen, zone);

    return at === null || at <= now;
}

/** The instant a chosen time names while it is still to come, read against the clock now, or `null` once it has gone. */
export function instantStillAhead(chosen: SendTime, zone: string | null): number | null {
    const at = instantOf(chosen, zone);

    return at !== null && at > Date.now() ? at : null;
}

/**
 * A time said the three ways the design says one: *Send Fri 08:00* (*Today 17:00* the same day, *03.09 09:30* a week
 * or more out), *Fri 28.08, 08:00* beside a ready time, and *Fri 28.08 at 08:00* in a sentence.
 *
 * The date and the clock time are already the reader's own, so each piece is formatted as a reading of UTC — which
 * says them back exactly as they were chosen — in the language's own spelling.
 */
export function wordSendTime(
    chosen: SendTime,
    now: number,
    zone: string | null,
    locale: Locale,
    sentence: SendTimeSentence,
): SendTimeWording {
    const day = new Date(`${chosen.date}T00:00:00Z`);
    const daysAway = Math.round((day.getTime() - Date.parse(`${calendarDayIn(now, zone)}T00:00:00Z`)) / aDay);
    const pieces: SendTimePieces = {
        weekday: new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' }).format(day),
        day: new Intl.DateTimeFormat(locale, { day: '2-digit', month: '2-digit', timeZone: 'UTC' }).format(day),
        time: new Intl.DateTimeFormat(locale, { timeStyle: 'short', timeZone: 'UTC' }).format(
            Date.parse(`${chosen.date}T${chosen.time}:00Z`),
        ),
    };
    const today = daysAway === 0;

    return {
        short: sentence('short', today, daysAway > 0 && daysAway < 7 ? pieces : { ...pieces, weekday: pieces.day }),
        date: sentence('date', today, pieces),
        long: sentence('long', today, pieces),
    };
}

/** The zone a time is named in, as the deployment is told it: the record's, or this runtime's where it states none. */
export function namedZone(zone: string | null): string {
    return zone ?? Intl.DateTimeFormat().resolvedOptions().timeZone;
}

/** The zone's offset at an instant as the picker says it, *UTC+02:00*. */
export function wordZoneOffset(zone: string | null, at: number): string {
    const minutes = zoneOffsetAt(zone, at) ?? 0;
    const apart = Math.abs(minutes);
    const hours = String(Math.floor(apart / 60)).padStart(2, '0');
    const rest = String(apart % 60).padStart(2, '0');

    return `UTC${minutes < 0 ? '-' : '+'}${hours}:${rest}`;
}
