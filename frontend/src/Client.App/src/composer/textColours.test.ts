// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { colourAt, ownColourOf, readableColourNear, readsOnBothThemes, textColourHues } from './textColours';

describe('textColours', () => {
    it('offers only colours that read on both themes, and no colour twice', () => {
        const offered = textColourHues.flatMap((hue) => hue.shades);

        expect(offered.filter((value) => !readsOnBothThemes(value))).toEqual([]);
        expect(new Set(offered).size).toBe(offered.length);
    });

    it.each([
        [0, 0],
        [0.5, 0.5],
        [1, 1],
    ])('writes a colour of somebody’s own inside the readable band at shade %s', (shade) => {
        for (const hue of [0, 60, 120, 210, 300]) {
            const written = colourAt({ hue, saturation: 0.8, shade });

            expect(readsOnBothThemes(written)).toBe(true);
        }
    });

    it('keeps a typed colour that already reads on both themes as it was typed', () => {
        expect(readableColourNear(' #3E7CF5 ')).toBe('#3e7cf5');
    });

    it.each([
        ['#000000', 0],
        ['#1a237e', 0],
        ['#ffffff', 1],
        ['#ffeb3b', 1],
    ])('moves %s to the nearer edge of the band, keeping its hue', (typed, edge) => {
        const written = readableColourNear(typed);

        expect(written).not.toBeNull();
        expect(readsOnBothThemes(written ?? '')).toBe(true);
        expect(ownColourOf(written ?? '')?.shade).toBeCloseTo(edge, 1);
        expect(ownColourOf(written ?? '')?.hue).toBeCloseTo(ownColourOf(typed)?.hue ?? Number.NaN, -1);
    });

    it.each([['blue'], ['#12345'], ['#12345g'], ['']])('refuses %j, which is not a colour', (typed) => {
        expect(readableColourNear(typed)).toBeNull();
    });
});
