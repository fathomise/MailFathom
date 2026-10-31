// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ColleagueWorkingHours } from '@mailfathom/client-backend';
import { describe, expect, it } from 'vitest';
import { wordColleagueHours } from './colleagueHours';

// A Wednesday after the clocks went back in Europe and before they went back in North America: 09:41 in Warsaw, 08:41
// in London, and 04:41 in New York.
const wednesday = Date.parse('2026-10-28T08:41:00Z');
const saturday = Date.parse('2026-10-31T10:00:00Z');

const officeHours: ColleagueWorkingHours = {
    days: ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'],
    start: 9 * 60,
    end: 17 * 60,
};

const run = (first: string, last: string): string => `${first}–${last}`;

describe('wordColleagueHours', () => {
    it("words the hours on the colleague's clock, the same hours on the reader's, and that it is outside them there", () => {
        expect(wordColleagueHours(officeHours, 'America/New_York', 'Europe/Warsaw', wednesday, 'pl', run)).toEqual({
            days: 'pon.–pt.',
            own: '09:00–17:00',
            yours: '14:00–22:00',
            outside: true,
        });
    });

    it('reads a colleague inside their hours where they are, in the reader’s language', () => {
        const hours: ColleagueWorkingHours = { days: ['Wednesday'], start: 8 * 60, end: 16 * 60 + 30 };

        expect(wordColleagueHours(hours, 'Europe/London', 'Europe/Warsaw', wednesday, 'en', run)).toEqual({
            days: 'Wed',
            own: '8:00 AM–4:30 PM',
            yours: '9:00 AM–5:30 PM',
            outside: false,
        });
    });

    it('marks a day that is not worked as outside the hours, whatever the time', () => {
        const hours: ColleagueWorkingHours = { ...officeHours, start: 8 * 60, end: 18 * 60 };

        expect(wordColleagueHours(hours, 'Europe/Warsaw', 'Europe/Warsaw', saturday, 'en', run).outside).toBe(true);
    });

    it('converts nothing where the two clocks read alike', () => {
        expect(
            wordColleagueHours(officeHours, 'Europe/Warsaw', 'Europe/Warsaw', wednesday, 'pl', run).yours,
        ).toBeNull();
    });

    it('wraps converted hours onto the reader’s clock where they cross the reader’s midnight', () => {
        expect(wordColleagueHours(officeHours, 'Europe/Warsaw', 'Asia/Tokyo', wednesday, 'pl', run).yours).toBe(
            '17:00–01:00',
        );
    });

    it('shows the hours as stated, converted to nothing and marked neither way, where no zone is known', () => {
        expect(wordColleagueHours(officeHours, null, 'Europe/Warsaw', wednesday, 'pl', run)).toEqual({
            days: 'pon.–pt.',
            own: '09:00–17:00',
            yours: null,
            outside: null,
        });
        expect(wordColleagueHours(officeHours, 'Mars/Olympus_Mons', null, wednesday, 'pl', run).outside).toBeNull();
    });

    it.each([
        [['Monday', 'Wednesday', 'Friday'], 'Mon, Wed, Fri'],
        [['Monday', 'Tuesday'], 'Mon, Tue'],
        [['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Saturday'], 'Mon–Thu, Sat'],
    ] as const)('folds only a run of three or more days: %j reads %s', (days, worded) => {
        expect(wordColleagueHours({ ...officeHours, days }, null, null, wednesday, 'en', run).days).toBe(worded);
    });
});
