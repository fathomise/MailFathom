// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { pullDisc, pullOffset, pullRefreshes, pullSoFar } from './pullTravel';

describe('pullSoFar', () => {
    it.each([
        [0, 13, 'undecided'],
        [-13, 13, 'undecided'],
        [0, 14, 'pull'],
        [10, 20, 'pull'],
        [0, -14, 'declined'],
        [20, 20, 'declined'],
        [-30, 18, 'declined'],
    ] as const)('reads a finger %i across and %i down as %s', (across, down, decision) => {
        expect(pullSoFar(across, down)).toBe(decision);
    });
});

describe('pullOffset', () => {
    it('keeps the disc above the list until the finger has moved', () => {
        expect(pullOffset(0)).toBe(-pullDisc);
    });

    it('follows half of the finger, so the pull has resistance', () => {
        expect(pullOffset(100)).toBe(50 - pullDisc);
    });

    it('never carries the disc further than its furthest point however far the finger goes', () => {
        expect(pullOffset(1_000)).toBe(pullOffset(192));
    });
});

describe('pullRefreshes', () => {
    it.each([
        [127, false],
        [128, true],
        [400, true],
    ])('reads a release after %i px as asking for a refresh: %s', (down, refreshes) => {
        expect(pullRefreshes(down)).toBe(refreshes);
    });
});
