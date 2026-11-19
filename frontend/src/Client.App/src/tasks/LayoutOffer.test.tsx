// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    CalendarEvent,
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    PersonalTask,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { LayoutOffer } from './LayoutOffer';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function taskCalled(id: string, title: string, held: Partial<PersonalTask> = {}): PersonalTask {
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
        estimatedMinutes: 30,
        plan: null,
        anchor: null,
        counterpart: null,
        answer: null,
        ...held,
    };
}

const room = taskCalled('task-room', 'Book the meeting room');
const reply = taskCalled('task-reply', 'Reply to Marta', {
    plan: { startAt: '2026-09-23T15:00:00+00:00', minutes: 20 },
});
const audit = taskCalled('task-audit', 'Prepare the IT contract list for audit');

const arranged = {
    arranged: true,
    placements: [
        { taskId: 'task-room', startAt: '2026-09-24T10:00:00+00:00', minutes: 30 },
        { taskId: 'task-reply', startAt: '2026-09-24T16:00:00+00:00', minutes: 20 },
    ],
    notToday: ['task-audit'],
};

function deployment(status: number, body: unknown) {
    const sent: ClientRequest[] = [];
    const transport: MailFathomTransport = (request) => {
        sent.push(request);

        return Promise.resolve({ status, headers: {}, body: JSON.stringify(body) });
    };

    return { sent, transport };
}

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

function drawOffer(transport: MailFathomTransport, events: readonly CalendarEvent[] = [], offered = true) {
    const planned = vi.fn();

    render(
        <LocalizationProvider>
            <LayoutOffer
                session={session}
                transport={transport}
                offered={offered}
                tasks={[room, reply, audit]}
                events={events}
                onPlan={planned}
            />
        </LocalizationProvider>,
    );

    return planned;
}

