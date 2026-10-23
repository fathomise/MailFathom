// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type { CalendarEvent } from '@mailfathom/client-backend';
import { draftedFrom, draftOf, draftOn, endsBeforeItBegins, lengthOf, nothingDrafted, recordOf } from './eventDraft';

// A day and a clock reading are resolved against the reader's own zone, so the zone is pinned: in UTC the whole of
// this module is the identity function and a test run there would prove none of it.
const zoneBefore = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

const meeting: CalendarEvent = {
    id: 'meeting',
    title: 'Review with Anna',
    start: '2026-09-24T09:00:00+02:00',
    end: '2026-09-24T10:30:00+02:00',
    isAllDay: false,
    reminders: [],
    notes: null,
    remindsAt: [],
    origin: 'Asserted',
    sourceMessage: null,
    recordedAt: '2026-09-01T09:00:00+02:00',
    amendedAt: '2026-09-01T09:00:00+02:00',
};

describe('draftOf', () => {
    it('reads an event back into the fields that would have produced it', () => {
        expect(draftOf({ ...meeting, notes: 'Bring the addendum.' })).toEqual({
            title: 'Review with Anna',
            notes: 'Bring the addendum.',
            startDay: '2026-09-24',
            start: '09:00',
            endDay: '2026-09-24',
            end: '10:30',
            allDay: false,
            reminders: [],
        });
    });

    it('reads a day back with no clock reading, because a day carries none', () => {
        const day = { ...meeting, start: '2026-09-24T00:00:00+02:00', end: null, isAllDay: true, reminders: [60] };

        expect(draftOf(day)).toEqual({
            ...nothingDrafted,
            title: 'Review with Anna',
            startDay: '2026-09-24',
            endDay: '2026-09-24',
            allDay: true,
            reminders: [60],
        });
    });

    it('reads days that end where their last day closes back as the last day they cover', () => {
        const days = {
            ...meeting,
            start: '2026-09-24T00:00:00+02:00',
            end: '2026-09-27T00:00:00+02:00',
            isAllDay: true,
        };

        expect(draftOf(days)).toMatchObject({ startDay: '2026-09-24', endDay: '2026-09-26' });
    });

    it('reads an event running into another day with the day it ends on', () => {
        expect(draftOf({ ...meeting, end: '2026-09-25T01:00:00+02:00' })).toMatchObject({
            endDay: '2026-09-25',
            end: '01:00',
        });
    });

    it('reads an instant sent in another offset against the reader"s own day', () => {
        // Twenty-two o'clock UTC is midnight in Warsaw, so the day somebody sees is the twenty-fifth.
        expect(draftOf({ ...meeting, start: '2026-09-24T22:30:00Z', end: null }).startDay).toBe('2026-09-25');
    });

    it('states no end as an empty field rather than as a time', () => {
        expect(draftOf({ ...meeting, end: null })).toMatchObject({ endDay: '2026-09-24', end: '' });
    });

    it('empties the fields an unreadable instant would otherwise fill with nonsense', () => {
        expect(draftOf({ ...meeting, start: 'whenever' })).toEqual({
            ...nothingDrafted,
            title: 'Review with Anna',
        });
    });

    it('reads back what announces it, which is what an amendment then carries', () => {
        expect(draftOf({ ...meeting, reminders: [15] }).reminders).toEqual([15]);
    });
});

describe('draftOn', () => {
    it('opens on the day the reader is standing on and fills nothing else', () => {
        expect(draftOn(new Date(2026, 8, 24, 15))).toEqual({
            ...nothingDrafted,
            startDay: '2026-09-24',
            endDay: '2026-09-24',
        });
    });
});

