// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import {
    dueDayOffsetMinutes,
    type ClientRequest,
    type ClientResponse,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTask,
} from '@mailfathom/client-backend';
import { calendarDayIn, dayFrom } from '../localization/instants';
import { LocalizationProvider } from '../localization/Localization';
import { TaskDialog } from './TaskDialog';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const held: PersonalTask = {
    id: 'task-a',
    title: 'Send the counter-proposal',
    dueOn: '2026-09-24',
    reminders: [60],
    remindsAt: ['2026-09-24T07:00:00+00:00'],
    origin: 'Asserted',
    completed: false,
    sourceMessageId: 'message-a',
    direction: 'Owed',
    estimatedMinutes: 45,
    plan: { startAt: '2026-09-24T09:00:00+00:00', minutes: 45 },
    anchor: { kind: 'Thread', id: '0190f3c2-8a7b-7cde-9f01-23456789abcd', label: 'Contract addendum - signatures' },
    counterpart: { address: 'anna@contoso.example', displayName: 'Anna Kowalska' },
    answer: null,
};

const answered: PersonalTask = {
    ...held,
    id: 'wait-a',
    title: 'Signed addendum from Anna',
    direction: 'Awaited',
    completed: true,
    plan: null,
    answer: { messageId: 'reply-a', arrivedAt: '2026-09-24T07:12:00+00:00' },
};

// The deployment behind the dialog: each revise answers with the task, or with the status a case names for it in turn —
// at once, or only when the case lets it go.
function deployment(statuses: readonly number[], waits: boolean) {
    const sent: ClientRequest[] = [];
    let release: () => void = () => undefined;
    const transport: MailFathomTransport = (request) => {
        sent.push(request);

        const status = statuses[sent.length - 1] ?? statuses.at(-1) ?? 200;
        const answer: ClientResponse =
            status === 200 ? { status, headers: {}, body: JSON.stringify(held) } : { status, headers: {}, body: '' };

        return waits
            ? new Promise((resolve) => {
                  release = () => {
                      resolve(answer);
                  };
              })
            : Promise.resolve(answer);
    };

    return {
        sent,
        transport,
        release: () => {
            release();
        },
    };
}

function Opened({
    task,
    transport,
    onWritten,
    onToggleCompleted,
    onReminders,
}: {
    readonly task: PersonalTask;
    readonly transport: MailFathomTransport;
    readonly onWritten: (said: string) => void;
    readonly onToggleCompleted: (task: PersonalTask) => void;
    readonly onReminders: (task: PersonalTask) => void;
}) {
    const asked = useRef<HTMLDialogElement>(null);

    useEffect(() => {
        asked.current?.showModal();
    }, []);

    return (
        <TaskDialog
            session={session}
            transport={transport}
            task={task}
            asked={asked}
            events={[]}
            onWritten={onWritten}
            onToggleCompleted={onToggleCompleted}
            onAskErasure={vi.fn()}
            onReminders={onReminders}
            onOpenAnchor={undefined}
            onLeft={vi.fn()}
        />
    );
}

function drawDialog({
    status = 200,
    statuses = [status],
    task = held,
    waits = false,
}: {
    status?: number;
    statuses?: readonly number[];
    task?: PersonalTask;
    waits?: boolean;
} = {}) {
    const { transport, sent, release } = deployment(statuses, waits);
    const written = vi.fn();
    const toggled = vi.fn();
    const announcing = vi.fn();
    const drawn = (shown: PersonalTask) => (
        <LocalizationProvider>
            <Opened
                task={shown}
                transport={transport}
                onWritten={written}
                onToggleCompleted={toggled}
                onReminders={announcing}
            />
        </LocalizationProvider>
    );
    const { rerender } = render(drawn(task));

    return {
        sent,
        written,
        toggled,
        announcing,
        release,
        // What the screen hands the dialog once its list has read the task again.
        readAgain: (shown: PersonalTask) => {
            rerender(drawn(shown));
        },
    };
}

function revisions(sent: readonly ClientRequest[]): unknown[] {
    return sent.filter(({ method }) => method === 'PUT').map(({ body }) => JSON.parse(body ?? 'null') as unknown);
}

function dialog(): HTMLDialogElement {
    return screen.getByRole<HTMLDialogElement>('dialog', { hidden: true });
}

