// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, renderHook, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { ScreenLayersContext, useScreenLayerStack, type ScreenLayers } from '../shell/screenLayers';
import { PeopleSpace } from './PeopleSpace';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// jsdom evaluates no media query, so the composition a test is about is stated as a width and read back out of the
// query itself rather than from a table this file would have to keep in step with the stylesheet. It is the double
// `mailSpace/MailSpace.test.tsx` uses, for the same reason: the space asks whether there is room for two panes and
// whether the toolbar is drawn, and each is its own width.
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
    atWidth(desktop);
});

afterEach(() => {
    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

function personCalled(id: string, displayName: string, origin = 'Asserted'): unknown {
    return {
        id,
        displayName,
        addresses: [`${id}@contoso.example`],
        preferredAddress: `${id}@contoso.example`,
        note: null,
        origin,
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
    };
}

const answered = { status: 200, headers: {} } as const;

// One deployment behind the screen: the two books, one person's correlation, and the three writes. A test states what
// the write answers and reads back what the screen did about it.
function deployment({
    own = [personCalled('anna', 'Anna Kowalska')],
    collected = [personCalled('celina', 'Celina Wrona', 'Collected')],
    write = { outcome: 'Written', contact: personCalled('anna', 'Anna Kowalska'), addressHolder: null },
    refuses = [],
    holdsWrites = false,
}: {
    own?: readonly unknown[];
    collected?: readonly unknown[];
    write?: unknown;

    /** Whose erasure this deployment will not perform, which is how a batch is made to half succeed. */
    refuses?: readonly string[];

    /** Whether a write waits to be released, which is what gives a test the time inside one to navigate elsewhere. */
    holdsWrites?: boolean;
} = {}): {
    readonly transport: MailFathomTransport;
    readonly sent: () => readonly ClientRequest[];
    readonly release: () => void;
} {
    const sent: ClientRequest[] = [];
    const held: (() => void)[] = [];

    return {
        sent: () => sent,
        release: () => {
            const answering = held.splice(0, held.length);

            for (const answer of answering) {
                answer();
            }
        },
        transport: (request) => {
            sent.push(request);

            const { method, path } = request;
            let answer: ClientResponse;

            if (method === 'DELETE') {
                answer = refuses.some((contact) => path.endsWith(`/contacts/${contact}`))
                    ? { status: 403, headers: {}, body: '' }
                    : {
                          ...answered,
                          body: JSON.stringify({ contact: 'anna', wasHeld: true, addressesErased: 1 }),
                      };
            } else if (method === 'POST' || method === 'PUT') {
                answer = { ...answered, body: JSON.stringify(write) };

                if (holdsWrites) {
                    const waiting = answer;

                    return new Promise<ClientResponse>((resolve) => {
                        held.push(() => {
                            resolve(waiting);
                        });
                    });
                }
            } else if (path.includes('/correspondence')) {
                answer = { ...answered, body: JSON.stringify({ contactId: 'anna', threads: [], documents: [] }) };
            } else if (path.includes('/contacts/collected')) {
                answer = { ...answered, body: JSON.stringify({ contacts: collected, nextCursor: null }) };
            } else {
                answer = { ...answered, body: JSON.stringify({ contacts: own, nextCursor: null }) };
            }

            return Promise.resolve(answer);
        },
    };
}

const notWriting: Composing = {
    offered: false,
    drafts: false,
    opening: null,
    compose: () => undefined,
    close: () => undefined,
};

function drawSpace(
    transport: MailFathomTransport,
    {
        writable = true,
        composing = notWriting,
        layers = null,
        lostServer = false,
    }: { writable?: boolean; composing?: Composing; layers?: ScreenLayers | null; lostServer?: boolean } = {},
): void {
    render(
        <LocalizationProvider>
            <ConnectionStatusContext value={lostServer ? serverLost : serverAnswering}>
                <ScreenLayersContext value={layers}>
                    <ComposingContext value={composing}>
                        <PeopleSpace
                            session={session}
                            transport={transport}
                            writable={writable}
                            onOpenThread={vi.fn()}
                            onOpenDocument={vi.fn()}
                        />
                    </ComposingContext>
                </ScreenLayersContext>
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

/** The shell's stack, which a test hands the space so it can press the back gesture the way the shell does. */
function shell(): { readonly current: ScreenLayers } {
    return renderHook(() => useScreenLayerStack()).result;
}

describe('PeopleSpace', () => {
    it('draws the book beside an empty pane until somebody is opened', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Anna Kowalska/u })).toBeDefined();
        expect(screen.getByText('Open somebody to read their page.')).toBeDefined();
    });

    it('opens the person a press lands on, on their own page beside the book', async () => {
        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }));

        expect(await screen.findByRole('heading', { name: 'Anna Kowalska' })).toBeDefined();
        expect(screen.queryByText('Open somebody to read their page.')).toBeNull();
    });

    it('writes somebody down as a person this user asserted, with the one address they were given', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Anna Kowalska/u })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'New contact' }));
        fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Bartek Nowak' } });
        fireEvent.change(screen.getByLabelText('Email address'), { target: { value: 'bartek@contoso.example' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        await waitFor(() => {
            expect(sent().some((request) => request.method === 'POST')).toBe(true);
        });

        const written = sent().find((request) => request.method === 'POST');

        expect(JSON.parse(written?.body ?? 'null')).toStrictEqual({
            displayName: 'Bartek Nowak',
            addresses: ['bartek@contoso.example'],
            preferredAddress: 'bartek@contoso.example',
            note: null,
        });
    });

    it('says what the book did about a write rather than leaving the screen unchanged', async () => {
        const { transport } = deployment({
            write: { outcome: 'AddressHeldByAnotherContact', contact: null, addressHolder: 'anna' },
        });
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Anna Kowalska/u })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'New contact' }));
        fireEvent.change(screen.getByLabelText('Name'), { target: { value: 'Bartek Nowak' } });
        fireEvent.change(screen.getByLabelText('Email address'), { target: { value: 'anna@contoso.example' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        expect(await screen.findByText('Another contact already holds that address.')).toBeDefined();
    });

    // A correction asked for again opens on what it wrote, so saving the form untouched cannot put the old name back.
    it('opens a correction asked for again on the record as the last one left it', async () => {
        const { transport } = deployment({
            write: { outcome: 'Written', contact: personCalled('anna', 'Anna Marlow'), addressHolder: null },
        });
        drawSpace(transport);
        const correction = () => screen.getByRole('dialog', { name: 'Edit contact' });

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }));
        fireEvent.click(await screen.findByRole('button', { name: 'Edit contact' }));
        fireEvent.change(within(correction()).getByLabelText('Name'), { target: { value: 'Anna Marlow' } });
        fireEvent.click(within(correction()).getByRole('button', { name: 'Save changes' }));

        expect(await screen.findByText('Contact saved.')).toBeDefined();

        fireEvent.click(await screen.findByRole('button', { name: 'Edit contact' }));

        expect(within(correction()).getByLabelText<HTMLInputElement>('Name').value).toBe('Anna Marlow');
        expect(within(correction()).getByRole('button', { name: 'Save changes' }).hasAttribute('disabled')).toBe(true);
    });

    // A person taken on moves between the two books entirely, so the screen follows them to the one that now holds
    // them rather than leaving the reader on a tab their record has left.
    it('takes a collected record on and shows the book that now holds them', async () => {
        const { transport, sent } = deployment({
            write: {
                outcome: 'Written',
                contact: personCalled('celina', 'Celina Wrona'),
                addressHolder: null,
            },
        });
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Anna Kowalska/u })).toBeDefined();

        fireEvent.click(screen.getByRole('radio', { name: 'Collected' }));
        fireEvent.click(await screen.findByRole('option', { name: /Celina Wrona/u }));
        fireEvent.click(await screen.findByRole('button', { name: 'Add to my contacts' }));

        await waitFor(() => {
            expect(sent().some((request) => request.path.includes('/promotion'))).toBe(true);
        });
        expect(await screen.findByRole('radio', { name: 'Own', checked: true })).toBeDefined();
    });

    // The other half of that: the answer names the person it was asked about, and a reader is free to open somebody
    // else while it is in flight. Applying it regardless would take the screen back off whoever they moved to — so
    // the row and the tab move only where the person taken on is still the person being read.
    it('leaves whoever was opened during a promotion where they are when the answer lands', async () => {
        const { transport, release } = deployment({
            collected: [
                personCalled('celina', 'Celina Wrona', 'Collected'),
                personCalled('dorota', 'Dorota Zaremba', 'Collected'),
            ],
            write: { outcome: 'Written', contact: personCalled('celina', 'Celina Wrona'), addressHolder: null },
            holdsWrites: true,
        });
        drawSpace(transport);

        expect(await screen.findByRole('option', { name: /Anna Kowalska/u })).toBeDefined();

        fireEvent.click(screen.getByRole('radio', { name: 'Collected' }));
        fireEvent.click(await screen.findByRole('option', { name: /Celina Wrona/u }));
        fireEvent.click(await screen.findByRole('button', { name: 'Add to my contacts' }));

        fireEvent.click(screen.getByRole('option', { name: /Dorota Zaremba/u }));
        release();

        expect(await screen.findByText('Contact added.')).toBeDefined();
        expect(screen.getByRole('heading', { name: 'Dorota Zaremba' })).toBeDefined();
        expect(screen.getByRole('radio', { name: 'Collected', checked: true })).toBeDefined();
    });

    // Deleting mail's counterpart here: the question names who it is about before anything is removed.
    it('asks before taking somebody out of the book, naming who it is about', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }));
        fireEvent.click(await screen.findByRole('button', { name: 'Delete contact' }));

        expect(await screen.findByText('Delete this contact?')).toBeDefined();
        expect(sent().every((request) => request.method !== 'DELETE')).toBe(true);

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        await waitFor(() => {
            expect(sent().some((request) => request.method === 'DELETE')).toBe(true);
        });
    });

    it('offers none of the acts that change the book where the credential may not', async () => {
        const { transport } = deployment();
        drawSpace(transport, { writable: false });

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }));

        expect(screen.queryByRole('button', { name: 'New contact' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Delete contact' })).toBeNull();
    });

    // The two books are two lists rather than one list read twice, and what says so is where a keyboard stands after
    // the move: a row carried over from the longer book points past the end of the shorter one, which leaves a roving
    // tabindex with no option to give focus to at all.
    it('draws the book arrived at as a list of its own rather than continuing the one left', async () => {
        const { transport } = deployment({
            own: [
                personCalled('anna', 'Anna Kowalska'),
                personCalled('bartek', 'Bartek Nowak'),
                personCalled('celina', 'Celina Zaremba'),
            ],
            collected: [personCalled('dorota', 'Dorota Wrona', 'Collected')],
        });
        drawSpace(transport);

        const book = await screen.findByRole('listbox', { name: 'Contacts' });

        fireEvent.keyDown(book, { key: 'ArrowDown' });
        fireEvent.keyDown(book, { key: 'ArrowDown' });

        expect(screen.getByRole('option', { name: /Celina Zaremba/u }).tabIndex).toBe(0);

        fireEvent.click(screen.getByRole('radio', { name: 'Collected' }));

        expect((await screen.findByRole('option', { name: /Dorota Wrona/u })).tabIndex).toBe(0);
    });

    // Erasing several is several writes, so one refusal among them is reported as itself: the sentence names who is
    // still in the book, and they are still picked out so that asking again is one press rather than a search.
    it('names whoever a batch erasure did not remove, and leaves them picked out', async () => {
        const { transport } = deployment({
            own: [personCalled('anna', 'Anna Kowalska'), personCalled('bartek', 'Bartek Nowak')],
            refuses: ['bartek'],
        });
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }), { ctrlKey: true });
        fireEvent.click(screen.getByRole('option', { name: /Bartek Nowak/u }), { ctrlKey: true });

        fireEvent.click(screen.getByRole('button', { name: 'Delete contacts' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));

        expect(await screen.findByText('Could not delete Bartek Nowak. Everybody else was deleted.')).toBeDefined();
        expect(screen.getByRole('toolbar', { name: 'Selected contacts' })).toBeDefined();
    });

    // An erasure nobody answered may have been made, so it is never named among those the server would not delete.
    it('says it is not known whether an erasure nobody answered was made', async () => {
        const { transport } = deployment({ refuses: [] });
        const unanswering: MailFathomTransport = (request) =>
            request.method === 'DELETE' ? Promise.resolve({ status: 504, headers: {}, body: '' }) : transport(request);

        drawSpace(unanswering);

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }), { ctrlKey: true });
        fireEvent.click(screen.getByRole('button', { name: 'Delete contacts' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Delete' }));

        expect(
            await screen.findByText('The server did not answer, so it is not known whether that was changed.'),
        ).toBeDefined();
    });

    it('refuses deleting somebody before it is pressed while the server is out of reach, and says why', async () => {
        const { transport, sent } = deployment();
        drawSpace(transport, { lostServer: true });

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }), { ctrlKey: true });

        const deleting = screen.getByRole('button', { name: 'Delete contacts' });

        expect(deleting.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(deleting);

        expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();
        expect(sent().some((request) => request.method === 'DELETE')).toBe(false);
    });

    // Below the width the mail screens collapse at, the book and the person are one pane the reader moves between, so
    // the person comes in front of the list and carries the control that returns to it.
    it('brings the person in front of the book where the two cannot stand together', async () => {
        atWidth(phone);

        const { transport } = deployment();
        drawSpace(transport);

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }));

        expect(await screen.findByRole('heading', { name: 'Anna Kowalska' })).toBeDefined();
        expect(screen.queryByRole('listbox', { name: 'Contacts' })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Back to the address book' }));

        expect(screen.getByRole('listbox', { name: 'Contacts' })).toBeDefined();
    });

    it('goes back to the book from the person on one press of the back gesture where the two cannot stand together', async () => {
        atWidth(phone);
        const layers = shell();

        const { transport } = deployment();
        drawSpace(transport, { layers: layers.current });

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }));
        await screen.findByRole('heading', { name: 'Anna Kowalska' });

        act(() => {
            layers.current.closeTop();
        });

        expect(screen.getByRole('listbox', { name: 'Contacts' })).toBeDefined();
        expect(layers.current.depth).toBe(0);
    });

    it('leaves the back gesture alone where the person stands beside the book', async () => {
        const layers = shell();

        const { transport } = deployment();
        drawSpace(transport, { layers: layers.current });

        fireEvent.click(await screen.findByRole('option', { name: /Anna Kowalska/u }));
        await screen.findByRole('heading', { name: 'Anna Kowalska' });

        expect(layers.current.depth).toBe(0);
    });
});
