// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { swipeDistance } from '../controls/swipeAcross';
import { LocalizationProvider } from '../localization/Localization';
import { CalendarSpace } from './CalendarSpace';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// Every day this screen computes is a day in the reader's own zone, so the zone is pinned rather than inherited: a
// run in UTC would draw a different week from a run in Warsaw and the assertions below would follow the machine.
const zoneBefore = process.env['TZ'];

// jsdom evaluates no media query, so the composition a test is about is stated as a width and read back out of the
// query itself, exactly as `people/PeopleSpace.test.tsx` does it.
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

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
    atWidth(desktop);
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;

    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

// A Thursday in the middle of a week that is wholly inside one month, so the week and the month cover different spans
// and a test can tell which one is drawn.
const standing = new Date(2026, 8, 24, 10);

function eventCalled(
    id: string,
    title: string,
    start: string,
    {
        end = null,
        origin = 'Asserted',
        sourceMessage = null,
        reminders = [],
    }: { end?: string | null; origin?: string; sourceMessage?: string | null; reminders?: readonly number[] } = {},
): unknown {
    return {
        id,
        title,
        start,
        end,
        isAllDay: false,
        reminders,
        remindsAt: [],
        origin,
        sourceMessage,
        recordedAt: '2026-09-01T09:00:00+02:00',
        amendedAt: '2026-09-01T09:00:00+02:00',
    };
}

const review = eventCalled('review', 'Review with Anna', '2026-09-24T09:00:00+02:00', {
    end: '2026-09-24T10:00:00+02:00',
});

const standup = eventCalled('standup', 'Stand-up', '2026-09-25T09:30:00+02:00');

const proposed = eventCalled('proposed', 'Lunch with Celina', '2026-09-24T13:00:00+02:00', {
    origin: 'Proposed',
    sourceMessage: 'message-4',
});

const answered = { status: 200, headers: {} } as const;

// One deployment behind the screen: a window of events, the capability behind the description field, and the four
// writes. A test states what a write answers and reads back what the screen did about it.
function deployment({
    events = [review, standup, proposed],
    readsDescriptions = true,
    refuses = [],
    refusesAcceptance = false,
    leavesUnanswered = false,
}: {
    events?: readonly unknown[];

    /** Whether this deployment turns a typed description into an event at all. */
    readsDescriptions?: boolean;

    /** Whose deletion this deployment will not perform, which is how a batch is made to half succeed. */
    refuses?: readonly string[];

    /** Whether taking a proposal onto the calendar fails, which is what the card coming back is about. */
    refusesAcceptance?: boolean;

    /** Whether a gateway answers every write for the server, which says nothing about whether it was handed on. */
    leavesUnanswered?: boolean;
} = {}): { readonly transport: MailFathomTransport; readonly sent: () => readonly ClientRequest[] } {
    const sent: ClientRequest[] = [];

    return {
        sent: () => sent,
        transport: (request) => {
            sent.push(request);

            const { method, path } = request;
            let answer: ClientResponse;

            if (path.includes('/calendar/drafts')) {
                answer = { ...answered, body: JSON.stringify({ readsDescriptions }) };
            } else if (method !== 'GET' && leavesUnanswered) {
                answer = { status: 504, headers: {}, body: '' };
            } else if (method === 'DELETE') {
                answer = refuses.some((event) => path.endsWith(`/calendar/${event}`))
                    ? { status: 403, headers: {}, body: '' }
                    : { status: 204, headers: {}, body: '' };
            } else if (method === 'POST' && path.endsWith('/acceptance') && refusesAcceptance) {
                answer = { status: 403, headers: {}, body: '' };
            } else if (method === 'POST' || method === 'PUT') {
                answer = { ...answered, body: JSON.stringify(review) };
            } else {
                answer = { ...answered, body: JSON.stringify({ events }) };
            }

            return Promise.resolve(answer);
        },
    };
}

