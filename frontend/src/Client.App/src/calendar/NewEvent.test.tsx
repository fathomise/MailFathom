// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type {
    CalendarEventRecord,
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailFathomTransport,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { NewEvent } from './NewEvent';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// The fields resolve a day and a clock reading against the reader's own zone, so the zone is pinned: in UTC what is
// saved and what was typed are the same value and the assertion would prove nothing.
const zoneBefore = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'Europe/Warsaw';
});

afterEach(() => {
    process.env['TZ'] = zoneBefore;
});

// Stated as an instant with its offset rather than as local parts, because this one is read before the zone above is
// pinned: a module-level `new Date(2026, 8, 24, 10)` is ten o'clock wherever the runner happens to stand, which is
// noon in Warsaw on a machine set to UTC and would make what the deployment is told the reader's hour depend on it.
const standing = new Date('2026-09-24T10:00:00+02:00');

function deployment(answer: ClientResponse): {
    readonly transport: MailFathomTransport;
    readonly sent: () => readonly ClientRequest[];
} {
    const sent: ClientRequest[] = [];

    return {
        sent: () => sent,
        transport: (request) => {
            sent.push(request);

            return Promise.resolve(answer);
        },
    };
}

function drafted(body: unknown): ClientResponse {
    return { status: 200, headers: {}, body: JSON.stringify(body) };
}

// What opens the dialog in the test, named here rather than written into the markup, exactly as
// `people/ContactDialog.test.tsx` does it: the localization rule holds over every `.tsx` under the packages.
const asks = 'Ask';

function Asking({
    transport,
    readsDescriptions,
    onSave,
}: {
    readonly transport: MailFathomTransport;
    readonly readsDescriptions: boolean;
    readonly onSave: (record: CalendarEventRecord) => void;
}) {
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                {asks}
            </button>

            <NewEvent
                asked={asked}
                session={session}
                transport={transport}
                on={standing}
                readsDescriptions={readsDescriptions}
                onSave={onSave}
            />
        </>
    );
}

function drawDialog(
    transport: MailFathomTransport,
    { readsDescriptions = true, lostServer = false }: { readsDescriptions?: boolean; lostServer?: boolean } = {},
): { readonly onSave: ReturnType<typeof vi.fn> } {
    const onSave = vi.fn();

    render(
        <LocalizationProvider>
            <ConnectionStatusContext value={lostServer ? serverLost : serverAnswering}>
                <Asking transport={transport} readsDescriptions={readsDescriptions} onSave={onSave} />
            </ConnectionStatusContext>
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: asks }));

    return { onSave };
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

/** The description field, found by the part of its label that is words rather than the mark beside them. */
function describedIn(): HTMLElement {
    return screen.getByLabelText(/Describe the event/u);
}

