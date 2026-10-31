// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { wordInstant } from '../localization/instants';
import type { Locale } from '../localization/locale';

// What a colleague's clock reads now, and how far it stands from the reader's: *09:41, 1 h behind you*. The member
// states the zone they live in and the reader's own record states theirs, so both are read off the same instant by
// `Intl` — the one place a zone's offset is known, daylight saving included — and nothing here keeps a table of zones.
//
// This is the one wording that places an instant in somebody else's zone on purpose, since what it says is the time
// where they are. It still reaches the time through `localization/instants.ts`, so the spelling of a clock time is the
// one every other screen uses.

/** A colleague's clock: the time it reads, and how many minutes it stands ahead of the reader's — behind where negative. */
export interface ColleagueClock {
    readonly time: string;
    readonly minutesAhead: number;
}

/**
 * The colleague's local time at an instant and its offset from the reader's, or `null` where this runtime does not know
 * the zone the colleague stated — a time read in the wrong zone being worse than none.
 *
 * @param zone The IANA zone the colleague states.
 * @param readerZone The zone the reader's record states, or `null` for this runtime's own.
 * @param now The instant it is read at, passed rather than read so a test pins it.
 */
export function colleagueClock(
    zone: string,
    readerZone: string | null,
    now: number,
    locale: Locale,
): ColleagueClock | null {
    const theirs = offsetAt(zone, now);
    const yours = offsetAt(readerZone, now);
    const time = wordInstant(new Date(now).toISOString(), locale, 'time', zone);

    return theirs === null || yours === null || time === null ? null : { time, minutesAhead: theirs - yours };
}

/** How far apart two clocks stand, as the language writes a short span: *1 h*, *5 h 30 min*. */
export function wordClockDifference(minutes: number, locale: Locale): string {
    const apart = Math.abs(minutes);
    const unit = (name: 'hour' | 'minute', amount: number): string =>
        new Intl.NumberFormat(locale, { style: 'unit', unit: name, unitDisplay: 'narrow' }).format(amount);

    if (apart % 60 === 0) {
        return unit('hour', apart / 60);
    }

    if (apart < 60) {
        return unit('minute', apart);
    }

    return new Intl.ListFormat(locale, { type: 'unit', style: 'narrow' }).format([
        unit('hour', Math.floor(apart / 60)),
        unit('minute', apart % 60),
    ]);
}

// The zone's offset from UTC at an instant, in minutes: the wall clock it reads there, taken as if it were UTC, less the
// instant itself. Read in a fixed numbering because these are numbers to compute with rather than words to show.
function offsetAt(zone: string | null, at: number): number | null {
    const fields: Intl.DateTimeFormatOptions = {
        hourCycle: 'h23',
        year: 'numeric',
        month: 'numeric',
        day: 'numeric',
        hour: 'numeric',
        minute: 'numeric',
    };

    let parts: Intl.DateTimeFormatPart[];

    try {
        parts = new Intl.DateTimeFormat('en-US', zone === null ? fields : { ...fields, timeZone: zone }).formatToParts(at);
    } catch {
        return null;
    }

    const field = (type: Intl.DateTimeFormatPartTypes): number =>
        Number(parts.find((part) => part.type === type)?.value);
    const wall = Date.UTC(field('year'), field('month') - 1, field('day'), field('hour'), field('minute'));
    const minute = Math.floor(at / 60_000) * 60_000;

    return Number.isNaN(wall) ? null : Math.round((wall - minute) / 60_000);
}
