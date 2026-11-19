// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { useEffect } from 'react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport, PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
import { MailTasksProvider } from './MailTasks';
import { TaskMark } from './TaskMark';
import { ThreadTasks } from './ThreadTasks';
import { taskAboutThread, useMailTasks, type MailTasks } from './useMailTasks';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const threadId = '00000000-0000-4000-8000-0000000000c0';
const quietThreadId = '00000000-0000-4000-8000-0000000000d1';

function taskCalled(id: string, held: Partial<PersonalTask> = {}): PersonalTask {
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
        anchor: { kind: 'Thread', id: threadId, label: 'Contract addendum' },
        counterpart: null,
        answer: null,
        ...held,
    };
}

// A deployment holding the tasks it is handed, answering a thread's read with that thread's and every write with a task.
function deployment(held: readonly PersonalTask[]): {
    readonly transport: MailFathomTransport;
    readonly sent: ClientRequest[];
} {
    const sent: ClientRequest[] = [];
    const tasks = [...held];

    return {
        sent,
        transport: (request) => {
            sent.push(request);

            if (request.method === 'POST') {
                tasks.push(taskCalled('Written here'));

                return Promise.resolve({ status: 201, headers: {}, body: JSON.stringify(taskCalled('Written here')) });
            }

            const asked = new URLSearchParams(request.path.split('?')[1]).get('threadId');
            const answered = tasks.filter((task) => asked === null || task.anchor?.id === asked);

            return Promise.resolve({
                status: 200,
                headers: {},
                body: JSON.stringify({ tasks: answered, nextCursor: null }),
            });
        },
    };
}

// What Mail reaches, held the way a surface below the provider holds it, so a test asks through it as a press would.
let reached: MailTasks | null = null;

function Reaching() {
    const tasks = useMailTasks();

    useEffect(() => {
        reached = tasks;
    });

    return null;
}

function drawMail(transport: MailFathomTransport, offered = true): void {
    render(
        <LocalizationProvider>
            <ToastsProvider>
                <MailTasksProvider session={offered ? session : null} transport={transport}>
                    <TaskMark threadId={threadId} />
                    <TaskMark threadId={quietThreadId} />
                    <ThreadTasks threadId={threadId} compact={false} />
                    <Reaching />
                </MailTasksProvider>
            </ToastsProvider>
        </LocalizationProvider>,
    );
}

describe('MailTasksProvider', () => {
    it('marks the threads an open task belongs to from one unfiltered read, and draws only the open ones', async () => {
        const { transport, sent } = deployment([
            taskCalled('Sign the addendum'),
            taskCalled('Send the invoice', { completed: true }),
        ]);

        drawMail(transport);

        expect(await screen.findByRole('button', { name: /^Sign the addendum/u })).toBeDefined();
        await waitFor(() => {
            expect(screen.getAllByText('Has an open task')).toHaveLength(1);
        });
        expect(screen.queryByRole('button', { name: /^Send the invoice/u })).toBeNull();
        expect(sent.filter((request) => !request.path.includes('threadId='))).toHaveLength(1);
    });

    it('marks no thread whose tasks are all done', async () => {
        const { transport } = deployment([
            taskCalled('Send the invoice', { completed: true }),
            taskCalled('Book the room', {
                anchor: { kind: 'Thread', id: quietThreadId, label: 'Room' },
            }),
        ]);

        drawMail(transport);

        expect(await screen.findByText('Has an open task')).toBeDefined();
        expect(screen.getAllByText('Has an open task')).toHaveLength(1);
        expect(screen.queryByRole('button', { name: /^Send the invoice/u })).toBeNull();
    });

    it("draws the thread's new task once one is written down about it", async () => {
        const { transport, sent } = deployment([]);

        drawMail(transport);
        act(() => {
            reached?.makeTask(taskAboutThread(threadId, 'Contract addendum', null));
        });

        const writing = screen.getByRole('dialog', { name: 'New task' });

        expect(screen.getByLabelText('Task description')).toHaveProperty('value', 'Contract addendum');
        fireEvent.click(screen.getByRole('button', { name: 'Add task' }));
        await waitFor(() => {
            expect(writing).toHaveProperty('open', false);
        });

        expect(await screen.findByRole('button', { name: /^Written here/u })).toBeDefined();
        expect(JSON.parse(sent.find((request) => request.method === 'POST')?.body ?? '{}')).toMatchObject({
            anchor: { kind: 'Thread', id: threadId, label: 'Contract addendum' },
        });
    });

    it('reads nothing and marks nothing where the person keeps no tasks', () => {
        const { transport, sent } = deployment([taskCalled('Sign the addendum')]);

        drawMail(transport, false);

        expect(screen.queryByText('Has an open task')).toBeNull();
        expect(sent).toHaveLength(0);
    });
});