/** A finger landing on what the tabs show and swiped a whole swipe to the left. */
function swipeLeftAcross(surface: Element): void {
    fireEvent.pointerDown(surface, { pointerId: 1, pointerType: 'touch', clientX: 300, clientY: 200 });
    fireEvent.pointerMove(surface, { pointerId: 1, pointerType: 'touch', clientX: 300 - swipeDistance, clientY: 200 });
    fireEvent.pointerUp(surface, { pointerId: 1, pointerType: 'touch', clientX: 300 - swipeDistance, clientY: 200 });
}

function drawSpace(transport: MailFathomTransport, onOpenMessage = vi.fn()): void {
    render(
        <LocalizationProvider>
            <CalendarSpace session={session} transport={transport} now={() => standing} onOpenMessage={onOpenMessage} />
        </LocalizationProvider>,
    );
}

function windowsAsked(sent: readonly ClientRequest[]): readonly ClientRequest[] {
    return sent.filter((request) => request.method === 'GET' && !request.path.includes('/drafts'));
}

describe('CalendarSpace', () => {
    it('opens on the week the reader is standing in and reads that span once', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();
        expect(screen.getByRole('heading', { name: 'September 2026' })).toBeDefined();

        const asked = windowsAsked(sent());
        expect(asked).toHaveLength(1);

        // Monday to Monday, which is the week the design draws and the window the deployment is asked for.
        expect(asked[0]?.path).toContain(`from=${encodeURIComponent(new Date(2026, 8, 21).toISOString())}`);
        expect(asked[0]?.path).toContain(`until=${encodeURIComponent(new Date(2026, 8, 28).toISOString())}`);
    });

    it('draws what is on the calendar in the views and what mail proposed beside them, never one in the other', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();
        expect(screen.queryByRole('option', { name: /Lunch with Celina/u })).toBeNull();

        const proposals = screen.getByRole('region', { name: 'Detected in mail' });
        expect(proposals.textContent).toContain('Lunch with Celina');
    });

    it('reads the new span when the reader moves, rather than filtering what it already holds', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Later' }));

        await waitFor(() => {
            expect(windowsAsked(sent())).toHaveLength(2);
        });

        expect(windowsAsked(sent())[1]?.path).toContain(
            `from=${encodeURIComponent(new Date(2026, 8, 28).toISOString())}`,
        );
    });

    it('reads the month when the month view is chosen, which covers whole weeks either side of it', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();

        fireEvent.click(screen.getByRole('radio', { name: 'Month' }));

        await waitFor(() => {
            expect(windowsAsked(sent())).toHaveLength(2);
        });

        // The thirty-first of August is the Monday of the week the first of September falls in.
        expect(windowsAsked(sent())[1]?.path).toContain(
            `from=${encodeURIComponent(new Date(2026, 7, 31).toISOString())}`,
        );
    });

    it('steps to the next view when a finger is swiped to the left across the calendar', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        swipeLeftAcross(await screen.findByRole('option', { name: /Review with Anna/u }));

        expect(screen.getByRole('radio', { name: 'Month', checked: true })).toBeDefined();
    });

    it('draws the week as an agenda below the width seven columns need, and says that is what it did', async () => {
        atWidth(phone);
        const { transport } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();
        expect(
            screen.getByText('This width has no room for the columns, so the same days are listed instead.'),
        ).toBeDefined();
    });

    it('takes a proposal onto the calendar and takes the card away before the read comes back', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByText('Lunch with Celina')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Add' }));

        expect(screen.queryByText('Lunch with Celina')).toBeNull();

        await waitFor(() => {
            expect(sent().some((request) => request.path.endsWith('/calendar/proposed/acceptance'))).toBe(true);
        });
    });

    it('puts a proposal back where the write did not happen, rather than leaving it answered', async () => {
        const { transport } = deployment({ refusesAcceptance: true });
        drawSpace(transport);

        expect(await screen.findByText('Lunch with Celina')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Add' }));

        expect(await screen.findByText('The server did not take that, so nothing was changed.')).toBeDefined();
        expect(screen.getByText('Lunch with Celina')).toBeDefined();
    });

    // A gateway answering for the server may have handed the write on first, so what became of it is not known: the
    // sentence says that much, and never that nothing was changed.
    it('says it is not known whether a write nobody answered was made, never that nothing was', async () => {
        const { transport } = deployment({ leavesUnanswered: true });
        drawSpace(transport);

        expect(await screen.findByText('Lunch with Celina')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Add' }));

        expect(
            await screen.findByText('The server did not answer, so it is not known whether that was changed.'),
        ).toBeDefined();
        expect(screen.queryByText(/nothing was changed/u)).toBeNull();
    });

    it('lets a proposal go by deleting it, and takes the card away as it goes', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByText('Lunch with Celina')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));

        expect(screen.queryByText('Lunch with Celina')).toBeNull();

        await waitFor(() => {
            expect(
                sent().some((request) => request.method === 'DELETE' && request.path.endsWith('/calendar/proposed')),
            ).toBe(true);
        });
    });

    it('draws the span the reader moved to, even where the span they left answers after it', async () => {
        // The two reads are held rather than answered, so the test decides which one arrives first: the ordering of
        // two requests in flight is not guaranteed, and last week's events under this week's heading would read as a
        // rendering defect rather than as the race it is.
        const held: ((answer: ClientResponse) => void)[] = [];

        const transport: MailFathomTransport = (request) =>
            request.path.includes('/calendar/drafts')
                ? Promise.resolve({ ...answered, body: JSON.stringify({ readsDescriptions: false }) })
                : new Promise((answer) => {
                      held.push(answer);
                  });

        drawSpace(transport);

        await waitFor(() => {
            expect(held).toHaveLength(1);
        });

        fireEvent.click(screen.getByRole('button', { name: 'Later' }));

        await waitFor(() => {
            expect(held).toHaveLength(2);
        });

        const arrived = eventCalled('handover', 'Warehouse handover', '2026-09-30T09:00:00+02:00');
        const late = eventCalled('carrier', 'Carrier review', '2026-09-30T14:00:00+02:00');

        held[1]?.({ ...answered, body: JSON.stringify({ events: [arrived] }) });

        expect(await screen.findByRole('option', { name: /Warehouse handover/u })).toBeDefined();

        // The week that was left answers last, and with an event inside the span now on the screen — so an answer
        // applied whatever it was asked for would be visible here rather than merely out of view. It is resolved
        // inside `act` because what is being asserted is that nothing follows it: an absence cannot be waited for, so
        // the render it would have caused is driven to completion first and read afterwards.
        await act(async () => {
            held[0]?.({ ...answered, body: JSON.stringify({ events: [late] }) });

            // The answer is an already-resolved promise, so one turn of the microtask queue is the whole of what
            // *after it arrived* means — and the render it would have caused has happened by the time `act` returns.
            await Promise.resolve();
        });

        expect(screen.getByRole('option', { name: /Warehouse handover/u })).toBeDefined();
        expect(screen.queryByRole('option', { name: /Carrier review/u })).toBeNull();
    });

    it('picks an entry out on a modifier-held press and says how many are held', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        const entry = await screen.findByRole('option', { name: /Review with Anna/u });

        fireEvent.click(entry, { ctrlKey: true });

        expect(entry.getAttribute('aria-selected')).toBe('true');
        expect(screen.getByRole('toolbar', { name: 'What to do with the selected events' })).toBeDefined();
        expect(screen.getByText('1 selected')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'New event' })).toBeNull();
    });

    it('opens the entry a plain press lands on rather than picking it out', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Review with Anna/u }));

        expect(await screen.findByRole('heading', { name: 'Review with Anna' })).toBeDefined();
        expect(screen.queryByText('1 selected')).toBeNull();
    });

    it('offers the same two gestures from the keyboard, a selection being unworkable without them', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        const entry = await screen.findByRole('option', { name: /Review with Anna/u });

        fireEvent.keyDown(entry, { key: 'Enter', ctrlKey: true });

        expect(entry.getAttribute('aria-selected')).toBe('true');
    });

    it('asks before deleting several, naming how many, and then deletes each of them', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Review with Anna/u }), { ctrlKey: true });
        fireEvent.click(screen.getByRole('option', { name: /Stand-up/u }), { ctrlKey: true });

        expect(screen.getByText('2 selected')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(screen.getByText('Delete 2 events?')).toBeDefined();
        expect(screen.getByText('They leave the calendar and cannot be brought back.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Delete from the calendar' }));

        await waitFor(() => {
            expect(sent().filter((request) => request.method === 'DELETE')).toHaveLength(2);
        });
    });

    it('says so where one of a batch was refused rather than reporting the whole of it as done', async () => {
        const { transport } = deployment({ refuses: ['standup'] });
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Review with Anna/u }), { ctrlKey: true });
        fireEvent.click(screen.getByRole('option', { name: /Stand-up/u }), { ctrlKey: true });
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
        fireEvent.click(screen.getByRole('button', { name: 'Delete from the calendar' }));

        expect(await screen.findByText('Some of them were not deleted.')).toBeDefined();
    });

    it('offers the event"s own menu on a right press, with the four acts this client can perform', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.contextMenu(await screen.findByRole('option', { name: /Review with Anna/u }));

        expect(await screen.findByRole('menuitem', { name: 'Select events' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Open the event' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Reminders' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Delete the event' })).toBeDefined();
    });

    it('opens the event with what announces it in front, where the menu asked for the reminders', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.contextMenu(await screen.findByRole('option', { name: /Review with Anna/u }));
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Reminders' }));

        expect(await screen.findByRole('dialog', { name: 'Reminders' })).toBeDefined();
    });

    it('amends the event inside the dialog already drawing it, rather than opening that dialog a second time', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Review with Anna/u }));

        const opened = within(await screen.findByRole('dialog', { name: 'Review with Anna' }));

        fireEvent.click(opened.getByRole('button', { name: 'Edit' }));
        fireEvent.change(opened.getByLabelText('Title'), { target: { value: 'Review with Celina' } });
        fireEvent.click(opened.getByRole('button', { name: 'Save' }));

        // The write replaces the event the dialog is drawing while it stays open, which is what asking the platform to
        // open it again would refuse to do.
        expect(await screen.findByText('Saved.')).toBeDefined();
    });

    it('opens a new event on the day the reader walked to, not on the one the screen was drawn on', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Review with Anna/u })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Later' }));
        fireEvent.click(await screen.findByRole('button', { name: 'New event' }));

        // Scoped to the dialog, the view control beside it carrying the same word: what is read here is the field the
        // event is written into, and the week walked to opens on its own Monday.
        const writing = within(screen.getByRole('dialog', { name: 'New event' }));

        expect(writing.getByLabelText<HTMLInputElement>('Starts').value).toBe('2026-09-28');
    });

    it('draws the description field only where the deployment reads one', async () => {
        const { transport } = deployment({ readsDescriptions: false });
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'New event' }));

        expect(screen.getByLabelText('Title')).toBeDefined();
        expect(screen.queryByLabelText('Describe the event and MailFathom fills the fields')).toBeNull();
    });

    it('says why the span did not answer and offers the way out', async () => {
        const failing: MailFathomTransport = (request) =>
            Promise.resolve(
                request.path.includes('/drafts')
                    ? { ...answered, body: JSON.stringify({ readsDescriptions: false }) }
                    : { status: 503, headers: {}, body: '' },
            );

        drawSpace(failing);

        expect(
            await screen.findByText('The server did not answer with the calendar. It may be offline or starting.'),
        ).toBeDefined();
        expect(screen.getByRole('button', { name: 'Try again' })).toBeDefined();
    });

    it('says the calendar is empty where the span holds nothing rather than drawing an empty grid in silence', async () => {
        const { transport } = deployment({ events: [] });
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('radio', { name: 'Agenda' }));

        expect(await screen.findByText('Nothing is on the calendar here.')).toBeDefined();
        expect(screen.getByText('Your mail has proposed nothing.')).toBeDefined();
    });
});

