// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { heightFor } from './fieldHeight';

// jsdom lays nothing out, so the measurement is asserted where it is decided: the height a field takes for what was
// drawn in it. What a browser draws is the browser suite's.

describe('heightFor', () => {
    it('takes the height of what is written while it is six lines or fewer, and does not scroll', () => {
        expect(heightFor({ written: 20 * 3 + 4, line: 20, frame: 4 })).toEqual({ height: 64, scrolls: false });
    });

    it('stops at six lines and scrolls past them', () => {
        expect(heightFor({ written: 20 * 9 + 4, line: 20, frame: 4 })).toEqual({ height: 124, scrolls: true });
    });

    it('does not scroll at exactly six lines', () => {
        expect(heightFor({ written: 20 * 6 + 4, line: 20, frame: 4 })).toEqual({ height: 124, scrolls: false });
    });
});
