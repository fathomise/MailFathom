// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, describe, expect, it } from 'vitest';
import { mailboxesWidthKey } from '../device/deviceStore';
import {
    mailboxesWidthWithin,
    narrowestMailboxes,
    readMailboxesWidth,
    startingMailboxesWidth,
    startingMailboxesWidthIn,
    storeMailboxesWidth,
    widestMailboxes,
} from './mailboxesWidth';

const reader = 'karolina';

afterEach(() => {
    window.localStorage.clear();
});

describe('mailboxesWidthWithin', () => {
    it('leaves a width inside the bounds where it stands', () => {
        expect(mailboxesWidthWithin(300)).toBe(300);
    });

    it('refuses to draw the column narrower than the width the design opens it at', () => {
        expect(mailboxesWidthWithin(40)).toBe(narrowestMailboxes);
    });

    it('refuses to draw it so wide that it stops being a column of names', () => {
        expect(mailboxesWidthWithin(2000)).toBe(widestMailboxes);
    });

    it('answers a whole number of pixels, which is what a column can actually be drawn at', () => {
        expect(mailboxesWidthWithin(300.6)).toBe(301);
    });
});

describe('readMailboxesWidth', () => {
    it('reads nothing chosen where this person has chosen none', () => {
        expect(readMailboxesWidth(reader)).toBeNull();
    });

    it('opens at the width this person settled on last time', () => {
        storeMailboxesWidth(reader, 320);

        expect(readMailboxesWidth(reader)).toBe(320);
    });

    it('keeps one person’s width apart from another’s on the same machine', () => {
        storeMailboxesWidth(reader, 320);

        expect(readMailboxesWidth('tomasz')).toBeNull();
    });

    it('brings a stored width outside the bounds back inside them, a store being somewhere a person can write', () => {
        window.localStorage.setItem(mailboxesWidthKey(reader), '9000');

        expect(readMailboxesWidth(reader)).toBe(widestMailboxes);
    });

    it('reads nothing chosen where what is stored is not a width at all', () => {
        window.localStorage.setItem(mailboxesWidthKey(reader), 'wide');

        expect(readMailboxesWidth(reader)).toBeNull();
    });

    it('reads nothing chosen where nobody is signed in, there being nobody to have chosen one', () => {
        expect(readMailboxesWidth(null)).toBeNull();
    });
});

describe('storeMailboxesWidth', () => {
    it('keeps nothing where nobody is signed in, so no width is written under nobody’s name', () => {
        storeMailboxesWidth(null, 320);

        expect(window.localStorage.length).toBe(0);
    });

    it('forgets the width this person settled on when handed none', () => {
        storeMailboxesWidth(reader, 320);
        storeMailboxesWidth(reader, null);

        expect(readMailboxesWidth(reader)).toBeNull();
    });
});

describe('startingMailboxesWidthIn', () => {
    it('opens the column wider past the monitor width, before anybody drags it', () => {
        expect(startingMailboxesWidthIn(false)).toBe(startingMailboxesWidth);
        expect(startingMailboxesWidthIn(true)).toBe(240);
    });
});