describe('TaskDialog', () => {
    it('saving a new title revises the task with the whole record and closes', async () => {
        const { sent, written } = drawDialog();

        fireEvent.change(screen.getByLabelText('What is it?'), { target: { value: '  Send the signed addendum  ' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.revised');
        });

        expect(sent.map(({ method, path }) => `${method} ${path}`)).toStrictEqual([
            'PUT https://mail.example.invalid/api/client/tasks/task-a',
        ]);
        expect(revisions(sent)).toStrictEqual([
            {
                title: 'Send the signed addendum',
                dueOn: '2026-09-24',
                reminders: [60],
                dueDayOffsetMinutes: dueDayOffsetMinutes('2026-09-24', null),
                sourceMessageId: 'message-a',
                direction: 'Owed',
                estimatedMinutes: 45,
                plan: { startAt: '2026-09-24T09:00:00+00:00', minutes: 45 },
                anchor: {
                    kind: 'Thread',
                    id: '0190f3c2-8a7b-7cde-9f01-23456789abcd',
                    label: 'Contract addendum - signatures',
                },
                counterpart: { address: 'anna@contoso.example', displayName: 'Anna Kowalska' },
            },
        ]);
        expect(dialog().open).toBe(false);
    });

    it('removing the anchor writes anchor null at once and keeps the dialog open on the person', async () => {
        const { sent, written } = drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'Remove the link to Contract addendum - signatures' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.revised');
        });

        expect(revisions(sent)).toMatchObject([{ anchor: null, counterpart: held.counterpart, title: held.title }]);
        expect(dialog().open).toBe(true);
        expect(screen.getByText('Anna Kowalska')).toBeDefined();
        expect(screen.getByText('Person')).toBeDefined();
    });

    it('writes each chip as it is pressed, the typed line waiting for Save', async () => {
        const { sent, written } = drawDialog();

        fireEvent.change(screen.getByLabelText('What is it?'), { target: { value: 'Not yet saved' } });
        fireEvent.click(screen.getByRole('radio', { name: 'I am waiting for this' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledTimes(1);
        });

        fireEvent.click(screen.getByRole('radio', { name: 'No day' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledTimes(2);
        });

        // A task with no day announces nothing, because a lead is measured back from the day.
        expect(revisions(sent)).toMatchObject([
            { title: held.title, direction: 'Awaited', dueOn: '2026-09-24', reminders: [60] },
            { title: held.title, direction: 'Awaited', dueOn: null, reminders: [], dueDayOffsetMinutes: null },
        ]);
        expect(screen.getByRole('heading', { name: 'Waiting for' })).toBeDefined();
        expect(dialog().open).toBe(true);
    });

    it('keeps a chip pressed while a write is out and writes it once that write answers', async () => {
        const { sent, written, release } = drawDialog({ waits: true });

        fireEvent.click(screen.getByRole('radio', { name: 'Tomorrow' }));
        fireEvent.click(screen.getByRole('radio', { name: 'I am waiting for this' }));

        // Both stand at once, and only one write is out.
        expect(screen.getByRole<HTMLInputElement>('radio', { name: 'Tomorrow' }).checked).toBe(true);
        expect(screen.getByRole('heading', { name: 'Waiting for' })).toBeDefined();
        expect(revisions(sent)).toHaveLength(1);

        release();

        await waitFor(() => {
            expect(revisions(sent)).toHaveLength(2);
        });

        release();

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.revised');
        });

        const tomorrow = dayFrom(calendarDayIn(Date.now(), null), 1);

        expect(revisions(sent)).toMatchObject([
            { direction: 'Owed', dueOn: tomorrow },
            { direction: 'Awaited', dueOn: tomorrow },
        ]);
        expect(screen.getByRole<HTMLInputElement>('radio', { name: 'Tomorrow' }).checked).toBe(true);
        expect(screen.getByRole('heading', { name: 'Waiting for' })).toBeDefined();
    });

    it('keeps the reminders it cleared when a day is chosen again before the list reads back', async () => {
        const { sent, written } = drawDialog();

        fireEvent.click(screen.getByRole('radio', { name: 'No day' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledTimes(1);
        });

        fireEvent.click(screen.getByRole('radio', { name: 'Tomorrow' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledTimes(2);
        });

        expect(revisions(sent)).toMatchObject([{ dueOn: null, reminders: [] }, { reminders: [] }]);
        expect(screen.getByText('No reminder')).toBeDefined();
    });

    it('tells the list what a first write took when the press queued behind it is refused', async () => {
        const { sent, written, release } = drawDialog({ statuses: [200, 409], waits: true });

        fireEvent.click(screen.getByRole('radio', { name: 'Tomorrow' }));
        fireEvent.click(screen.getByRole('radio', { name: 'I am waiting for this' }));
        release();

        await waitFor(() => {
            expect(revisions(sent)).toHaveLength(2);
        });

        release();

        expect(
            await screen.findByText('A reply already answered this task, so which way it runs cannot change.'),
        ).toBeDefined();
        expect(written).toHaveBeenCalledWith('tasks.revised');
        expect(screen.getByRole<HTMLInputElement>('radio', { name: 'Tomorrow' }).checked).toBe(true);
        expect(screen.getByRole<HTMLInputElement>('radio', { name: 'I owe this' }).checked).toBe(true);
    });

    it('sends nothing more and lets go when the task went while a press was queued', async () => {
        const { sent, written, release } = drawDialog({ statuses: [404], waits: true });

        fireEvent.click(screen.getByRole('radio', { name: 'Tomorrow' }));
        fireEvent.click(screen.getByRole('radio', { name: 'I am waiting for this' }));
        release();

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.gone');
        });

        expect(revisions(sent)).toHaveLength(1);
        expect(dialog().open).toBe(false);
    });

    it('lets reminders set elsewhere stand over the ones a chip cleared', async () => {
        const { sent, written, announcing, readAgain } = drawDialog();
        const tomorrow = dayFrom(calendarDayIn(Date.now(), null), 1);

        fireEvent.click(screen.getByRole('radio', { name: 'No day' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledTimes(1);
        });

        fireEvent.click(screen.getByRole('radio', { name: 'Tomorrow' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledTimes(2);
        });

        fireEvent.click(screen.getByRole('button', { name: 'Change the reminders' }));

        // The panel is handed what the dialog shows, not what the list last read.
        expect(announcing).toHaveBeenCalledWith(expect.objectContaining({ dueOn: tomorrow, reminders: [] }));

        // The panel wrote a reminder through the screen, whose list then read the task back.
        readAgain({ ...held, dueOn: tomorrow, reminders: [1440] });

        expect(screen.getByText('1 reminder')).toBeDefined();

        fireEvent.click(screen.getByRole('radio', { name: 'I am waiting for this' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledTimes(3);
        });

        expect(revisions(sent).at(-1)).toMatchObject({ direction: 'Awaited', dueOn: tomorrow, reminders: [1440] });
    });

    it('says a reply already answered a wait turned round, and puts the chip back', async () => {
        const { written } = drawDialog({ status: 409, task: answered });

        fireEvent.click(screen.getByRole('radio', { name: 'I owe this' }));

        expect(
            await screen.findByText('A reply already answered this task, so which way it runs cannot change.'),
        ).toBeDefined();
        expect(screen.getByRole<HTMLInputElement>('radio', { name: 'I am waiting for this' }).checked).toBe(true);
        expect(written).not.toHaveBeenCalled();
    });

    it('keeps waiting on a wait a reply closed without leaving the dialog', () => {
        const { toggled } = drawDialog({ task: answered });

        fireEvent.click(screen.getByRole('button', { name: 'Keep waiting' }));

        expect(toggled).toHaveBeenCalledWith(answered);
        expect(dialog().open).toBe(true);
    });

    it('lets go of a task the deployment no longer holds when nothing about what it belongs to changed', async () => {
        const { written } = drawDialog({ status: 404 });

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.gone');
        });
    });

    it('closes nothing it was not asked from when a write answers after the dialog was left', async () => {
        const { release, written } = drawDialog({ waits: true });

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));
        dialog().showModal();
        release();

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.revised');
        });

        expect(dialog().open).toBe(true);
    });

    it('refuses to save a title nobody typed', () => {
        const { sent } = drawDialog();

        fireEvent.change(screen.getByLabelText('What is it?'), { target: { value: '   ' } });

        expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Save' }).disabled).toBe(true);
        expect(sent).toStrictEqual([]);
    });

    it('draws where the task is planned and the one thing it belongs to', () => {
        drawDialog();

        expect(screen.getByRole('dialog', { name: 'Task' })).toBeDefined();
        expect(screen.getByText('Contract addendum - signatures')).toBeDefined();
        expect(screen.queryByText('Anna Kowalska')).toBeNull();
        expect(screen.getByText('Planned')).toBeDefined();
    });
});
