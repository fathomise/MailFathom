// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { historyWidthWithin, narrowestHistory, widestHistory } from './historyWidth';

describe('historyWidthWithin', () => {
    it('leaves a width inside the bounds where it stands', () => {
        expect(historyWidthWithin(320)).toBe(320);
    });

    it('refuses to draw the history narrower than the design lets it go', () => {
        expect(historyWidthWithin(40)).toBe(narrowestHistory);
    });

    it('refuses to draw it wider than the design lets it go', () => {
        expect(historyWidthWithin(2000)).toBe(widestHistory);
    });

    it('answers a whole number of pixels, which is what a column can actually be drawn at', () => {
        expect(historyWidthWithin(320.6)).toBe(321);
    });
});
