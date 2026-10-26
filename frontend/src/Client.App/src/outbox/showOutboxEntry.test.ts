// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { catalogues } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { wordUnshownSend } from './showOutboxEntry';

const translate: Translate = (key, values) =>
    catalogues.en[key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);

describe('wordUnshownSend', () => {
    it('says the server no longer holds a send it has no record of', () => {
        expect(wordUnshownSend('missing', translate)).toBe('The server no longer holds that send.');
    });

    it('says why a send could not be read rather than that it is gone', () => {
        expect(wordUnshownSend('unavailable', translate)).toBe(
            `That send could not be read: ${catalogues.en['failure.unavailable']}.`,
        );
    });
});