describe('recordOf', () => {
    const filled = {
        ...nothingDrafted,
        title: 'Review with Anna',
        startDay: '2026-09-24',
        start: '09:00',
        endDay: '2026-09-24',
        end: '10:30',
    };

    it('resolves the days and the clock readings against the reader"s own zone', () => {
        expect(recordOf(filled)).toEqual({
            title: 'Review with Anna',
            start: new Date(2026, 8, 24, 9).toISOString(),
            end: new Date(2026, 8, 24, 10, 30).toISOString(),
            isAllDay: false,
            reminders: [],
            notes: null,
        });
    });

    it('ends on the day the end was picked on, so an event can run across days', () => {
        expect(recordOf({ ...filled, endDay: '2026-09-26', end: '08:00' })?.end).toBe(
            new Date(2026, 8, 26, 8).toISOString(),
        );
    });

    it('takes the spaces off a title and the notes rather than writing them down', () => {
        expect(
            recordOf({ ...filled, title: '  Review with Anna  ', notes: '  Room 4\nBring the copy  ' }),
        ).toMatchObject({ title: 'Review with Anna', notes: 'Room 4\nBring the copy' });
    });

    it('states no notes where only spaces were typed', () => {
        expect(recordOf({ ...filled, notes: '   ' })?.notes).toBeNull();
    });

    it('states no end where none was picked', () => {
        expect(recordOf({ ...filled, end: '' })?.end).toBeNull();
    });

    it('writes a day down as midnight in the reader"s own zone and states that it is a day', () => {
        expect(recordOf({ ...filled, allDay: true, start: '', end: '' })).toEqual({
            title: 'Review with Anna',
            start: new Date(2026, 8, 24).toISOString(),
            end: null,
            isAllDay: true,
            reminders: [],
            notes: null,
        });
    });

    it('writes several days down as ending where the last of them closes', () => {
        expect(recordOf({ ...filled, allDay: true, start: '', end: '', endDay: '2026-09-26' })).toMatchObject({
            start: new Date(2026, 8, 24).toISOString(),
            end: new Date(2026, 8, 27).toISOString(),
            isAllDay: true,
        });
    });

    it('carries what is to announce it rather than dropping it on the way to the record', () => {
        expect(recordOf({ ...filled, reminders: [15, 60] })?.reminders).toEqual([15, 60]);
    });

    it.each([
        ['a title nobody typed', { ...filled, title: '   ' }],
        ['a day nobody picked', { ...filled, startDay: '' }],
        ['a beginning nobody picked', { ...filled, start: '' }],
        ['a day that is not one', { ...filled, startDay: '2026-02-31x' }],
        ['an end before the beginning', { ...filled, end: '08:00' }],
        ['an end at the beginning', { ...filled, end: '09:00' }],
        ['an end on a day before the beginning', { ...filled, endDay: '2026-09-23', end: '11:00' }],
        ['a last day before the first', { ...filled, allDay: true, endDay: '2026-09-23' }],
    ])('refuses %s', (_, draft) => {
        expect(recordOf(draft)).toBeNull();
    });
});

describe('endsBeforeItBegins', () => {
    const filled = { ...nothingDrafted, title: 'Call', startDay: '2026-09-24', start: '09:00', endDay: '2026-09-24' };

    it('says so for an end at or before the beginning, which is what the form puts in words', () => {
        expect(endsBeforeItBegins({ ...filled, end: '08:00' })).toBe(true);
        expect(endsBeforeItBegins({ ...filled, end: '09:00' })).toBe(true);
    });

    it('says nothing about a draft that states no end yet', () => {
        expect(endsBeforeItBegins(filled)).toBe(false);
    });

    it('says so for a last day before the first', () => {
        expect(endsBeforeItBegins({ ...filled, allDay: true, endDay: '2026-09-22' })).toBe(true);
    });
});

describe('lengthOf', () => {
    const filled = { ...nothingDrafted, title: 'Call', startDay: '2026-09-24', start: '09:00', endDay: '2026-09-24' };

    it('counts the minutes between a beginning and an end', () => {
        expect(lengthOf({ ...filled, end: '09:45' })).toEqual({ unit: 'minute', count: 45 });
    });

    it('counts the days a day-long event covers, its first and last included', () => {
        expect(lengthOf({ ...filled, allDay: true, start: '', endDay: '2026-09-26' })).toEqual({
            unit: 'day',
            count: 3,
        });
    });

    it('states nothing for an event with no end or one that ends before it begins', () => {
        expect(lengthOf(filled)).toBeNull();
        expect(lengthOf({ ...filled, end: '08:00' })).toBeNull();
    });
});

describe('draftedFrom', () => {
    const standing = {
        ...nothingDrafted,
        title: 'Typed by hand',
        startDay: '2026-09-24',
        start: '08:00',
        endDay: '2026-09-24',
        end: '',
    };

    it('puts what the deployment read into the fields', () => {
        expect(
            draftedFrom(
                { title: 'Lunch with Anna', start: '2026-09-25T13:00:00+02:00', end: '2026-09-25T14:00:00+02:00' },
                standing,
            ),
        ).toEqual({
            ...standing,
            title: 'Lunch with Anna',
            startDay: '2026-09-25',
            start: '13:00',
            endDay: '2026-09-25',
            end: '14:00',
        });
    });

    it('moves the last day with the first where the reading named no end', () => {
        expect(draftedFrom({ title: null, start: '2026-09-27T10:00:00+02:00', end: null }, standing)).toMatchObject({
            startDay: '2026-09-27',
            endDay: '2026-09-27',
        });
    });

    it('leaves a field the deployment said nothing about as the person left it', () => {
        expect(draftedFrom({ title: null, start: null, end: null }, standing)).toEqual(standing);
    });

    it('leaves a field an unreadable instant would otherwise empty', () => {
        expect(draftedFrom({ title: 'Lunch', start: 'whenever', end: null }, standing)).toEqual({
            ...standing,
            title: 'Lunch',
        });
    });
});
