// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { catalogues, type Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { outboxCountOf } from './outboxCounts';

/** Reads the catalogue the way the provider does, which is a lookup and a hole filled. */
function translating(locale: Locale): Translate {
    return (key, values) =>
        catalogues[locale][key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);
}

function counted(waiting: number, refused: number, locale: Locale = 'en') {
    return outboxCountOf({ waiting, refused }, { locale, translate: translating(locale) });
}

describe('outboxCountOf', () => {
    it('draws no mark for an Outbox holding nothing, nor for one the credential may not read', () => {
        expect(counted(0, 0)).toBeNull();
        expect(outboxCountOf(null, { locale: 'en', translate: translating('en') })).toBeNull();
    });

    it('counts what waits and what was refused together, and names each of the two', () => {
        expect(counted(4, 1)).toStrictEqual({
            count: '5',
            said: 'Outbox - 4 waiting to leave, 1 refused',
            refused: true,
        });
    });

    it('names only the half that holds anything', () => {
        expect(counted(2, 0)).toStrictEqual({ count: '2', said: 'Outbox - 2 waiting to leave', refused: false });
        expect(counted(0, 3)?.said).toBe('Outbox - 3 refused');
    });

    it('names both halves in the plural form Polish takes for each count', () => {
        expect(counted(1, 1, 'pl')?.said).toBe('Nadawcza - 1 czeka na wysłanie, 1 odrzucona');
        expect(counted(3, 2, 'pl')?.said).toBe('Nadawcza - 3 czekają na wysłanie, 2 odrzucone');
        expect(counted(5, 5, 'pl')?.said).toBe('Nadawcza - 5 czeka na wysłanie, 5 odrzuconych');
    });
});
