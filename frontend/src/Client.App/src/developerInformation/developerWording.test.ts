// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { wordBytes, wordFromNow, wordMilliseconds } from './developerWording';

const now = 1_000_000;

describe('an instant, worded from now', () => {
    it('reads as now within a second either side, which is the skew the surface’s once-a-second clock leaves', () => {
        expect(wordFromNow(now + 300, now, 'en')).toBe('now');
        expect(wordFromNow(now - 300, now, 'en')).toBe('now');
        expect(wordFromNow(now + 999, now, 'en')).toBe('now');
    });

    it('reads as seconds ahead or behind from a second on', () => {
        expect(wordFromNow(now + 1_000, now, 'en')).toBe('in 1 sec.');
        expect(wordFromNow(now - 1_000, now, 'en')).toBe('1 sec. ago');
    });
});

describe('a duration and a size', () => {
    it('words a duration in whole milliseconds', () => {
        expect(wordMilliseconds(250.4, 'en')).toBe('250 ms');
    });

    it('words a size in bytes below a kilobyte and in kilobytes from one', () => {
        expect(wordBytes(1_023, 'en')).toBe('1,023 byte');
        expect(wordBytes(1_024, 'en')).toBe('1 kB');
    });
});
