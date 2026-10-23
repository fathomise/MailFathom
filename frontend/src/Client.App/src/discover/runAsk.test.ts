// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { runAskedFor } from './runAsk';

const question = 'What did we agree the rate would be?';

describe('runAskedFor', () => {
    it('asks the words somebody typed about every mailbox, stated as the empty scope rather than left out', () => {
        expect(runAskedFor({ question })).toEqual({
            question,
            accounts: [],
            folders: [],
            thread: null,
            emails: [],
        });
    });
});