// A task as the task routes answer it, owed and open unless a test says otherwise.
function taskCalled(id: string, title: string, held: Record<string, unknown> = {}): unknown {
    return {
        id,
        title,
        dueOn: null,
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        direction: 'Owed',
        estimatedMinutes: 45,
        plan: null,
        anchor: null,
        counterpart: null,
        answer: null,
        ...held,
    };
}

const plannedTask = taskCalled('task-tender', 'Send the counter-proposal', {
    dueOn: '2026-09-25',
    plan: { startAt: '2026-09-24T11:00:00+02:00', minutes: 45 },
});

const proposedTask = taskCalled('task-baseline', 'Ask Marta for the baseline', {
    origin: 'Proposed',
    dueOn: '2026-09-25',
});

// The same deployment with the task routes behind it: the window's tasks, the proposals, the tasks an event carries,
// and every task write answered with the task it names.
function withTasks({
    own = [plannedTask],
    proposed = [proposedTask],
    ofTheEvent = [],
    arrangesDays = true,
    failsTasks = false,
}: {
    own?: readonly unknown[];
    proposed?: readonly unknown[];
    ofTheEvent?: readonly unknown[];

    /** Whether this deployment arranges a day, which *Plan my day* is offered under. */
    arrangesDays?: boolean;

    /** Whether every task read fails while the calendar itself answers. */
    failsTasks?: boolean;
} = {}) {
    const calendar = deployment();

    const transport: MailFathomTransport = (request) => {
        if (!request.path.includes('/tasks')) {
            return calendar.transport(request);
        }

        if (request.path.includes('/tasks/today/layout')) {
            return Promise.resolve({ ...answered, body: JSON.stringify({ arrangesDays }) });
        }

        if (failsTasks && request.method === 'GET') {
            return Promise.resolve({ status: 503, headers: {}, body: '' });
        }

        const page = request.path.includes('/tasks/proposed')
            ? proposed
            : request.path.includes('eventId=')
              ? ofTheEvent
              : own;

        return Promise.resolve(
            request.method === 'GET'
                ? { ...answered, body: JSON.stringify({ tasks: page, nextCursor: null }) }
                : { ...answered, body: JSON.stringify(proposedTask) },
        );
    };

    const sent: ClientRequest[] = [];

    return {
        sent: () => sent,
        transport: (request: ClientRequest) => {
            sent.push(request);

            return transport(request);
        },
    };
}

