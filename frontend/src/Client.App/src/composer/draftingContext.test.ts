// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { mostDraftContextMessages } from '@mailfathom/client-backend';
import { describe, expect, it } from 'vitest';
import { contextPointedAt, sameMessages } from './draftingContext';

const nothing = { selection: null, selected: [] };

describe('contextPointedAt', () => {
    it('makes a message opened from the list the context', () => {
        expect(contextPointedAt(nothing, { selection: 'm1', selected: [] })).toStrictEqual(['m1']);
    });

    it('makes a selection the context, whichever message is open', () => {
        expect(contextPointedAt(nothing, { selection: 'm1', selected: ['m2', 'm3'] })).toStrictEqual(['m2', 'm3']);
    });

    it('leaves the context where it stood when a selection empties', () => {
        const selecting = { selection: 'm1', selected: ['m2'] };

        expect(contextPointedAt(selecting, { selection: 'm1', selected: [] })).toBeNull();
    });

    it('leaves it where it stood when the open message closes', () => {
        expect(contextPointedAt({ selection: 'm1', selected: [] }, nothing)).toBeNull();
    });

    it('takes no more of a selection than one drafting is written from', () => {
        const selected = Array.from({ length: mostDraftContextMessages + 3 }, (_, index) => `m${String(index)}`);

        expect(contextPointedAt(nothing, { selection: null, selected })).toHaveLength(mostDraftContextMessages);
    });
});

describe('sameMessages', () => {
    it('reads two contexts holding the same messages in another order as the same', () => {
        expect(sameMessages(['m1', 'm2'], ['m2', 'm1'])).toBe(true);
    });

    it('reads a context that lost or gained a message as another', () => {
        expect(sameMessages(['m1', 'm2'], ['m1'])).toBe(false);
        expect(sameMessages(['m1'], ['m2'])).toBe(false);
    });
});
