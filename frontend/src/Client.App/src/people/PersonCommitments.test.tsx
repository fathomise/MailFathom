// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MailTasksContext, type MailTasks } from '../tasks/useMailTasks';
import { PersonCommitments } from './PersonCommitments';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function taskWith(id: string, address: string, held: Readonly<Record<string, unknown>> = {}): unknown {
    return {
        id,
        title: id,
        dueOn: null,
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        direction: 'Owed',
        estimatedMinutes: null,
        plan: null,
        anchor: null,
        counterpart: { address, displayName: 'Ada Lovelace' },
        answer: null,
        ...held,
    };
}

// One page for each of Ada's two addresses, the wait she owes standing on both.
function answering(request: ClientRequest): unknown[] {
    const wait = taskWith('Notes on the engine', 'ada@example.invalid', { direction: 'Awaited' });

    return request.path.includes('counterpart=ada%40example.invalid')
        ? [taskWith('Send Ada the tables', 'ada@example.invalid'), wait]
        : [
              wait,
              taskWith('Return the borrowed book', 'a.lovelace@example.invalid', { completed: true }),
              taskWith('Book the lecture hall', 'a.lovelace@example.invalid'),
          ];
}

function drawCommitments(answer: (request: ClientRequest) => unknown[] = answering) {
    const asked: ClientRequest[] = [];
    const transport: MailFathomTransport = (request) => {
        asked.push(request);

        return Promise.resolve({
            status: 200,
            headers: {},
            body: JSON.stringify({ tasks: answer(request), nextCursor: null }),
        });
    };
    const tasks: MailTasks = {
        session,
        transport,
        threadsWithTasks: new Set(),
        makeTask: vi.fn(),
        makeTaskOn: vi.fn(),
        openTask: vi.fn(),
        record: vi.fn(),
        follow: () => () => undefined,
    };

    render(
        <LocalizationProvider>
            <MailTasksContext value={tasks}>
                <PersonCommitments
                    addresses={['ada@example.invalid', 'a.lovelace@example.invalid']}
                    preferredAddress="ada@example.invalid"
                    displayName="Ada Lovelace"
                />
            </MailTasksContext>
        </LocalizationProvider>,
    );

    return { tasks, asked };
}

describe('PersonCommitments', () => {
    it('PersonCommitments splits owed from awaited', async () => {
        const { tasks, asked } = drawCommitments();

        const owed = await screen.findByRole('list', { name: 'You owe Ada' });
        const awaited = screen.getByRole('list', { name: 'Ada owes you' });

        expect(
            within(owed)
                .getAllByRole('button')
                .map((task) => task.textContent),
        ).toStrictEqual([
            expect.stringContaining('Send Ada the tables'),
            expect.stringContaining('Book the lecture hall'),
        ]);
        expect(within(awaited).getAllByRole('button')).toHaveLength(1);
        expect(within(awaited).getByRole('button').textContent).toContain('Notes on the engine');
        expect(screen.queryByText(/Return the borrowed book/u)).toBeNull();
        expect(asked.map((request) => new URL(request.path).searchParams.get('counterpart'))).toStrictEqual([
            'ada@example.invalid',
            'a.lovelace@example.invalid',
        ]);

        fireEvent.click(within(awaited).getByRole('button'));

        expect(tasks.openTask).toHaveBeenCalledWith(expect.objectContaining({ id: 'Notes on the engine' }));
    });

    it('says a side is empty, and adds a task with the person', async () => {
        const { tasks } = drawCommitments(() => []);

        expect(await screen.findByText('Nothing you owe.')).toBeDefined();
        expect(screen.getByText('Nothing you are waiting for.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Add a task' }));

        expect(tasks.makeTask).toHaveBeenCalledWith({
            counterpart: { address: 'ada@example.invalid', displayName: 'Ada Lovelace' },
        });
    });
});