describe('LayoutOffer', () => {
    it('LayoutOffer writes plans only after "Plan these"', async () => {
        const { sent, transport } = deployment(200, arranged);
        const planned = drawOffer(transport);

        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        const accept = await screen.findByRole('button', { name: 'Plan these' });

        expect(screen.getByText('Here is a way to fit the rest of today')).toBeDefined();
        expect(screen.getByText('Book the meeting room')).toBeDefined();
        expect(planned).not.toHaveBeenCalled();
        expect(sent.map(({ method }) => method)).toStrictEqual(['POST']);

        fireEvent.click(accept);

        expect(planned).toHaveBeenCalledWith([
            [room, { startAt: '2026-09-24T10:00:00+00:00', minutes: 30 }],
            [reply, { startAt: '2026-09-24T16:00:00+00:00', minutes: 20 }],
        ]);
        expect(screen.queryByRole('button', { name: 'Plan these' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Lay out today' })).toBeDefined();
    });

    it('names where a placed task moved from and what did not fit today', async () => {
        const { transport } = deployment(200, arranged);

        drawOffer(transport);
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        expect(await screen.findByText(/^moved from 3:00 PM/u)).toBeDefined();
        expect(screen.getByText('One task did not fit today: Prepare the IT contract list for audit.')).toBeDefined();
    });

    it('puts the offer down on "Not now" without handing over any plan', async () => {
        const { transport } = deployment(200, arranged);
        const planned = drawOffer(transport);

        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Not now' }));

        expect(planned).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Lay out today' })).toBeDefined();
    });

    it('says nothing is left to lay out where the deployment arranged nothing', async () => {
        const { transport } = deployment(200, { arranged: false, placements: [], notToday: [] });

        drawOffer(transport);
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        expect(await screen.findByText('Nothing is left to lay out today.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Plan these' })).toBeNull();
    });

    // The list is read a page at a time, so a day laid out around tasks it has not read yet is not an empty day.
    it('says the tasks laid out are further down the list than it has read, rather than that nothing is left', async () => {
        const { transport } = deployment(200, {
            arranged: true,
            placements: [{ taskId: 'task-unread', startAt: '2026-09-24T10:00:00+00:00', minutes: 30 }],
            notToday: [],
        });

        drawOffer(transport);
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        expect(
            await screen.findByText(
                'The tasks this would plan are further down the list than it has read. Read more of the list, then lay out today again.',
            ),
        ).toBeDefined();
        expect(screen.queryByText('Nothing is left to lay out today.')).toBeNull();
    });

    it('leaves a task done since the offer arrived out of what "Plan these" plans', async () => {
        const { transport } = deployment(200, arranged);
        const planned = vi.fn();
        const drawn = (tasks: readonly PersonalTask[]) => (
            <LocalizationProvider>
                <LayoutOffer
                    session={session}
                    transport={transport}
                    offered
                    tasks={tasks}
                    events={[]}
                    onPlan={planned}
                />
            </LocalizationProvider>
        );
        const { rerender } = render(drawn([room, reply, audit]));

        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));
        await screen.findByRole('button', { name: 'Plan these' });

        rerender(drawn([{ ...room, completed: true }, reply, audit]));
        fireEvent.click(screen.getByRole('button', { name: 'Plan these' }));

        expect(planned).toHaveBeenCalledWith([[reply, { startAt: '2026-09-24T16:00:00+00:00', minutes: 20 }]]);
    });

    it('says laying out days is used up, and still offers to ask again', async () => {
        const { transport } = deployment(429, {});

        drawOffer(transport);
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        expect(await screen.findByText('Laying out days is used up for now.')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Lay out today' })).toBeDefined();
    });

    it('says it is laying out the day while the answer is on its way', async () => {
        const transport: MailFathomTransport = () => new Promise(() => undefined);

        drawOffer(transport);
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        await waitFor(() => {
            expect(screen.getByRole('status').textContent).toBe('Laying out the rest of the day…');
        });
    });

    // The control that asked is removed while the day is laid out, so nothing but a deliberate move keeps a keyboard or
    // a screen reader anywhere at all for the length of the wait.
    it('puts focus on what is being waited for once the control that asked is gone', async () => {
        const transport: MailFathomTransport = () => new Promise(() => undefined);

        drawOffer(transport);
        screen.getByRole('button', { name: 'Lay out today' }).focus();
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('status'));
        });
    });

    // The sentence waited on goes once the answer is drawn, so focus moves on to the offer rather than to the body.
    it('puts focus on the offer once the day is laid out', async () => {
        const { transport } = deployment(200, arranged);

        drawOffer(transport);
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByText('Here is a way to fit the rest of today'));
        });
    });

    // *Plan these* and *Not now* take themselves away with the offer, so focus lands on the control drawn in its place.
    it.each(['Plan these', 'Not now'])('puts focus on "Lay out today" once "%s" put the offer down', async (answer) => {
        const { transport } = deployment(200, arranged);

        drawOffer(transport);
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));
        fireEvent.click(await screen.findByRole('button', { name: answer }));

        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Lay out today' }));
        });
    });

    // A day that cannot be laid out draws the act again in place of the sentence waited on, and focus goes to it.
    it('puts focus on "Lay out today" once laying out days is used up', async () => {
        const { transport } = deployment(429, {});

        drawOffer(transport);
        fireEvent.click(screen.getByRole('button', { name: 'Lay out today' }));

        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Lay out today' }));
        });
    });

    // Opened from the calendar's *Plan my day*, the day is laid out once the deployment has said it arranges days, and
    // never before.
    it('lays the day out on opening only once the deployment has said it arranges days', async () => {
        const { sent, transport } = deployment(200, arranged);
        const drawn = (offered: boolean) => (
            <LocalizationProvider>
                <LayoutOffer
                    session={session}
                    transport={transport}
                    offered={offered}
                    startsArranging
                    tasks={[room, reply, audit]}
                    events={[]}
                    onPlan={vi.fn()}
                />
            </LocalizationProvider>
        );
        const { rerender } = render(drawn(false));

        expect(sent).toHaveLength(0);
        expect(screen.queryByRole('status')).toBeNull();

        rerender(drawn(true));

        expect(await screen.findByRole('button', { name: 'Plan these' })).toBeDefined();
        expect(sent.map(({ method }) => method)).toStrictEqual(['POST']);
    });

    it('offers nothing to lay out where the deployment arranges no day', () => {
        const { transport } = deployment(200, arranged);

        drawOffer(transport, [], false);

        expect(screen.queryByRole('button', { name: 'Lay out today' })).toBeNull();
    });
});
