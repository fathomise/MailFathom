// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientNotification } from '@mailfathom/client-backend';
import { failureOf } from './failures';

const row: ClientNotification = {
    id: 'n-1',
    kind: 'Mail',
    title: 'A change did not reach your mailbox',
    body: '',
    source: 'work',
    statement: { cause: 'MailboxChangeAbandoned', counted: 4, outOf: null },
    target: { kind: 'MailboxChange', storedEmailId: 'e-1', changeRecordId: 'r-1' },
    occurredAt: '2026-09-04T11:55:00+00:00',
    read: false,
};

describe('failureOf', () => {
    it('reads a change the mailbox did not take with the act its statement counts', () => {
        expect(failureOf(row)).toStrictEqual({ kind: 'change', recordId: 'r-1', storedEmailId: 'e-1', act: 'move' });
    });

    it('reads a refused send and a draft out of step from their own targets', () => {
        expect(
            failureOf({
                ...row,
                statement: { cause: 'SendRefused', counted: 1, outOf: null },
                target: { kind: 'OutgoingEmail', outgoingEmailId: 'o-1' },
            }),
        ).toStrictEqual({ kind: 'send', outgoingEmailId: 'o-1' });
        expect(
            failureOf({
                ...row,
                statement: { cause: 'DraftCopyNotKept', counted: 0, outOf: null },
                target: { kind: 'MailDraft', draftId: 'd-1' },
            }),
        ).toStrictEqual({ kind: 'draft', draftId: 'd-1' });
    });

    it.each([
        ['a cause over a target that is not its own', { target: { kind: 'Message', storedEmailId: 'e-1' } }],
        [
            'an act this build does not name',
            { statement: { cause: 'MailboxChangeAbandoned', counted: 99, outOf: null } },
        ],
        ['a row stating no condition', { statement: null }],
    ] as const)('reads no failure from %s', (_, differs) => {
        expect(failureOf({ ...row, ...differs })).toBeNull();
    });
});
