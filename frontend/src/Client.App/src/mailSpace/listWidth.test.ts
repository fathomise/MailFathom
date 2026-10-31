// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { listWidthKey } from '../device/deviceStore';
import {
    leastReadingWidth,
    listWidthWithin,
    narrowestList,
    readListWidth,
    startingListWidth,
    startingListWidthIn,
    storeListWidth,
    widestList,
} from './listWidth';

const reader = 'karolina';

afterEach(() => {
    window.localStorage.clear();
});

describe('listWidthWithin', () => {
    it('leaves a width the window has room for where it stands', () => {
        expect(listWidthWithin(400, 1200)).toBe(400);
    });

    it('refuses to draw the list narrower than a sender and a subject fit on', () => {
        expect(listWidthWithin(40, 1200)).toBe(narrowestList);
    });

    it('refuses to draw the list so wide that it stops being a list beside a message', () => {
        expect(listWidthWithin(2000, 4000)).toBe(widestList);
    });

    it('brings a width a narrower window no longer has room for back to what fits', () => {
        expect(listWidthWithin(widestList, leastReadingWidth + 400)).toBe(400);
    });

    it('keeps the list at its own minimum where the window has room for neither column in full', () => {
        expect(listWidthWithin(widestList, narrowestList)).toBe(narrowestList);
    });

    it('answers a whole number of pixels, which is what a column can actually be drawn at', () => {
        expect(listWidthWithin(400.6, 1200)).toBe(401);
    });
});

describe('readListWidth', () => {
    it('reads nothing chosen where this person has chosen none', () => {
        expect(readListWidth(reader)).toBeNull();
    });

    it('opens at the width this person last settled on', () => {
        storeListWidth(reader, 420);

        expect(readListWidth(reader)).toBe(420);
    });

    it('reads nothing chosen for somebody else on the same machine', () => {
        storeListWidth(reader, 420);

        expect(readListWidth('marta')).toBeNull();
    });

    it('reads nothing chosen once the width this person settled on is forgotten', () => {
        storeListWidth(reader, 420);
        storeListWidth(reader, null);

        expect(readListWidth(reader)).toBeNull();
    });

    it('reads nothing chosen where nobody is named, and keeps nothing under one', () => {
        storeListWidth(null, 420);

        expect(readListWidth(null)).toBeNull();
    });

    it.each(['', 'wide', '0', '-1'])('reads a stored %s as nothing chosen rather than as a width', (stored) => {
        window.localStorage.setItem(listWidthKey(reader), stored);

        expect(readListWidth(reader)).toBeNull();
    });

    it('holds a stored width to the bounds, so an edited store cannot draw a column off the screen', () => {
        window.localStorage.setItem(listWidthKey(reader), '4000');

        expect(readListWidth(reader)).toBe(widestList);
    });
});

describe('startingListWidthIn', () => {
    it('opens the list at the width each band draws it at before anybody drags it', () => {
        expect(startingListWidthIn(false, false, false)).toBe(248);
        expect(startingListWidthIn(false, false, true)).toBe(284);
        expect(startingListWidthIn(true, false, true)).toBe(startingListWidth);
        expect(startingListWidthIn(true, true, true)).toBe(420);
    });
});
