// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { conversation } from '../../../../tests/fixtures/mail';
import { swipeDistance } from '../controls/swipeAcross';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { TasksSpace } from './TasksSpace';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// jsdom evaluates no media query, so the composition a test is about is stated as a width and read back out of the
// query itself rather than from a table this file would have to keep in step with the stylesheet. It is the double
// `people/PeopleSpace.test.tsx` uses, for the same reason.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

const phone = 390;
const desktop = 1440;

function atWidth(pixels: number): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => {
            const named = /([\d.]+)rem/u.exec(query)?.[1];

            return {
                matches: named !== undefined && pixels >= Number(named) * 16,
                media: query,
                addEventListener: () => undefined,
                removeEventListener: () => undefined,
            };
        },
    });
}

const machineZone = process.env['TZ'];

beforeEach(() => {
    atWidth(desktop);
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    vi.useRealTimers();
    process.env['TZ'] = machineZone;

    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

function taskCalled(id: string, title: string, held: Record<string, unknown> = {}): unknown {
    return {
        id,
        title,
        dueOn: '2026-09-24',
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        direction: 'Owed',
        estimatedMinutes: null,
        plan: null,
        anchor: null,
        counterpart: null,
        answer: null,
        ...held,
    };
}

const answered = { status: 200, headers: {} } as const;

// One deployment behind the screen: both halves of the list, the day beside it, whether a day is arranged at all, the
// arrangement itself, and the five writes. A test states what each answers and reads back what the screen did about it.
function deployment({
    committed = [taskCalled('a', 'Answer the tender')],
    proposed = [],
    events = [],
    arranges = true,
    layout = { arranged: false, placements: [], notToday: [] },
    layoutStatus = 200,
    refuses = [],
    leavesUnanswered = [],
    holdsTaskWrites = false,
}: {
    committed?: readonly unknown[];
    proposed?: readonly unknown[];
    events?: readonly unknown[];
    arranges?: boolean;
    layout?: unknown;
    layoutStatus?: number;

    /** Which writes this deployment will not perform, which is how a batch is made to half succeed. */
    refuses?: readonly string[];

    /** Which writes a gateway answers for the server, which says nothing about whether they were handed on. */
    leavesUnanswered?: readonly string[];

    /** Whether a task's revision waits until it is let through, which is how a second press is caught in flight. */
    holdsTaskWrites?: boolean;
} = {}): {
    readonly transport: MailFathomTransport;
    readonly sent: () => readonly ClientRequest[];
    readonly letThrough: () => void;
} {
    const sent: ClientRequest[] = [];
    const held: (() => void)[] = [];
    let served = 0;

    return {
        sent: () => sent,
        letThrough: () => {
            held.shift()?.();
        },
        transport: (request) => {
            sent.push(request);

            const { method, path } = request;
            let answer: ClientResponse;

            if (path.includes('/tasks/today/layout')) {
                answer =
                    method === 'GET'
                        ? { ...answered, body: JSON.stringify({ arrangesDays: arranges }) }
                        : { status: layoutStatus, headers: {}, body: JSON.stringify(layout) };
            } else if (path.includes('/calendar')) {
                // The window asked for is what is answered, so a day read and a longer one read differ as they would.
                const asked = new URL(path).searchParams;
                const from = Date.parse(asked.get('from') ?? '');
                const until = Date.parse(asked.get('until') ?? '');
                const inWindow = events.filter((event) => {
                    const start = Date.parse((event as { readonly start: string }).start);

                    return start >= from && start < until;
                });

                answer = { ...answered, body: JSON.stringify({ events: inWindow }) };
            } else if (method === 'PUT' && holdsTaskWrites) {
                const answering = { ...answered, body: JSON.stringify(taskCalled('a', 'Answer the tender')) };

                return new Promise<ClientResponse>((resolve) => {
                    held.push(() => {
                        resolve(answering);
                    });
                });
            } else if (method !== 'GET' && leavesUnanswered.some((task) => path.includes(`/tasks/${task}`))) {
                answer = { status: 504, headers: {}, body: '' };
            } else if (method === 'DELETE') {
                answer = refuses.some((task) => path.endsWith(`/tasks/${task}`))
                    ? { status: 403, headers: {}, body: '' }
                    : { ...answered, body: JSON.stringify({ id: 'a', erased: true }) };
            } else if (method === 'POST' || method === 'PUT') {
                answer = refuses.some((task) => path.includes(`/tasks/${task}`))
                    ? { status: 403, headers: {}, body: '' }
                    : { ...answered, body: JSON.stringify(taskCalled('a', 'Answer the tender')) };
            } else if (path.includes('/tasks/proposed')) {
                served += 1;
                answer = { ...answered, body: JSON.stringify({ tasks: served > 1 ? [] : proposed, nextCursor: null }) };
            } else {
                answer = { ...answered, body: JSON.stringify({ tasks: committed, nextCursor: null }) };
            }

            return Promise.resolve(answer);
        },
    };
}

function drawSpace(
    transport: MailFathomTransport,
    {
        asksDeployment = true,
        readsCalendar = true,
        readsMail = true,
        onOpenMessage = vi.fn(),
        lostServer = false,
    }: {
        asksDeployment?: boolean;
        readsCalendar?: boolean;
        readsMail?: boolean;
        onOpenMessage?: (id: string) => void;

        /** Whether the connection status says the server is out of reach, which refuses every write before use. */
        lostServer?: boolean;
    } = {},
): void {
    render(
        <LocalizationProvider>
            <ConnectionStatusContext value={lostServer ? serverLost : serverAnswering}>
                <TasksSpace
                    session={session}
                    transport={transport}
                    asksDeployment={asksDeployment}
                    readsCalendar={readsCalendar}
                    onOpenMessage={readsMail ? onOpenMessage : undefined}
                />
            </ConnectionStatusContext>
        </LocalizationProvider>,
    );
}

const serverAnswering: ConnectionStatus = {
    level: 'online',
    attempts: null,
    retry: () => undefined,
    detailsOpen: false,
    showDetails: () => undefined,
    disconnectedNow: () => false,
};

const serverLost: ConnectionStatus = {
    level: 'disconnected',
    cause: 'unanswered',
    since: 0,
    attempts: null,
    retry: () => undefined,
    detailsOpen: false,
    showDetails: () => undefined,
    disconnectedNow: () => true,
};

// Moves to one of the four views, which the head draws as a choice among four.
function showView(name: RegExp): void {
    fireEvent.click(screen.getByRole('radio', { name }));
}

// The first row of the list, which the hours of the day beside it are list items beside.
function firstTaskRow(): Promise<HTMLElement> {
    return waitFor(() => {
        const row = screen.getAllByRole('listitem').find((item) => item.closest('aside') === null);

        if (row === undefined) {
            throw new Error('No task row is drawn.');
        }

        return row;
    });
}

// One hour of the day beside the list, found by the time it starts at.
function hourOfTheDay(label: string): HTMLElement {
    const hour = within(screen.getByRole('complementary')).getByText(label, { exact: true }).closest('li');

    if (hour === null) {
        throw new Error(`No hour ${label} is drawn.`);
    }

    return hour;
}

// Carries a task's row onto one hour of the day, as a pointer that drags it there would.
function dragOnto(title: string, label: string): void {
    const row = screen
        .getAllByText(title)
        .map((drawn) => drawn.closest('li[draggable="true"]'))
        .find((found): found is Element => found !== null);
    const carried = new Map<string, string>();
    const dataTransfer = {
        effectAllowed: 'none',
        get types() {
            return [...carried.keys()];
        },
        setData: (type: string, value: string) => carried.set(type, value),
        getData: (type: string) => carried.get(type) ?? '',
    };

    if (row === undefined) {
        throw new Error(`No row of ${title} can be dragged.`);
    }

    const hour = hourOfTheDay(label);

    fireEvent.dragStart(row, { dataTransfer });
    fireEvent.dragOver(hour, { dataTransfer });
    fireEvent.drop(hour, { dataTransfer });
}

function pathsSent(sent: readonly ClientRequest[], method: string): string[] {
    return sent.filter((request) => request.method === method).map((request) => request.path);
}

describe('TasksSpace', () => {
    it('draws what is owed under Today and what was proposed under Proposed, marking what came out of mail', async () => {
        const { transport } = deployment({
            proposed: [taskCalled('b', 'Send the invoice', { origin: 'Proposed', sourceMessageId: 'message' })],
        });

        drawSpace(transport);

        expect(await screen.findByText('Answer the tender')).toBeDefined();
        expect(screen.queryByText('Send the invoice')).toBeNull();

        showView(/^Proposed/u);

        expect(screen.getByText('Send the invoice')).toBeDefined();
        expect(screen.getByText('From mail')).toBeDefined();
        expect(screen.queryByText('Answer the tender')).toBeNull();
    });

    it('opens a task from its row and writes a new title through its dialog', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Answer the tender' }));

        const dialog = screen.getByRole('dialog', { name: 'Task' });

        fireEvent.change(within(dialog).getByLabelText('What is it?'), {
            target: { value: 'Answer the Nordwind tender' },
        });
        fireEvent.click(within(dialog).getByRole('button', { name: 'Save' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Saved.');
        });

        const revised = sent().find((request) => request.method === 'PUT');

        expect(revised?.path).toBe('https://mail.example.invalid/api/client/tasks/a');
        expect(JSON.parse(revised?.body ?? 'null')).toMatchObject({ title: 'Answer the Nordwind tender' });
    });

    it('heads each view with what it holds and counts what is open in it', async () => {
        const { transport } = deployment({
            committed: [
                taskCalled('a', 'Answer the tender'),
                taskCalled('w', 'Signed addendum from Anna', { direction: 'Awaited', dueOn: '2099-01-01' }),
            ],
        });

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        expect(screen.getByRole('radio', { name: /^Waiting for\s*1$/u })).toBeDefined();

        showView(/^Waiting for/u);

        expect(screen.getByRole('heading', { name: 'Waiting for others' })).toBeDefined();
        expect(screen.getByText('1 reply you are waiting on')).toBeDefined();
        expect(screen.getByRole('region', { name: 'Waiting' })).toBeDefined();
    });

    it('reads today’s events through the calendar’s own route and draws them beside the list', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-09-21T05:00:00Z'));

        const { transport, sent } = deployment({
            events: [
                {
                    id: 'e',
                    title: 'Standup',
                    start: '2026-09-21T07:00:00+00:00',
                    end: '2026-09-21T07:15:00+00:00',
                    isAllDay: false,
                    reminders: [],
                    remindsAt: [],
                    origin: 'Asserted',
                    sourceMessage: null,
                    recordedAt: '2026-09-21T07:00:00+00:00',
                    amendedAt: '2026-09-21T07:00:00+00:00',
                },
            ],
        });

        drawSpace(transport);

        expect(await screen.findByText('Standup')).toBeDefined();
        expect(pathsSent(sent(), 'GET').some((path) => path.includes('/calendar?from='))).toBe(true);
        vi.useRealTimers();
    });

    // Through child 2's route, and the entry goes because the list is read again rather than corrected in place.
    it('takes a task mail proposed, and reads the list again rather than editing it', async () => {
        const { transport, sent } = deployment({
            committed: [],
            proposed: [taskCalled('b', 'Send the invoice', { origin: 'Proposed' })],
        });

        drawSpace(transport);
        showView(/^Proposed/u);

        fireEvent.click(await screen.findByRole('button', { name: 'Accept' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Added to your tasks.');
        });

        expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/tasks/b/acceptance'))).toBe(true);
        expect(screen.queryByText('Send the invoice')).toBeNull();
    });

    it('turns a proposal down through the erasure at once, asking nothing first', async () => {
        const { transport, sent } = deployment({
            committed: [],
            proposed: [taskCalled('b', 'Send the invoice', { origin: 'Proposed' })],
        });

        drawSpace(transport);
        showView(/^Proposed/u);

        fireEvent.keyDown(await firstTaskRow(), { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Dismiss task' }));

        await waitFor(() => {
            expect(pathsSent(sent(), 'DELETE').some((path) => path.endsWith('/tasks/b'))).toBe(true);
        });

        expect(screen.queryByText('Delete this task?')).toBeNull();
        expect(screen.getByRole('status').textContent).toBe('Dismissed.');

        await waitFor(() => {
            expect(screen.queryByText('Send the invoice')).toBeNull();
        });
    });

    it('marks a task done and says so', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('checkbox', { name: 'Mark Answer the tender as done' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Marked as done.');
        });

        expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/tasks/a/completion'))).toBe(true);
    });

    it('keeps a box where it was pressed until the list read after the write answers, and then draws the list', async () => {
        const { transport: answering } = deployment();
        let release = (): void => undefined;
        const transport: MailFathomTransport = (request) =>
            request.path.endsWith('/completion')
                ? new Promise<ClientResponse>((resolve) => {
                      release = () => {
                          void answering(request).then(resolve);
                      };
                  })
                : answering(request);

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('checkbox', { name: 'Mark Answer the tender as done' }));

        expect(screen.getByRole('checkbox', { name: 'Mark Answer the tender as not done' })).toHaveProperty(
            'checked',
            true,
        );

        release();

        // This deployment still lists the task as open after the write, and the box is the list's again once that
        // read has answered.
        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Marked as done.');
        });
        expect(await screen.findByRole('checkbox', { name: 'Mark Answer the tender as done' })).toHaveProperty(
            'checked',
            false,
        );
    });

    it('holds every box the selection bar marked done until the list read after those writes answers', async () => {
        const { transport: answering } = deployment({
            committed: [taskCalled('a', 'Answer the tender'), taskCalled('b', 'File the return')],
        });
        const held: (() => void)[] = [];
        const transport: MailFathomTransport = (request) =>
            request.path.endsWith('/completion')
                ? new Promise<ClientResponse>((resolve) => {
                      held.push(() => {
                          void answering(request).then(resolve);
                      });
                  })
                : answering(request);

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        fireEvent.keyDown(screen.getAllByRole('listitem')[0] as Element, { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));
        fireEvent.click(screen.getByRole('button', { name: 'Select File the return' }));
        fireEvent.click(
            within(screen.getByRole('toolbar', { name: 'Acts over the tasks you picked out' })).getByRole('button', {
                name: 'Mark as done',
            }),
        );

        for (const title of ['Answer the tender', 'File the return']) {
            expect(screen.getByRole('checkbox', { name: `Mark ${title} as not done` })).toHaveProperty('checked', true);
        }

        for (const release of held) {
            release();
        }

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Marked as done.');
        });
        expect(await screen.findByRole('checkbox', { name: 'Mark File the return as done' })).toHaveProperty(
            'checked',
            false,
        );
    });

    // Which of the four it was decides what somebody does next, so a batch the deployment refused outright says it
    // rather than leaving them with a sentence that fits every failure equally.
    it('names why the deployment refused where it refused the whole of it', async () => {
        const { transport } = deployment({ refuses: ['a'] });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('checkbox', { name: 'Mark Answer the tender as done' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe(
                'The server did not accept that: unauthorized. Nothing was changed.',
            );
        });
    });

    // A gateway answering for the server may have handed the write on first, so what became of it is not known — and
    // the list read again after it is what says where it stands.
    it('says it is not known whether a write nobody answered was made, never that nothing was', async () => {
        const { transport } = deployment({ leavesUnanswered: ['a'] });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('checkbox', { name: 'Mark Answer the tender as done' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe(
                'The server did not answer, so it is not known whether that was changed.',
            );
        });
    });

    it('refuses every write on a row before it is pressed while the server is out of reach, and says why', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport, { lostServer: true });

        const box = await screen.findByRole('checkbox', {
            name: 'Mark Answer the tender as done - no connection to the MailFathom server.',
        });

        expect(box.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(box);
        fireEvent.keyDown(await firstTaskRow(), { key: 'ContextMenu' });

        const done = screen.getByRole('menuitem', { name: 'Mark as done - no connection to the MailFathom server.' });

        expect(done.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(done);

        expect(pathsSent(sent(), 'PUT')).toStrictEqual([]);
        expect(pathsSent(sent(), 'POST')).toStrictEqual([]);
    });

    // The acceptance the screen exists for: the arrangement is an offer, and a task is planned only by the control that
    // says so — each placed task revised with its plan, and no calendar event of its own written for any of them.
    it('plans each placement only once "Plan these" is pressed, and writes no calendar event for it', async () => {
        const { transport, sent } = deployment({
            committed: [taskCalled('a', 'Answer the tender', { estimatedMinutes: 30 })],
            layout: {
                arranged: true,
                placements: [{ taskId: 'a', startAt: '2026-09-21T09:00:00+00:00', minutes: 45 }],
                notToday: [],
            },
        });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Lay out today' }));

        const accept = await screen.findByRole('button', { name: 'Plan these' });

        expect(pathsSent(sent(), 'PUT')).toStrictEqual([]);

        fireEvent.click(accept);

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('1 task planned into today.');
        });

        expect(pathsSent(sent(), 'PUT')).toStrictEqual(['https://mail.example.invalid/api/client/tasks/a']);
        expect(JSON.parse(sent().find((request) => request.method === 'PUT')?.body ?? 'null')).toMatchObject({
            title: 'Answer the tender',
            estimatedMinutes: 30,
            plan: { startAt: '2026-09-21T09:00:00+00:00', minutes: 45 },
        });
        expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/calendar'))).toBe(false);
    });

    it('puts an arrangement down on "Not now" without planning any of it', async () => {
        const { transport, sent } = deployment({
            layout: {
                arranged: true,
                placements: [{ taskId: 'a', startAt: '2026-09-21T09:00:00+00:00', minutes: 45 }],
                notToday: [],
            },
        });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Lay out today' }));

        fireEvent.click(await screen.findByRole('button', { name: 'Not now' }));

        expect(pathsSent(sent(), 'PUT')).toStrictEqual([]);
        expect(screen.getByRole('button', { name: 'Lay out today' })).toBeDefined();
    });

    it('lays the day out at once where the screen was opened to lay it out', async () => {
        const { transport, sent } = deployment();

        render(
            <LocalizationProvider>
                <ConnectionStatusContext value={serverAnswering}>
                    <TasksSpace
                        session={session}
                        transport={transport}
                        asksDeployment
                        readsCalendar
                        onOpenMessage={undefined}
                        openLayoutOffer
                    />
                </ConnectionStatusContext>
            </LocalizationProvider>,
        );

        await waitFor(() => {
            expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/tasks/today/layout'))).toBe(true);
        });
    });

    // Every space stays mounted while it is aside, so the calendar's *Plan my day* reaches a screen already standing.
    it('lays the day out on Today when a screen already standing is asked to', async () => {
        const { transport, sent } = deployment();
        const drawn = (openLayoutOffer: boolean) => (
            <LocalizationProvider>
                <ConnectionStatusContext value={serverAnswering}>
                    <TasksSpace
                        session={session}
                        transport={transport}
                        asksDeployment
                        readsCalendar
                        onOpenMessage={undefined}
                        openLayoutOffer={openLayoutOffer}
                    />
                </ConnectionStatusContext>
            </LocalizationProvider>
        );

        const { rerender } = render(drawn(false));

        fireEvent.click(await screen.findByRole('radio', { name: /^Upcoming/u }));
        expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/tasks/today/layout'))).toBe(false);

        rerender(drawn(true));

        await waitFor(() => {
            expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/tasks/today/layout'))).toBe(true);
        });
        expect(screen.getByRole<HTMLInputElement>('radio', { name: /^Today/u }).checked).toBe(true);
    });

    it('dropping a task on 14:00 plans it from 14:00 for its estimate', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-09-24T06:00:00Z'));

        try {
            const { transport, sent } = deployment({
                committed: [taskCalled('a', 'Answer the tender', { estimatedMinutes: 45 })],
            });

            drawSpace(transport);

            const row = (await screen.findByText('Answer the tender')).closest('li[draggable="true"]');
            const hour = screen.getByText('2:00 PM', { exact: true }).closest('li');
            const carried = new Map<string, string>();
            const dataTransfer = {
                effectAllowed: 'none',
                get types() {
                    return [...carried.keys()];
                },
                setData: (type: string, value: string) => carried.set(type, value),
                getData: (type: string) => carried.get(type) ?? '',
            };

            expect(row).not.toBeNull();
            expect(hour).not.toBeNull();

            fireEvent.dragStart(row ?? document.body, { dataTransfer });
            fireEvent.dragOver(hour ?? document.body, { dataTransfer });
            fireEvent.drop(hour ?? document.body, { dataTransfer });

            await waitFor(() => {
                expect(pathsSent(sent(), 'PUT')).toStrictEqual(['https://mail.example.invalid/api/client/tasks/a']);
            });

            expect(JSON.parse(sent().find((request) => request.method === 'PUT')?.body ?? 'null')).toMatchObject({
                title: 'Answer the tender',
                plan: { startAt: '2026-09-24T12:00:00.000Z', minutes: 45 },
            });
        } finally {
            vi.useRealTimers();
        }
    });

    it('plans a task for half an hour where nobody said how long it takes', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-09-24T06:00:00Z'));

        try {
            const { transport, sent } = deployment();

            drawSpace(transport);

            const row = (await screen.findByText('Answer the tender')).closest('li[draggable="true"]');
            const hour = screen.getByText('9:00 AM', { exact: true }).closest('li');
            const dataTransfer = {
                types: ['application/x-mailfathom-task'],
                setData: () => undefined,
                getData: () => 'a',
            };

            fireEvent.dragStart(row ?? document.body, { dataTransfer });
            fireEvent.drop(hour ?? document.body, { dataTransfer });

            await waitFor(() => {
                expect(pathsSent(sent(), 'PUT')).toHaveLength(1);
            });

            expect(JSON.parse(sent().find((request) => request.method === 'PUT')?.body ?? 'null')).toMatchObject({
                plan: { startAt: '2026-09-24T07:00:00.000Z', minutes: 30 },
            });
        } finally {
            vi.useRealTimers();
        }
    });

    it('plans a task from the Plan on its row, and lets no row be dragged under a finger', async () => {
        const declared = window.matchMedia.bind(window);

        Object.defineProperty(window, 'matchMedia', {
            configurable: true,
            value: (query: string) => ({
                matches: query === '(pointer: coarse)' || declared(query).matches,
                media: query,
                addEventListener: () => undefined,
                removeEventListener: () => undefined,
            }),
        });

        const { transport, sent } = deployment();

        drawSpace(transport);

        const plan = await screen.findByRole('button', { name: 'Plan' });

        expect(screen.getByText('Answer the tender').closest('li')?.getAttribute('draggable')).not.toBe('true');

        fireEvent.click(plan);

        const dialog = screen.getByRole('dialog', { name: 'Plan a time' });

        fireEvent.click(within(dialog).getByRole('button', { name: /^9:00 AM/u }));
        fireEvent.click(within(dialog).getByRole('button', { name: 'Plan' }));

        await waitFor(() => {
            expect(pathsSent(sent(), 'PUT')).toStrictEqual(['https://mail.example.invalid/api/client/tasks/a']);
        });

        expect(JSON.parse(sent().find((request) => request.method === 'PUT')?.body ?? 'null')).toMatchObject({
            estimatedMinutes: 30,
            plan: { minutes: 30 },
        });
    });

    // One write of a plan is out per task at a time, and every part of the screen draws the plan pressed rather than
    // the one last read — so a second drop is written after the first rather than beside it, and is what is said.
    it('writes a second drop once the first has answered, draws each where it was dropped, and names the later one', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-09-24T06:00:00Z'));

        const { transport, sent, letThrough } = deployment({ holdsTaskWrites: true });

        drawSpace(transport);
        await screen.findByText('Answer the tender');

        dragOnto('Answer the tender', '2:00 PM');

        expect(within(hourOfTheDay('2:00 PM')).getByText('Answer the tender')).toBeDefined();

        dragOnto('Answer the tender', '9:00 AM');

        expect(within(hourOfTheDay('9:00 AM')).getByText('Answer the tender')).toBeDefined();
        expect(pathsSent(sent(), 'PUT')).toHaveLength(1);

        letThrough();

        await waitFor(() => {
            expect(pathsSent(sent(), 'PUT')).toHaveLength(2);
        });

        const plans = sent()
            .filter((request) => request.method === 'PUT')
            .map((request) => (JSON.parse(request.body ?? 'null') as { readonly plan: unknown }).plan);

        expect(plans).toStrictEqual([
            { startAt: '2026-09-24T12:00:00.000Z', minutes: 30 },
            { startAt: '2026-09-24T07:00:00.000Z', minutes: 30 },
        ]);

        letThrough();

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('“Answer the tender” planned at 9:00 AM.');
        });
        expect(pathsSent(sent(), 'PUT')).toHaveLength(2);
    });

    it('opens a task whose plan is being written on the plan just pressed, and restates that plan', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-09-24T06:00:00Z'));

        const { transport, sent } = deployment({ holdsTaskWrites: true });

        drawSpace(transport);
        await screen.findByText('Answer the tender');

        dragOnto('Answer the tender', '2:00 PM');
        fireEvent.click(within(await firstTaskRow()).getByRole('button', { name: 'Answer the tender' }));

        const dialog = screen.getByRole('dialog', { name: 'Task' });

        expect(within(dialog).getByRole('button', { name: 'Move' })).toBeDefined();

        fireEvent.click(within(dialog).getByRole('radio', { name: 'Tomorrow' }));

        const restated = sent().filter((request) => request.method === 'PUT');

        expect(restated).toHaveLength(2);
        expect(JSON.parse(restated[1]?.body ?? 'null')).toMatchObject({
            dueOn: '2026-09-25',
            plan: { startAt: '2026-09-24T12:00:00.000Z', minutes: 30 },
        });
    });

    // The plan dialog offers more days than today, so what it is read against for a clash is every day it offers.
    it('warns of a clash with a meeting on a later day the plan dialog offers', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-09-24T06:00:00Z'));

        const { transport } = deployment({
            events: [
                {
                    id: 'e',
                    title: 'Supplier call',
                    start: '2026-09-25T08:00:00+00:00',
                    end: '2026-09-25T09:00:00+00:00',
                    isAllDay: false,
                    reminders: [],
                    remindsAt: [],
                    origin: 'Asserted',
                    sourceMessage: null,
                    recordedAt: '2026-09-21T07:00:00+00:00',
                    amendedAt: '2026-09-21T07:00:00+00:00',
                },
            ],
        });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Plan' }));

        const planning = screen.getByRole('dialog', { name: 'Plan a time' });

        fireEvent.click(within(planning).getByRole('radio', { name: 'Tomorrow' }));
        fireEvent.click(await within(planning).findByRole('button', { name: /^10:00 AM/u }));

        expect(
            await within(planning).findByText('Clashes with Supplier call at 10:00 AM. You can still plan it.'),
        ).toBeDefined();
    });

    it('lays the day out from the toolbar, on Today, from whichever view the list was in', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport);
        await screen.findByText('Answer the tender');
        showView(/^Upcoming/u);

        fireEvent.click(await screen.findByRole('button', { name: 'Lay out the day' }));

        await waitFor(() => {
            expect(pathsSent(sent(), 'POST').some((path) => path.endsWith('/tasks/today/layout'))).toBe(true);
        });
        expect(screen.getByRole('radio', { name: /^Today/u, checked: true })).toBeDefined();
    });

    // A drop is a write, so it is held exactly as the row's *Plan* is while the server is out of reach.
    it('lets no row be dragged onto the day while writes are refused', async () => {
        const { transport } = deployment();

        drawSpace(transport, { lostServer: true });

        const row = await firstTaskRow();

        expect(row.getAttribute('draggable')).not.toBe('true');
    });

    it('offers no arrangement where the deployment arranges no day', async () => {
        const { transport } = deployment({ arranges: false });

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        expect(screen.queryByRole('button', { name: 'Lay out today' })).toBeNull();
    });

    it('asks nothing about arranging a day where this credential may not ask the deployment anything', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport, { asksDeployment: false });

        await screen.findByText('Answer the tender');

        expect(pathsSent(sent(), 'GET').some((path) => path.endsWith('/tasks/today/layout'))).toBe(false);
        expect(screen.queryByRole('button', { name: 'Lay out today' })).toBeNull();
    });

    it('says the provider allowance is spent rather than reporting a failure', async () => {
        const { transport } = deployment({ layoutStatus: 429, layout: {} });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Lay out today' }));

        await waitFor(() => {
            expect(screen.getByText('Laying out days is used up for now.')).toBeDefined();
        });
    });

    it('replaces the toolbar with the selection bar while tasks are picked out, and leaves it through its own control', async () => {
        const { transport } = deployment();

        drawSpace(transport);

        fireEvent.keyDown(await firstTaskRow(), { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));

        expect(screen.getByRole('toolbar', { name: 'Acts over the tasks you picked out' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Show done' })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Cancel selection' }));

        expect(screen.getByRole('button', { name: 'Show done' })).toBeDefined();
    });

    it('confirms a destructive act over several rows before performing any of it', async () => {
        const { transport, sent } = deployment({
            committed: [taskCalled('a', 'Answer the tender'), taskCalled('b', 'File the return')],
        });

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        fireEvent.keyDown(screen.getAllByRole('listitem')[0] as Element, { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));
        fireEvent.click(screen.getByRole('button', { name: 'Select File the return' }));
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(screen.getByText('Delete 2 tasks?')).toBeDefined();
        expect(pathsSent(sent(), 'DELETE')).toStrictEqual([]);
    });

    it('counts what the deployment refused in a batch rather than calling the whole of it a failure', async () => {
        const { transport } = deployment({
            committed: [taskCalled('a', 'Answer the tender'), taskCalled('b', 'File the return')],
            refuses: ['b'],
        });

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        fireEvent.keyDown(screen.getAllByRole('listitem')[0] as Element, { key: 'ContextMenu' });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));
        fireEvent.click(screen.getByRole('button', { name: 'Select File the return' }));
        fireEvent.click(screen.getByRole('button', { name: 'Mark as done' }));

        await waitFor(() => {
            expect(screen.getByText('The server refused one of these. The rest went through.')).toBeDefined();
        });
    });

    it('hides what is done until the toolbar is asked to show it', async () => {
        const { transport } = deployment({
            committed: [taskCalled('a', 'Answer the tender', { completed: true })],
        });

        drawSpace(transport);

        await waitFor(() => {
            expect(screen.getByText('Everything on the list is done. Turn on “Show done” to see it.')).toBeDefined();
        });

        fireEvent.click(screen.getByRole('button', { name: 'Show done' }));

        expect(screen.getByText('Answer the tender')).toBeDefined();
    });

    it('writes a task down from the form, as one the person asserted', async () => {
        const { transport, sent } = deployment({ committed: [] });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'New task' }));
        fireEvent.change(screen.getByLabelText('Task description'), {
            target: { value: 'Answer the tender' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Add task' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Written down.');
        });

        const written = sent().find((request) => request.method === 'POST' && request.path.endsWith('/tasks'));

        expect(JSON.parse(written?.body ?? 'null')).toStrictEqual({
            title: 'Answer the tender',
            dueOn: null,
            reminders: [],
            dueDayOffsetMinutes: null,
            sourceMessageId: null,
            direction: 'Owed',
            estimatedMinutes: null,
            plan: null,
            anchor: null,
            counterpart: null,
        });
    });

    // The panel is the one place a reminder is decided, and what it answers is written as it is answered: there is no
    // *Save* behind it, so the whole record goes back with the leads the reader chose.
    it('writes what announces a task as the whole record, with the offset its due day runs in', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Add a reminder to Answer the tender' }));
        fireEvent.click(screen.getByRole('button', { name: '1 day before' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Reminders saved.');
        });

        const written = sent().find((request) => request.method === 'PUT');

        expect(written?.path).toBe('https://mail.example.invalid/api/client/tasks/a');
        expect(JSON.parse(written?.body ?? 'null')).toStrictEqual({
            title: 'Answer the tender',
            dueOn: '2026-09-24',
            reminders: [24 * 60],
            dueDayOffsetMinutes: 120,
            sourceMessageId: null,
            direction: 'Owed',
            estimatedMinutes: null,
            plan: null,
            anchor: null,
            counterpart: null,
        });
    });

    // The route takes the whole commitment, so a revision that restated none of it would drop a plan the person made
    // elsewhere — which is what left a task planned into the calendar showing as unplanned after its reminders moved.
    it('restates the plan, anchor and counterpart a task already holds when its reminders are written', async () => {
        const commitment = {
            direction: 'Awaited',
            estimatedMinutes: 30,
            plan: { startAt: '2026-09-24T08:00:00+00:00', minutes: 45 },
            anchor: { kind: 'Thread', id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a94', label: 'Tender thread' },
            counterpart: { address: 'dana@example.invalid', displayName: 'Dana' },
        };
        const { transport, sent } = deployment({
            committed: [taskCalled('a', 'Answer the tender', commitment)],
        });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Add a reminder to Answer the tender' }));
        fireEvent.click(screen.getByRole('button', { name: '1 day before' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Reminders saved.');
        });

        const written = sent().find((request) => request.method === 'PUT');

        expect(JSON.parse(written?.body ?? 'null')).toMatchObject(commitment);
    });

    it('states a task announcing nothing as an empty set rather than as a field left out', async () => {
        const { transport, sent } = deployment({
            committed: [taskCalled('a', 'Answer the tender', { reminders: [60], remindsAt: ['2026-09-24T08:00:00Z'] })],
        });

        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Reminders for Answer the tender: 1 reminder' }));
        fireEvent.click(screen.getByRole('button', { name: 'Turn all off' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Reminders saved.');
        });

        expect(JSON.parse(sent().find((request) => request.method === 'PUT')?.body ?? 'null')).toMatchObject({
            reminders: [],
            dueDayOffsetMinutes: 120,
        });
    });

    it('walks to the next view on a swipe across the list', async () => {
        const { transport } = deployment();

        drawSpace(transport);

        const row = await firstTaskRow();

        fireEvent.pointerDown(row, { pointerId: 1, pointerType: 'touch', clientX: 300, clientY: 200 });
        fireEvent.pointerMove(row, { pointerId: 1, pointerType: 'touch', clientX: 300 - swipeDistance, clientY: 200 });
        fireEvent.pointerUp(row, { pointerId: 1, pointerType: 'touch', clientX: 300 - swipeDistance, clientY: 200 });

        expect(screen.getByRole('radio', { name: /^Upcoming/u, checked: true })).toBeDefined();
    });

    // Two threads asked for one after the other: the first answering last, or at all, opens nothing, since the reader
    // has since asked for the second.
    it('opens only the thread asked for last when an earlier read answers after it', async () => {
        const onOpenMessage = vi.fn();
        const thread = (id: string, label: string) => ({ anchor: { kind: 'Thread', id, label } });
        const { transport } = deployment({
            committed: [
                taskCalled('a', 'Answer the tender', thread('0190f3c2-8a7b-7cde-9f01-23456789ab01', 'Tender thread')),
                taskCalled('b', 'Send the invoice', thread('0190f3c2-8a7b-7cde-9f01-23456789ab02', 'Invoice thread')),
            ],
        });
        const threadReads: ((answer: ClientResponse) => void)[] = [];
        const holding: MailFathomTransport = (request) =>
            request.path.includes('/threads/')
                ? new Promise((resolve) => {
                      threadReads.push(resolve);
                  })
                : transport(request);

        drawSpace(holding, { onOpenMessage });

        fireEvent.click(await screen.findByRole('button', { name: 'Tender thread' }));
        fireEvent.click(screen.getByRole('button', { name: 'Invoice thread' }));

        await waitFor(() => {
            expect(threadReads).toHaveLength(2);
        });

        threadReads[0]?.({ ...answered, body: JSON.stringify(conversation()) });
        threadReads[1]?.({ status: 503, headers: {}, body: '' });

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('The thread could not be opened.');
        });

        expect(onOpenMessage).not.toHaveBeenCalled();
    });

    // A task cites a message and mail is read in the Mail space, so opening one is the frame's act rather than this
    // screen's.
    it('hands the message a task cites to the frame rather than drawing it here', async () => {
        const onOpenMessage = vi.fn();
        const { transport } = deployment({
            committed: [taskCalled('a', 'Answer the tender', { sourceMessageId: 'message' })],
        });

        drawSpace(transport, { onOpenMessage });

        fireEvent.click(await screen.findByRole('button', { name: 'Open the message this came from' }));

        expect(onOpenMessage).toHaveBeenCalledWith('message');
    });

    // The calendar is a record of its own beside the task list, read under its own grant. A credential holding the
    // task list alone therefore meets the list, with no day beside it and no request the deployment would refuse.
    it('draws the list alone and asks nothing of the calendar where this credential reads no calendar', async () => {
        const { transport, sent } = deployment();

        drawSpace(transport, { readsCalendar: false, asksDeployment: false });

        expect(await screen.findByText('Answer the tender')).toBeDefined();
        expect(screen.queryByRole('heading', { name: /^Today · /u })).toBeNull();
        expect(sent().some((request) => request.path.includes('/calendar'))).toBe(false);
    });

    it('cites no message where this credential reads no mail', async () => {
        const { transport } = deployment({
            committed: [taskCalled('a', 'Answer the tender', { sourceMessageId: 'message' })],
        });

        drawSpace(transport, { readsMail: false });

        expect(await screen.findByText('Answer the tender')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Open the message this came from' })).toBeNull();
    });

    it('offers the way to write a task down at a phone width too, where the toolbar has nowhere to stand', async () => {
        atWidth(phone);

        const { transport } = deployment();

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        expect(screen.getByRole('button', { name: 'New task' })).toBeDefined();
    });

    // The sidebar stands beside the list where there is room and under it where there is not, and the difference
    // between those two is a stylesheet's. What a test here can say is the part that would be a defect either way:
    // that both panels are still in the document at the narrowest composition rather than dropped out of it.
    it('keeps both sidebar panels reachable at a phone width rather than dropping them', async () => {
        atWidth(phone);

        const { transport } = deployment();

        drawSpace(transport);

        await screen.findByText('Answer the tender');

        expect(screen.getByRole('heading', { name: /^Today · /u })).toBeDefined();
        expect(screen.getByRole('heading', { name: 'Day capacity' })).toBeDefined();
    });
});
