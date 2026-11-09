// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailCategory } from '@mailfathom/client-backend';
import { catalogues } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { drawnCategories, drawsCategories } from './categoryWords';

const translateTo: Translate = (key) => catalogues.pl[key];

describe('drawnCategories', () => {
    it('names the defaults in the reader’s language and keeps an own category as written, in the published order', () => {
        const categories: readonly MailCategory[] = [
            { identity: 'travel', name: null },
            { identity: 'Budget 2027', name: 'Budget 2027' },
        ];

        expect(drawnCategories(categories, translateTo)).toEqual([
            { identity: 'travel', name: 'Podróże' },
            { identity: 'Budget 2027', name: 'Budget 2027' },
        ]);
    });

    it('leaves out the two defaults no label is drawn for', () => {
        const categories: readonly MailCategory[] = [
            { identity: 'correspondence', name: null },
            { identity: 'other', name: null },
        ];

        expect(drawnCategories(categories, translateTo)).toEqual([]);
        expect(drawsCategories(categories)).toBe(false);
    });

    it('counts an own category as something to draw', () => {
        expect(drawsCategories([{ identity: 'Legal', name: 'Legal' }])).toBe(true);
    });
});
