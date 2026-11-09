// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { parseMailCategories } from './mailCategories';

describe('parseMailCategories', () => {
    it('reads a default and an own category in the order they were published', () => {
        expect(
            parseMailCategories([
                { identity: 'finance', name: null },
                { identity: 'Contracts', name: 'Contracts' },
            ]),
        ).toEqual([
            { identity: 'finance', name: null },
            { identity: 'Contracts', name: 'Contracts' },
        ]);
    });

    it('reads a field the answer left out as a message showing none', () => {
        expect(parseMailCategories(undefined)).toEqual([]);
    });

    it('refuses an unnamed category that is none of the defaults', () => {
        expect(parseMailCategories([{ identity: 'Contracts', name: null }])).toBeNull();
    });

    it('refuses a name that is not one a mailbox could have written', () => {
        expect(parseMailCategories([{ identity: 'Long', name: 'x'.repeat(257) }])).toBeNull();
        expect(parseMailCategories([{ identity: 'Empty', name: '' }])).toBeNull();
    });

    it('refuses more categories than a mailbox can hold', () => {
        const tooMany = Array.from({ length: 28 }, (_, at) => ({ identity: `own-${String(at)}`, name: 'Own' }));

        expect(parseMailCategories(tooMany)).toBeNull();
    });

    it('refuses a field that is not a list', () => {
        expect(parseMailCategories('finance')).toBeNull();
    });
});
