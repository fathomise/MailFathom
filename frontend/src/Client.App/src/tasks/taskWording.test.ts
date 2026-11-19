// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { en } from '../localization/en';
import type { MessageKey } from '../localization/en';
import { wordCounterpart, wordEstimate, wordPlan, wordReply, wordTaskDay } from './taskWording';

// The zone is named on every call, so each spelling is the reader's own day and clock rather than the runner's.
const warsaw = 'Europe/Warsaw';

// A Monday morning in Warsaw.
const now = Date.parse('2026-09-21T09:00:00+02:00');

function translate(key: MessageKey, values: Readonly<Record<string, string>> = {}): string {
    let said: string = en[key];

    for (const [name, value] of Object.entries(values)) {
        said = said.replace(`{${name}}`, value);
    }

    return said;
}

describe('taskWording', () => {
    it('words a day as today, tomorrow, or the day and the month', () => {
        expect(wordTaskDay('2026-09-21', 'en', now, warsaw)).toBe('today');
        expect(wordTaskDay('2026-09-22', 'en', now, warsaw)).toBe('tomorrow');
        expect(wordTaskDay('2026-10-02', 'en', now, warsaw)).toBe('10/02');
    });

    it('words a stretch planned today as its two clock times, and one on another day with that day beside it', () => {
        const today = wordPlan({ startAt: '2026-09-21T09:00:00+00:00', minutes: 45 }, 'pl', now, warsaw, translate);
        const tomorrow = wordPlan({ startAt: '2026-09-22T07:00:00+00:00', minutes: 30 }, 'pl', now, warsaw, translate);

        expect(today).toBe('11:00–11:45');
        expect(tomorrow).toBe('09:00–09:30 · jutro');
    });

    it('words how long a task takes the short way', () => {
        expect(wordEstimate(45, 'en')).toBe('45 min');
        expect(wordEstimate(120, 'en')).toBe('2 hr');
    });

    it('names who replied to a wait, and when, by the name they are known by', () => {
        const counterpart = { address: 'jacek@contoso.example', displayName: 'Jacek Wrona' };

        expect(wordReply('2026-09-21T07:12:00+00:00', counterpart, 'pl', warsaw, translate)).toBe(
            'Jacek Wrona replied at 09:12',
        );
        expect(wordReply('2026-09-21T07:12:00+00:00', null, 'pl', warsaw, translate)).toBe('Replied at 09:12');
        expect(wordCounterpart({ address: 'ewa@adventure-works.example', displayName: null })).toBe(
            'ewa@adventure-works.example',
        );
    });
});
