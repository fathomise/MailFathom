// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { detailsPlacement, receiptDetailsShape, senderDetailsShape } from './detailsPlacement';

const desktop = { width: 1440, height: 900 };

describe('detailsPlacement', () => {
    it('opens just under the mark, a little before its start', () => {
        expect(detailsPlacement({ left: 300, right: 324, top: 120, bottom: 144 }, desktop, senderDetailsShape)).toEqual(
            {
                left: 292,
                top: 150,
                maxHeight: 560,
            },
        );
    });

    it('opens above a mark near the foot of the window, where there is more room', () => {
        expect(detailsPlacement({ left: 300, right: 324, top: 800, bottom: 824 }, desktop, senderDetailsShape)).toEqual(
            {
                left: 292,
                bottom: 106,
                maxHeight: 560,
            },
        );
    });

    it('stays inside the window beside a mark near its end edge', () => {
        expect(
            detailsPlacement({ left: 1420, right: 1440, top: 120, bottom: 144 }, desktop, senderDetailsShape).left,
        ).toBe(1038);
    });

    it('grows no taller than the room under the mark', () => {
        expect(
            detailsPlacement({ left: 300, right: 324, top: 500, bottom: 524 }, desktop, senderDetailsShape).maxHeight,
        ).toBe(356);
    });

    it('stands the receipt details with their end edge on the mark’s', () => {
        expect(
            detailsPlacement({ left: 900, right: 980, top: 120, bottom: 142 }, desktop, receiptDetailsShape),
        ).toEqual({
            left: 620,
            top: 148,
            maxHeight: 520,
        });
    });

    it('keeps the receipt details inside the window beside a mark near its start edge', () => {
        expect(
            detailsPlacement({ left: 20, right: 44, top: 120, bottom: 142 }, desktop, receiptDetailsShape).left,
        ).toBe(10);
    });
});
