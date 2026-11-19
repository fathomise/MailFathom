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

// The deployment behind the dialog: a revise answers with the task it was handed, or with the status a case names.
function deployment(status = 200): { readonly transport: MailFathomTransport; readonly sent: ClientRequest[] } {
    const sent: ClientRequest[] = [];

    return {
        sent,
        transport: (request) => {
            sent.push(request);

            const answer: ClientResponse =
                status === 200
                    ? { status, headers: {}, body: JSON.stringify(held) }
                    : { status, headers: {}, body: '' };

            return Promise.resolve(answer);
        },
    };
}

function Opened({
    transport,
    onWritten,
}: {
    readonly transport: MailFathomTransport;
    readonly onWritten: (said: string) => void;
}) {
    const asked = useRef<HTMLDialogElement>(null);

    useEffect(() => {
        asked.current?.showModal();
    }, []);

    return (
        <TaskDialog
            session={session}
            transport={transport}
            task={held}
            asked={asked}
            events={[]}
            onWritten={onWritten}
            onToggleCompleted={vi.fn()}
            onAskErasure={vi.fn()}
            onReminders={vi.fn()}
            onOpenAnchor={undefined}
            onLeft={vi.fn()}
        />
    );
}

function drawDialog(status = 200) {
    const { transport, sent } = deployment(status);
    const written = vi.fn();

    render(
        <LocalizationProvider>
            <Opened transport={transport} onWritten={written} />
        </LocalizationProvider>,
    );

    return { sent, written };
}

function revised(sent: readonly ClientRequest[]): unknown {
    const [put] = sent.filter(({ method }) => method === 'PUT');

    return put === undefined ? undefined : JSON.parse(put.body ?? 'null');
}

describe('TaskDialog', () => {
    it('saving a new title revises the task with the whole record', async () => {
        const { sent, written } = drawDialog();

        fireEvent.change(screen.getByLabelText('What is it?'), { target: { value: '  Send the signed addendum  ' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.revised');
        });

        expect(sent.map(({ method, path }) => `${method} ${path}`)).toStrictEqual([
            'PUT https://mail.example.invalid/api/client/tasks/task-a',
        ]);
        expect(revised(sent)).toStrictEqual({
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
        });
    });

    it('removing the anchor sends anchor null', async () => {
        const { sent, written } = drawDialog();

        fireEvent.click(screen.getByRole('button', { name: 'Remove the link to Contract addendum - signatures' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalled();
        });

        expect(revised(sent)).toMatchObject({ anchor: null, counterpart: held.counterpart });
    });

    it('changes which way the task runs and the day it is followed up on', async () => {
        const { sent, written } = drawDialog();

        fireEvent.click(screen.getByRole('radio', { name: 'I am waiting for this' }));
        fireEvent.click(screen.getByRole('radio', { name: 'No day' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalled();
        });

        // A task with no day announces nothing, because a lead is measured back from the day.
        expect(revised(sent)).toMatchObject({
            direction: 'Awaited',
            dueOn: null,
            reminders: [],
            dueDayOffsetMinutes: null,
        });
    });

    it('says in the dialog that a reply already answered the task when its direction cannot change', async () => {
        const { written } = drawDialog(409);

        fireEvent.click(screen.getByRole('radio', { name: 'I am waiting for this' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(
            await screen.findByText('A reply already answered this task, so which way it runs cannot change.'),
        ).toBeDefined();
        expect(written).not.toHaveBeenCalled();
    });

    it('lets go of a task the deployment no longer holds when nothing about what it belongs to changed', async () => {
        const { written } = drawDialog(404);

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        await waitFor(() => {
            expect(written).toHaveBeenCalledWith('tasks.gone');
        });
    });

    it('refuses to save a title nobody typed', () => {
        const { sent } = drawDialog();

        fireEvent.change(screen.getByLabelText('What is it?'), { target: { value: '   ' } });

        expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Save' }).disabled).toBe(true);
        expect(sent).toStrictEqual([]);
    });

    it('draws where the task is planned and what it belongs to', () => {
        drawDialog();

        expect(screen.getByRole('dialog', { name: 'Task' })).toBeDefined();
        expect(screen.getByText('Contract addendum - signatures')).toBeDefined();
        expect(screen.getByText('Anna Kowalska')).toBeDefined();
        expect(screen.getByText('Planned')).toBeDefined();
    });
});
