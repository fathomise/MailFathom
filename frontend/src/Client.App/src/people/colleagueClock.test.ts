// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { colleagueClock, wordClockDifference } from './colleagueClock';

// After the clocks went back in Europe and before they went back in North America, which is the week a table of fixed
// offsets would get wrong.
const autumn = Date.parse('2026-10-31T08:41:00Z');
const summer = Date.parse('2026-07-15T08:41:00Z');

describe('colleagueClock', () => {
    it("reads the colleague's own clock and stands it behind the reader's by the zones' offsets at that instant", () => {
        expect(colleagueClock('Europe/London', 'Europe/Warsaw', autumn, 'en')).toEqual({
            time: '8:41 AM',
            minutesAhead: -60,
        });
    });

    it('follows daylight saving in each zone rather than a fixed offset', () => {
        expect(colleagueClock('America/New_York', 'Europe/Warsaw', autumn, 'pl')).toEqual({
            time: '04:41',
            minutesAhead: -300,
        });
        expect(colleagueClock('America/New_York', 'Europe/Warsaw', summer, 'pl')?.minutesAhead).toBe(-360);
    });

    it('stands a colleague ahead where their day is further on, by a part of an hour where the zone keeps one', () => {
        expect(colleagueClock('Asia/Kolkata', 'Europe/London', autumn, 'en')).toEqual({
            time: '2:11 PM',
            minutesAhead: 330,
        });
    });

    it('reads the same time in the same zone as no difference at all', () => {
        expect(colleagueClock('Europe/Warsaw', 'Europe/Warsaw', autumn, 'pl')).toEqual({
            time: '09:41',
            minutesAhead: 0,
        });
    });

    it('reads no time at all for a zone this runtime does not know, rather than one in the wrong zone', () => {
        expect(colleagueClock('Mars/Olympus_Mons', 'Europe/Warsaw', autumn, 'en')).toBeNull();
    });
});

describe('wordClockDifference', () => {
    it.each([
        [-60, 'en', '1h'],
        [-60, 'pl', '1 h'],
        [300, 'pl', '5 h'],
        [330, 'en', '5h 30m'],
        [330, 'pl', '5 h i 30 min'],
        [-45, 'en', '45m'],
    ] as const)('words %i minutes in %s as %s', (minutes, locale, worded) => {
        expect(wordClockDifference(minutes, locale)).toBe(worded);
    });
});