function drawSpaceWithTasks(transport: MailFathomTransport, onPlanDay = vi.fn()): void {
    render(
        <LocalizationProvider>
            <CalendarSpace
                session={session}
                transport={transport}
                now={() => standing}
                onOpenMessage={vi.fn()}
                readsTasks
                onPlanDay={onPlanDay}
            />
        </LocalizationProvider>,
    );
}

describe('CalendarSpace with tasks', () => {
    it('draws a planned task at its time and a due marker on its day, and a task opens the task dialog', async () => {
        const { transport } = withTasks();
        drawSpaceWithTasks(transport);

        const planned = await screen.findByRole('option', { name: /^Task · .*Send the counter-proposal/u });
        expect(screen.getByRole('option', { name: /^Due · taskSend the counter-proposal/u })).toBeDefined();

        fireEvent.click(planned);

        expect(await screen.findByRole('dialog', { name: 'Task' })).toBeDefined();
        expect(screen.queryByRole('dialog', { name: 'Send the counter-proposal' })).toBeNull();
    });

    it('accepts a proposed task from the column beside the views', async () => {
        const { transport, sent } = withTasks();
        drawSpaceWithTasks(transport);

        const proposals = await screen.findByRole('region', { name: 'Proposed tasks' });
        fireEvent.click(await within(proposals).findByRole('button', { name: 'Accept' }));

        expect(await screen.findByText('No proposals left. Tasks keeps every one you accepted.')).toBeDefined();
        expect(
            sent().some(
                (request) => request.method === 'POST' && request.path.endsWith('/tasks/task-baseline/acceptance'),
            ),
        ).toBe(true);
    });

    it('offers "Plan my day" over the proposed tasks', async () => {
        const onPlanDay = vi.fn();
        const { transport } = withTasks();
        drawSpaceWithTasks(transport, onPlanDay);

        fireEvent.click(await screen.findByRole('button', { name: 'Plan my day' }));

        expect(onPlanDay).toHaveBeenCalledOnce();
    });

    it('offers no "Plan my day" where the deployment arranges no day', async () => {
        const { transport } = withTasks({ arrangesDays: false });
        drawSpaceWithTasks(transport);

        await screen.findByRole('region', { name: 'Proposed tasks' });
        await screen.findByRole('option', { name: /Review with Anna/u });

        expect(screen.queryByRole('button', { name: 'Plan my day' })).toBeNull();
    });

    it('says the tasks of these days failed apart from the calendar, and reads them again', async () => {
        const { transport, sent } = withTasks({ failsTasks: true });
        drawSpaceWithTasks(transport);

        expect(await screen.findByText('The tasks for these days could not be read.')).toBeDefined();
        expect(screen.getByRole('option', { name: /Review with Anna/u })).toBeDefined();

        const readsBefore = sent().filter(
            (request) => request.method === 'GET' && request.path.includes('/tasks?'),
        ).length;
        fireEvent.click(screen.getByRole('button', { name: 'Read again' }));

        await waitFor(() => {
            expect(
                sent().filter((request) => request.method === 'GET' && request.path.includes('/tasks?')).length,
            ).toBeGreaterThan(readsBefore);
        });
    });

    it('says an event another screen asked for is gone when the calendar no longer holds it', async () => {
        const calendar = deployment();
        const transport: MailFathomTransport = (request) =>
            request.path.endsWith('/calendar/gone')
                ? Promise.resolve({ status: 404, headers: {}, body: '' })
                : calendar.transport(request);

        render(
            <LocalizationProvider>
                <CalendarSpace
                    session={session}
                    transport={transport}
                    now={() => standing}
                    onOpenMessage={vi.fn()}
                    eventAsked={{ eventId: 'gone' }}
                />
            </LocalizationProvider>,
        );

        expect(await screen.findByText('That event is no longer on the calendar.')).toBeDefined();
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('shows the tasks an event carries, and adds one prepared for it and due on its day', async () => {
        const { transport, sent } = withTasks({ ofTheEvent: [] });
        drawSpaceWithTasks(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Review with Anna/u }));

        const opened = within(await screen.findByRole('region', { name: 'Tasks for this event' }));
        expect(
            await opened.findByText(
                'Nothing to prepare yet. A task added here belongs to this event and is due on its day.',
            ),
        ).toBeDefined();
        expect(sent().some((request) => request.path.includes('eventId=review'))).toBe(true);

        fireEvent.click(opened.getByRole('button', { name: 'Add a task' }));

        const writing = within(screen.getByRole('dialog', { name: 'New task' }));
        expect(writing.getByLabelText<HTMLInputElement>('Task description').value).toBe('Prepare for Review with Anna');
        expect(writing.getByLabelText<HTMLInputElement>('Due date').value).toBe('2026-09-24');

        fireEvent.click(writing.getByRole('button', { name: 'Add task' }));

        await waitFor(() => {
            expect(sent().some((request) => request.method === 'POST' && request.path.endsWith('/tasks'))).toBe(true);
        });

        const recorded = sent().find((request) => request.method === 'POST' && request.path.endsWith('/tasks'));
        expect(JSON.parse(recorded?.body ?? '{}')).toMatchObject({
            title: 'Prepare for Review with Anna',
            dueOn: '2026-09-24',
            anchor: { kind: 'Event', id: 'review', label: 'Review with Anna' },
        });
    });
});
