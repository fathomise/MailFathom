// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport, PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import type { MessageKey } from '../localization/en';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { TaskSurfaces, type TaskAsked } from './TaskSurfaces';
import { usePlanWriter } from './usePlanWriter';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const serverAnswering: ConnectionStatus = {
    level: 'online',
    attempts: null,
    retry: () => undefined,
    detailsOpen: false,
    showDetails: () => undefined,
    disconnectedNow: () => false,
};

const owed: PersonalTask = {
    id: 'task-room',
    title: 'Book the meeting room',
    dueOn: '2026-09-24',
    reminders: [],
    remindsAt: [],
    origin: 'Asserted',
    completed: false,
    sourceMessageId: null,
    direction: 'Owed',
    estimatedMinutes: 30,
    plan: null,
    anchor: null,
    counterpart: null,
    answer: null,
};

const machineZone = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'UTC';
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-09-24T07:00:00Z'));
});

afterEach(() => {
    vi.useRealTimers();
    process.env['TZ'] = machineZone;
});

// A deployment answering every write with the task it names and every read with an empty calendar.
function deployment(): { readonly transport: MailFathomTransport; readonly sent: ClientRequest[] } {
    const sent: ClientRequest[] = [];

    return {
        sent,
        transport: (request) => {
            sent.push(request);

            const body =
                request.method === 'DELETE'
                    ? { id: owed.id, erased: true }
                    : request.method === 'GET'
                      ? { events: [] }
                      : owed;

            return Promise.resolve({ status: 200, headers: {}, body: JSON.stringify(body) });
        },
    };
}

function Screen({
    transport,
    first,
    onWritten,
}: {
    readonly transport: MailFathomTransport;
    readonly first: TaskAsked;
    readonly onWritten: (said: MessageKey) => Promise<void>;
}) {
    const [asked, setAsked] = useState<TaskAsked | null>(first);
    const writer = usePlanWriter(session, transport, 'UTC');

    return (
        <TaskSurfaces
            session={session}
            transport={transport}
            asked={asked}
            events={[]}
            tasks={[]}
            writer={writer}
            onAsk={setAsked}
            onWritten={onWritten}
            onOpenAnchor={undefined}
        />
    );
}

function drawSurfaces(first: TaskAsked) {
    const { transport, sent } = deployment();
    const written = vi.fn<(said: MessageKey) => Promise<void>>(() => Promise.resolve());

    render(
        <LocalizationProvider>
            <ConnectionStatusContext value={serverAnswering}>
                <Screen transport={transport} first={first} onWritten={written} />
            </ConnectionStatusContext>
        </LocalizationProvider>,
    );

    return { sent, written };
}

const held: TaskAsked = { kind: 'menu', task: owed, at: { x: 10, y: 10 } };

function writesTo(sent: readonly ClientRequest[], method: string): readonly ClientRequest[] {
    return sent.filter((request) => request.method === method && request.path.includes('/tasks'));
}

describe('TaskSurfaces', () => {
    it("the menu's Open task opens the task dialog", () => {
        drawSurfaces(held);

        fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Open task' }));

        expect(screen.getByRole('dialog', { name: 'Task' })).toBeDefined();
        expect(screen.queryByRole('menu')).toBeNull();
    });

    it("the menu's Mark as done marks the task done and says so", async () => {
        const { sent, written } = drawSurfaces(held);

        fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Mark as done' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.markedDone');
        });
        expect(writesTo(sent, 'POST').map((request) => [request.path, request.body])).toStrictEqual([
            [expect.stringContaining('/tasks/task-room/completion'), JSON.stringify({ completed: true })],
        ]);
    });

    it("the menu's Plan a time opens the plan dialog, whose plan is written through the plan writer", async () => {
        const { sent, written } = drawSurfaces(held);

        fireEvent.click(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Plan a time' }));

        const planning = screen.getByRole('dialog', { name: 'Plan a time' });

        fireEvent.click(within(planning).getByRole('button', { name: /^2:00 PM/u }));
        fireEvent.click(within(planning).getByRole('button', { name: 'Plan' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.plannedOn', expect.objectContaining({ title: owed.title }));
        });

        const [revision] = writesTo(sent, 'PUT');
        expect(JSON.parse(revision?.body ?? '{}')).toMatchObject({
            title: owed.title,
            plan: { startAt: '2026-09-24T14:00:00.000Z', minutes: 30 },
            estimatedMinutes: 30,
        });
    });

    it('a task planned already is moved from the menu', () => {
        drawSurfaces({ ...held, task: { ...owed, plan: { startAt: '2026-09-24T09:00:00Z', minutes: 30 } } });

        expect(within(screen.getByRole('menu')).getByRole('menuitem', { name: 'Move' })).toBeDefined();
    });

    it('erases the task through the question its deletion stands behind', async () => {
        const { sent, written } = drawSurfaces({ kind: 'open', task: owed });

        fireEvent.click(within(screen.getByRole('dialog', { name: 'Task' })).getByRole('button', { name: 'Delete' }));

        expect(writesTo(sent, 'DELETE')).toHaveLength(0);

        fireEvent.click(
            within(screen.getByRole('dialog', { name: 'Delete this task?' })).getByRole('button', { name: 'Delete' }),
        );

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.erased');
        });
        expect(writesTo(sent, 'DELETE').map((request) => request.path)).toStrictEqual([
            expect.stringContaining('/tasks/task-room'),
        ]);
    });

    it('writes what announces the task with the whole record restated', async () => {
        const { sent, written } = drawSurfaces({ kind: 'open', task: owed });

        fireEvent.click(screen.getByRole('button', { name: 'Change the reminders' }));
        fireEvent.click(
            within(screen.getByRole('dialog', { name: /Reminders/u })).getByRole('button', { name: '2 days before' }),
        );

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.remindersWritten');
        });

        const [revision] = writesTo(sent, 'PUT');
        expect(JSON.parse(revision?.body ?? '{}')).toMatchObject({
            title: owed.title,
            dueOn: owed.dueOn,
            reminders: [2880],
            estimatedMinutes: 30,
        });
    });
});
