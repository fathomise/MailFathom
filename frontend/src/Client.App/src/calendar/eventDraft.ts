// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { CalendarEvent, CalendarEventAmendment } from '@mailfathom/client-backend';

// What somebody has typed into an event, and the two directions it travels: out of an event the deployment holds, and
// into the record a write states. Both dialogs read it — writing an event down and amending one are the same fields —
// so it is stated once rather than twice with a difference nobody meant.
//
// **A day and a time are what a person picks, and an instant is what the deployment holds.** The platform's own date
// and time fields answer `2026-09-24` and `09:00`, which are a day and a clock reading rather than moments, so they
// are resolved here against the reader's own zone — never against UTC, which would put every reader west of Greenwich
// on the day before the one they picked.
//
// **An event that is days rather than hours ends where its last day closes**, which is the midnight after it: that is
// how a calendar file states one, and it is what lets a day run into the next without a clock reading inventing an
// hour nobody chose. A single day states no end at all, as it always has.

/** What a person has typed into an event, in the shape the fields on the screen hold it. */
export interface EventDraft {
    readonly title: string;

    /** What the event is about, as it is being typed, which is empty where nothing is written. */
    readonly notes: string;

    /** The day it begins on, as a date field answers one: `yyyy-mm-dd`. */
    readonly startDay: string;

    /** When it begins, as a time field answers one: `hh:mm`. */
    readonly start: string;

    /** The day it ends on, in the same form — for a day, the last day it covers. */
    readonly endDay: string;

    /** When it ends, in the same form, or empty to state no end. */
    readonly end: string;

    /** Whether it is stated as a day rather than as a clock time, which is what puts the two time fields away. */
    readonly allDay: boolean;

    /** What is to announce it, in minutes before it begins. */
    readonly reminders: readonly number[];
}

/** How long a draft lasts, in the unit it was stated in, which is what the form says under its fields. */
export type DraftedLength =
    { readonly unit: 'day'; readonly count: number } | { readonly unit: 'minute'; readonly count: number };

/** An event with nothing typed into it yet, which is what the new-event dialog opens on. */
export const nothingDrafted: EventDraft = {
    title: '',
    notes: '',
    startDay: '',
    start: '',
    endDay: '',
    end: '',
    allDay: false,
    reminders: [],
};

const millisecondsInADay = 24 * 60 * 60 * 1000;

/** The event the deployment holds, read back into the fields that would have produced it. */
export function draftOf(event: CalendarEvent): EventDraft {
    const begins = new Date(event.start);
    const ends = event.end === null ? null : new Date(event.end);
    const kept = { title: event.title, notes: event.notes ?? '', allDay: event.isAllDay, reminders: event.reminders };

    // An event whose beginning cannot be read leaves every time empty rather than only that one: an end standing in a
    // form with no day and no beginning is a time that means nothing, and a reader would have to clear it themselves
    // before the form could be saved at all.
    if (Number.isNaN(begins.getTime())) {
        return { ...nothingDrafted, ...kept };
    }

    const startDay = writtenDay(begins);
    const endsReadably = ends !== null && !Number.isNaN(ends.getTime());

    // A day carries no clock reading, so the two time fields are empty rather than holding the midnight its instant
    // was written at — a reader turning the day off is picking a time rather than correcting one this form invented.
    if (event.isAllDay) {
        return {
            ...nothingDrafted,
            ...kept,
            startDay,
            endDay: endsReadably ? writtenDay(new Date(ends.getTime() - 1)) : startDay,
        };
    }

    return {
        ...kept,
        startDay,
        start: writtenTime(begins),
        endDay: endsReadably ? writtenDay(ends) : startDay,
        end: endsReadably ? writtenTime(ends) : '',
    };
}

/** The day a date field opens on where the reader is standing on that day, which is what the new-event dialog uses. */
export function draftOn(day: Date): EventDraft {
    const written = writtenDay(day);

    return { ...nothingDrafted, startDay: written, endDay: written };
}

/**
 * The record a write would state, or `null` where the draft is not one yet.
 *
 * Four things make it `null`, and each is something the screen can see for itself: a title nobody typed, a day or a
 * beginning nobody picked, an end that is not after the beginning, and a later last day with no time to end on it. Everything else the deployment judges is
 * reported as an outcome rather than refused here, because a second copy of a rule is how two of them come to
 * disagree.
 */
