// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { reachedTheRowsRead } from './rowsRead';

function scrolledTo(scrollTop: number) {
    return { scrollHeight: 3000, clientHeight: 800, scrollTop };
}

describe('reachedTheRowsRead', () => {
    it('asks for the next page once less than one screen of read rows is left below', () => {
        expect(reachedTheRowsRead(scrolledTo(1400))).toBe(true);
    });

    it('waits while more than one screen of read rows is still below', () => {
        expect(reachedTheRowsRead(scrolledTo(1399))).toBe(false);
    });
});
