// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type {
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailFathomTransport,
    PersonalTask,
} from '@mailfathom/client-backend';
import { usePlanWriter } from './usePlanWriter';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const read: PersonalTask = {
    id: 'task-room',
    title: 'Book the meeting room',
    dueOn: '2026-09-24',
    reminders: [],
    remindsAt: [],
    origin: 'Asserted',
    completed: false,
    sourceMessageId: null,
    direction: 'Owed',
    estimatedMinutes: 10,
    plan: null,
    anchor: null,
    counterpart: null,
    answer: null,
};

describe('usePlanWriter', () => {
    // A second plan pressed while the first is out is written once that one answers, restating the task as the second
    // press read it — a list read back between the two presses has a newer record than the first press held.
    it('writes a plan pressed while one is out with the task the latest press read', async () => {
        const sent: ClientRequest[] = [];
        const answers: ((answer: ClientResponse) => void)[] = [];
        const transport: MailFathomTransport = (request) => {
            sent.push(request);

            return new Promise((resolve) => {
                answers.push(resolve);
            });
        };

        const { result } = renderHook(() => usePlanWriter(session, transport, 'UTC'));
        const nine = { startAt: '2026-09-24T09:00:00.000Z', minutes: 10 };
        const ten = { startAt: '2026-09-24T10:00:00.000Z', minutes: 10 };
        const renamed = { ...read, title: 'Book the big meeting room' };

        act(() => {
            result.current.plan([[read, { plan: nine, estimatedMinutes: 10 }]], () => Promise.resolve());
            result.current.plan([[renamed, { plan: ten, estimatedMinutes: 10 }]], () => Promise.resolve());
        });

        expect(sent).toHaveLength(1);

        act(() => {
            answers[0]?.({ status: 200, headers: {}, body: JSON.stringify({ ...read, plan: nine }) });
        });

        await waitFor(() => {
            expect(sent).toHaveLength(2);
        });

        const second: unknown = JSON.parse(sent[1]?.body ?? '{}');

        expect(second).toMatchObject({ title: 'Book the big meeting room', plan: ten });
    });
});