export function recordOf(draft: EventDraft): CalendarEventAmendment | null {
    const title = draft.title.trim();
    const notes = draft.notes.trim() === '' ? null : draft.notes.trim();
    const stated = { title, isAllDay: draft.allDay, reminders: draft.reminders, notes };

    if (title === '' || draft.startDay === '' || endsBeforeItBegins(draft) || wantsAnEndTime(draft)) {
        return null;
    }

    if (draft.allDay) {
        const opens = new Date(`${draft.startDay}T00:00`);
        const lastDay = draft.endDay === '' ? draft.startDay : draft.endDay;
        const closes = new Date(`${lastDay}T00:00`);

        if (Number.isNaN(opens.getTime()) || Number.isNaN(closes.getTime())) {
            return null;
        }

        return {
            ...stated,
            start: opens.toISOString(),
            end: lastDay === draft.startDay ? null : dayAfter(closes).toISOString(),
        };
    }

    const begins = beginningOf(draft);

    if (begins === null) {
        return null;
    }

    if (draft.end === '') {
        return { ...stated, start: begins.toISOString(), end: null };
    }

    const ends = endingOf(draft);

    return ends === null ? null : { ...stated, start: begins.toISOString(), end: ends.toISOString() };
}

/** Whether the draft states an end that does not come after its beginning, which the form says rather than hides. */
export function endsBeforeItBegins(draft: EventDraft): boolean {
    if (draft.allDay) {
        return draft.endDay !== '' && draft.startDay !== '' && draft.endDay < draft.startDay;
    }

    const begins = beginningOf(draft);
    const ends = endingOf(draft);

    return begins !== null && ends !== null && ends.getTime() <= begins.getTime();
}

/**
 * Whether a timed draft ends on a later day with no time to end at, which the form says rather than saving the event as
 * a moment and dropping the day somebody picked.
 */
export function wantsAnEndTime(draft: EventDraft): boolean {
    return !draft.allDay && draft.end === '' && draft.endDay !== '' && draft.endDay !== draft.startDay;
}

/** How long the draft lasts, or `null` where it states no length the form could word. */
export function lengthOf(draft: EventDraft): DraftedLength | null {
    if (endsBeforeItBegins(draft)) {
        return null;
    }

    if (draft.allDay) {
        const first = Date.parse(draft.startDay);
        const last = Date.parse(draft.endDay === '' ? draft.startDay : draft.endDay);

        return Number.isNaN(first) || Number.isNaN(last)
            ? null
            : { unit: 'day', count: Math.round((last - first) / millisecondsInADay) + 1 };
    }

    const begins = beginningOf(draft);
    const ends = endingOf(draft);

    return begins === null || ends === null
        ? null
        : { unit: 'minute', count: Math.round((ends.getTime() - begins.getTime()) / 60_000) };
}

/** The draft an answered description leaves behind, which is the deployment's reading put into the fields to edit. */
export function draftedFrom(
    drafted: { readonly title: string | null; readonly start: string | null; readonly end: string | null },
    standing: EventDraft,
): EventDraft {
    const begins = readable(drafted.start);
    const ends = readable(drafted.end);

    return {
        ...standing,
        title: drafted.title ?? standing.title,
        startDay: begins === null ? standing.startDay : writtenDay(begins),
        start: begins === null ? standing.start : writtenTime(begins),
        endDay: ends !== null ? writtenDay(ends) : begins !== null ? writtenDay(begins) : standing.endDay,
        end: ends === null ? standing.end : writtenTime(ends),
    };
}

function beginningOf(draft: EventDraft): Date | null {
    return draft.startDay === '' || draft.start === '' ? null : readable(`${draft.startDay}T${draft.start}`);
}

function endingOf(draft: EventDraft): Date | null {
    return draft.end === '' ? null : readable(`${draft.endDay === '' ? draft.startDay : draft.endDay}T${draft.end}`);
}

function readable(instant: string | null): Date | null {
    if (instant === null) {
        return null;
    }

    const read = new Date(instant);

    return Number.isNaN(read.getTime()) ? null : read;
}

function dayAfter(day: Date): Date {
    return new Date(day.getFullYear(), day.getMonth(), day.getDate() + 1);
}

function writtenDay(at: Date): string {
    return `${padded(at.getFullYear(), 4)}-${padded(at.getMonth() + 1, 2)}-${padded(at.getDate(), 2)}`;
}

function writtenTime(at: Date): string {
    return `${padded(at.getHours(), 2)}:${padded(at.getMinutes(), 2)}`;
}

function padded(value: number, places: number): string {
    return value.toFixed(0).padStart(places, '0');
}
