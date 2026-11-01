// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
    hasGone,
    instantOf,
    instantStillAhead,
    readyTimeOf,
    readyTimes,
    wordSendTime,
    wordZoneOffset,
    type SendTimeSentence,
} from './sendLater';

// Thursday 27 August 2026 at 10:47 in Warsaw, which is the moment the design's own sample stands at.
const thursdayMorning = Date.parse('2026-08-27T08:47:00Z');

const said: SendTimeSentence = (shape, today, { weekday, day, time }) =>
    `${shape}:${today ? 'today' : `${weekday}|${day}`}|${time}`;

describe('readyTimes', () => {
    it('resolves tomorrow morning, tomorrow afternoon and Monday morning on the clock of the reader’s zone', () => {
        expect(readyTimes(thursdayMorning, 'Europe/Warsaw')).toStrictEqual({
            tomorrowMorning: { date: '2026-08-28', time: '08:00' },
            tomorrowAfternoon: { date: '2026-08-28', time: '13:00' },
            mondayMorning: { date: '2026-08-31', time: '08:00' },
        });
    });

    it('reads which day it is in the reader’s zone rather than in UTC', () => {
        // 23:30 on Thursday in UTC is already Friday in Warsaw, so tomorrow is Saturday.
        const ready = readyTimes(Date.parse('2026-08-27T23:30:00Z'), 'Europe/Warsaw');

        expect(ready.tomorrowMorning.date).toBe('2026-08-29');
    });

    it.each([
        { today: '2026-08-31T08:00:00Z', monday: '2026-09-07' },
        { today: '2026-08-30T08:00:00Z', monday: '2026-08-31' },
    ])('takes the next Monday from $today, a week on from a Monday itself', ({ today, monday }) => {
        expect(readyTimes(Date.parse(today), 'Europe/Warsaw').mondayMorning.date).toBe(monday);
    });

    it('names a picked time matching a ready time as that ready time, and any other as none', () => {
        expect(readyTimeOf({ date: '2026-08-28', time: '13:00' }, thursdayMorning, 'Europe/Warsaw')).toBe(
            'tomorrowAfternoon',
        );
        expect(readyTimeOf({ date: '2026-08-28', time: '13:30' }, thursdayMorning, 'Europe/Warsaw')).toBeNull();
    });
});

describe('instantOf', () => {
    it('reads a time on the zone’s clock as the instant it names there', () => {
        expect(new Date(instantOf({ date: '2026-08-28', time: '08:00' }, 'Europe/Warsaw') ?? 0).toISOString()).toBe(
            '2026-08-28T06:00:00.000Z',
        );
        expect(new Date(instantOf({ date: '2026-12-28', time: '08:00' }, 'Europe/Warsaw') ?? 0).toISOString()).toBe(
            '2026-12-28T07:00:00.000Z',
        );
    });

    it('lands a time the clock skips over after the gap', () => {
        expect(new Date(instantOf({ date: '2026-03-29', time: '02:30' }, 'Europe/Warsaw') ?? 0).toISOString()).toBe(
            '2026-03-29T01:30:00.000Z',
        );
    });

    it('answers nothing for a date or a time that is not one', () => {
        expect(instantOf({ date: '', time: '08:00' }, 'Europe/Warsaw')).toBeNull();
    });
});

describe('hasGone', () => {
    it('holds a time at or before now as gone and a time still to come as not', () => {
        expect(hasGone({ date: '2026-08-27', time: '10:47' }, thursdayMorning, 'Europe/Warsaw')).toBe(true);
        expect(hasGone({ date: '2026-08-27', time: '09:00' }, thursdayMorning, 'Europe/Warsaw')).toBe(true);
        expect(hasGone({ date: '2026-08-27', time: '10:48' }, thursdayMorning, 'Europe/Warsaw')).toBe(false);
    });
});

describe('instantStillAhead', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(thursdayMorning);
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('answers the instant of a time still to come, and nothing once the clock has reached it', () => {
        expect(instantStillAhead({ date: '2026-08-27', time: '10:48' }, 'Europe/Warsaw')).toBe(
            Date.parse('2026-08-27T08:48:00Z'),
        );
        expect(instantStillAhead({ date: '2026-08-27', time: '10:47' }, 'Europe/Warsaw')).toBeNull();
    });
});

describe('wordSendTime', () => {
    it('says a time this week by its weekday on Send, and one a week or more out by its date', () => {
        expect(wordSendTime({ date: '2026-08-28', time: '08:00' }, thursdayMorning, 'Europe/Warsaw', 'pl', said)).toStrictEqual({
            short: 'short:pt.|28.08|08:00',
            date: 'date:pt.|28.08|08:00',
            long: 'long:pt.|28.08|08:00',
        });
        expect(
            wordSendTime({ date: '2026-09-03', time: '09:30' }, thursdayMorning, 'Europe/Warsaw', 'pl', said).short,
        ).toBe('short:03.09|03.09|09:30');
    });

    it('says a time later today as today', () => {
        expect(
            wordSendTime({ date: '2026-08-27', time: '17:00' }, thursdayMorning, 'Europe/Warsaw', 'en', said).short,
        ).toBe('short:today|05:00 PM');
    });
});

describe('wordZoneOffset', () => {
    it('says how far the zone stands from UTC at the time named', () => {
        expect(wordZoneOffset('Europe/Warsaw', thursdayMorning)).toBe('UTC+02:00');
        expect(wordZoneOffset('America/St_Johns', thursdayMorning)).toBe('UTC-02:30');
    });
});
