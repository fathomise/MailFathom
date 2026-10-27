// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { detailsPlacement } from './detailsPlacement';

const desktop = { width: 1440, height: 900 };

describe('detailsPlacement', () => {
    it('opens just under the mark, a little before its start', () => {
        expect(detailsPlacement({ left: 300, top: 120, bottom: 144 }, desktop)).toEqual({
            left: 292,
            top: 150,
            maxHeight: 560,
        });
    });

    it('opens above a mark near the foot of the window, where there is more room', () => {
        expect(detailsPlacement({ left: 300, top: 800, bottom: 824 }, desktop)).toEqual({
            left: 292,
            bottom: 106,
            maxHeight: 560,
        });
    });

    it('stays inside the window beside a mark near its end edge', () => {
        expect(detailsPlacement({ left: 1420, top: 120, bottom: 144 }, desktop).left).toBe(1038);
    });

    it('grows no taller than the room under the mark', () => {
        expect(detailsPlacement({ left: 300, top: 500, bottom: 524 }, desktop).maxHeight).toBe(356);
    });
});