describe('NewEvent', () => {
    it('opens both its days on the day the reader is standing on rather than on nothing', () => {
        drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        expect(screen.getByLabelText<HTMLInputElement>('Starts').value).toBe('2026-09-24');
        expect(screen.getByLabelText<HTMLInputElement>('Ends').value).toBe('2026-09-24');
    });

    it('saves nothing until a title and a beginning have been typed', () => {
        const { onSave } = drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(true);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Lunch with Anna' } });
        fireEvent.change(screen.getByLabelText('Start time'), { target: { value: '13:00' } });

        expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(false);

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onSave).toHaveBeenCalledWith({
            title: 'Lunch with Anna',
            start: new Date(2026, 8, 24, 13).toISOString(),
            end: null,
            isAllDay: false,
            reminders: [],
            notes: null,
            sourceMessage: null,
        });
    });

    // Saving writes the event, which has no route while the server is out of reach: it says so before it is pressed and
    // writes nothing, and what was typed stays in the dialog.
    it('refuses saving before it is pressed while the server is out of reach, says why, and keeps what was typed', () => {
        const { onSave } = drawDialog(deployment(drafted({ drafted: false, spent: false })).transport, {
            lostServer: true,
        });

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Lunch with Anna' } });
        fireEvent.change(screen.getByLabelText('Start time'), { target: { value: '13:00' } });

        const saving = screen.getByRole('button', { name: 'Save' });

        expect(saving.getAttribute('aria-disabled')).toBe('true');
        expect(screen.getByText('Save - no connection to the MailFathom server.')).toBeDefined();

        fireEvent.click(saving);

        expect(onSave).not.toHaveBeenCalled();
        expect(screen.getByLabelText<HTMLInputElement>('Title').value).toBe('Lunch with Anna');
    });

    it('writes down what the event is about beside its title', () => {
        const { onSave } = drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Lunch with Anna' } });
        fireEvent.change(screen.getByLabelText('Description'), { target: { value: '  Bring the signed copy.  ' } });
        fireEvent.change(screen.getByLabelText('Start time'), { target: { value: '13:00' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ notes: 'Bring the signed copy.' }));
    });

    it('puts the clock away for a day and writes the day"s own midnight down', () => {
        const { onSave } = drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Site visit' } });
        fireEvent.click(screen.getByLabelText('All day'));

        // A day carries no clock reading, so the two fields that would ask for one are gone rather than ignored.
        expect(screen.queryByLabelText('Start time')).toBeNull();
        expect(screen.queryByLabelText('End time')).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onSave).toHaveBeenCalledWith({
            title: 'Site visit',
            start: new Date(2026, 8, 24).toISOString(),
            end: null,
            isAllDay: true,
            reminders: [],
            notes: null,
            sourceMessage: null,
        });
    });

    it('spans several days, ending where the last of them closes', () => {
        const { onSave } = drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Conference' } });
        fireEvent.click(screen.getByLabelText('All day'));
        fireEvent.change(screen.getByLabelText('Ends'), { target: { value: '2026-09-26' } });

        expect(screen.getByText('3 days')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onSave).toHaveBeenCalledWith(
            expect.objectContaining({
                start: new Date(2026, 8, 24).toISOString(),
                end: new Date(2026, 8, 27).toISOString(),
                isAllDay: true,
            }),
        );
    });

    it('says an end before the beginning and saves nothing while it stands', () => {
        drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Lunch with Anna' } });
        fireEvent.change(screen.getByLabelText('Start time'), { target: { value: '13:00' } });
        fireEvent.change(screen.getByLabelText('End time'), { target: { value: '12:00' } });

        expect(screen.getByRole('alert').textContent).toBe('The event ends before it starts.');
        expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(true);
    });

    it('asks for a time to end at where the event ends on a later day', () => {
        drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Offsite' } });
        fireEvent.change(screen.getByLabelText('Start time'), { target: { value: '09:00' } });
        fireEvent.change(screen.getByLabelText('Ends'), { target: { value: '2026-09-26' } });

        expect(screen.getByRole('alert').textContent).toBe('An event ending on a later day needs a time to end at.');
        expect(screen.getByRole('button', { name: 'Save' }).hasAttribute('disabled')).toBe(true);
    });

    it('sets reminders before the event is written down', () => {
        const { onSave } = drawDialog(deployment(drafted({ drafted: false, spent: false })).transport);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Lunch with Anna' } });
        fireEvent.change(screen.getByLabelText('Start time'), { target: { value: '13:00' } });
        fireEvent.click(screen.getByRole('button', { name: /Reminders/u }));
        fireEvent.click(screen.getByRole('button', { name: '15 min before' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save' }));

        expect(onSave).toHaveBeenCalledWith(expect.objectContaining({ reminders: [15] }));
    });

    it('puts what the deployment read into the fields and saves nothing on its own', async () => {
        const { transport, sent } = deployment(
            drafted({
                drafted: true,
                spent: false,
                title: 'Lunch with Anna',
                start: '2026-09-24T13:00:00+02:00',
                end: '2026-09-24T14:00:00+02:00',
            }),
        );
        const { onSave } = drawDialog(transport);

        fireEvent.change(describedIn(), {
            target: { value: 'lunch with Anna on Thursday at one' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Generate' }));

        expect(await screen.findByText('Filled in from your description. Check it before saving.')).toBeDefined();
        expect(screen.getByLabelText<HTMLInputElement>('Title').value).toBe('Lunch with Anna');
        expect(screen.getByLabelText<HTMLInputElement>('Start time').value).toBe('13:00');
        expect(screen.getByLabelText<HTMLInputElement>('End time').value).toBe('14:00');

        // Read rather than written: nothing has been saved, and the one request that went out is the reading.
        expect(onSave).not.toHaveBeenCalled();
        expect(sent()).toHaveLength(1);
        expect(sent()[0]?.path).toContain('/calendar/drafts');
    });

    it('sends the sentence and nothing else, which is the whole of what the route takes', async () => {
        const { transport, sent } = deployment(drafted({ drafted: false, spent: false }));
        drawDialog(transport);

        fireEvent.change(describedIn(), {
            target: { value: 'tomorrow at nine' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Generate' }));

        await waitFor(() => {
            expect(sent()).toHaveLength(1);
        });

        // The route refuses a member it does not name, so a second one here is every reading answered with a refusal.
        expect(JSON.parse(sent()[0]?.body ?? '{}')).toStrictEqual({ description: 'tomorrow at nine' });
    });

    it('reads the description on Enter rather than saving the fields typed beside it', async () => {
        const { transport, sent } = deployment(drafted({ drafted: false, spent: false }));
        const { onSave } = drawDialog(transport);

        fireEvent.change(screen.getByLabelText('Title'), { target: { value: 'Lunch with Anna' } });
        fireEvent.change(screen.getByLabelText('Start time'), { target: { value: '13:00' } });
        fireEvent.change(describedIn(), { target: { value: 'lunch on Thursday' } });
        fireEvent.keyDown(describedIn(), { key: 'Enter' });

        await waitFor(() => {
            expect(sent()).toHaveLength(1);
        });

        // What the field promises is that the sentence is read, so the key that ends a line in it reads rather than
        // writing the event down from the two fields that happen to be filled.
        expect(sent()[0]?.path).toContain('/calendar/drafts');
        expect(onSave).not.toHaveBeenCalled();
    });

    it('says a sentence it could read nothing out of rather than filling the fields with a guess', async () => {
        const { transport } = deployment(drafted({ drafted: false, spent: false }));
        drawDialog(transport);

        fireEvent.change(describedIn(), {
            target: { value: 'something' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Generate' }));

        expect(
            await screen.findByText('No event could be read out of that. Try naming a day and a time.'),
        ).toBeDefined();
        expect(screen.getByLabelText<HTMLInputElement>('Title').value).toBe('');
    });

    it('says a spent allowance as itself, the form beside it still being usable', async () => {
        const { transport } = deployment({ status: 429, headers: {}, body: '' });
        drawDialog(transport);

        fireEvent.change(describedIn(), {
            target: { value: 'lunch on Thursday' },
        });
        fireEvent.click(screen.getByRole('button', { name: 'Generate' }));

        expect(
            await screen.findByText('This server has used up what it may spend on reading descriptions for now.'),
        ).toBeDefined();
        expect(screen.getByLabelText('Title')).toBeDefined();
    });
});
