// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadingZoneContext } from '../localization/useReadingZone';
import { mostRecipientsInOneHeader } from './composition';
import type { RecipientSuggestion } from './recipientSuggestions';
import { RecipientField } from './RecipientField';

const emptyFieldSays = 'add a recipient…';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

// One person as the book answers them, in whichever half of it the test puts them.
function contact(identity: string, displayName: string, addresses: readonly [string, ...string[]]) {
    return {
        id: `0198f4a1-2b6c-7a1d-9f3e-${identity}`,
        displayName,
        addresses,
        preferredAddress: addresses[0],
        note: null,
        phoneNumbers: [],
        origin: 'Asserted',
        book: 'Own',
        recordedAt: '2026-03-01T08:00:00+00:00',
        amendedAt: '2026-03-01T08:00:00+00:00',
        photo: null,
        organization: null,
    };
}

/** What one half of the book answers a search with. */
interface BookAnswer {
    readonly status: number;
    readonly contacts?: readonly unknown[];
}

/** What the books answer, so a test states only the people it is about; the directory answers nobody unless stated. */
interface Books {
    readonly own?: BookAnswer;
    readonly colleagues?: BookAnswer;
    readonly collected?: BookAnswer;
}

function pageOf(answer: BookAnswer = { status: 200 }): { status: number; body: string } {
    return { status: answer.status, body: JSON.stringify({ contacts: answer.contacts ?? [], nextCursor: null }) };
}

function deployment(books: Books): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            const answer = pageOf(
                request.path.includes('/contacts/collected')
                    ? books.collected
                    : request.path.includes('/contacts/colleagues')
                      ? books.colleagues
                      : books.own,
            );

            return Promise.resolve({ ...answer, headers: {} });
        },
    };
}

function drawField(
    addresses: readonly string[] = [],
    participants: readonly RecipientSuggestion[] = [],
    books: Books = {},
    organizationName: string | null = null,
    readingZone: string | null = null,
): { changed: ReturnType<typeof vi.fn>; asked: ClientRequest[] } {
    const changed = vi.fn();
    const { transport, asked } = deployment(books);

    render(
        <LocalizationProvider>
            <ReadingZoneContext value={readingZone}>
                <RecipientField
                    label="To"
                    placeholder={emptyFieldSays}
                    addresses={addresses}
                    participants={participants}
                    session={session}
                    transport={transport}
                    organizationName={organizationName}
                    onChanged={changed}
                />
            </ReadingZoneContext>
        </LocalizationProvider>,
    );

    return { changed, asked };
}

// Typing as somebody does it: into a field that has the focus, which is what the list is drawn under.
function typeInto(text: string): void {
    fireEvent.focus(field());
    fireEvent.change(field(), { target: { value: text } });
}

// What an entry reads out: its words without the avatar's letters, which are drawn for the eye and hidden from the
// accessibility tree.
function readOut(element: HTMLElement): string {
    const copy = element.cloneNode(true) as HTMLElement;

    for (const hidden of copy.querySelectorAll('[aria-hidden="true"]')) {
        hidden.remove();
    }

    return copy.textContent;
}

function field(): HTMLElement {
    return screen.getByLabelText('To');
}

describe('RecipientField', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('writes an address in when it is finished with Enter', () => {
        const { changed } = drawField();

        fireEvent.change(field(), { target: { value: 'ada@example.invalid' } });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).toHaveBeenCalledWith(['ada@example.invalid']);
    });

    it('writes one address in when the next is started with a comma', () => {
        const { changed } = drawField();

        fireEvent.change(field(), { target: { value: 'ada@example.invalid,' } });

        expect(changed).toHaveBeenCalledWith(['ada@example.invalid']);
    });

    it('empties the field for a comma with nothing before it, as it does for an address', () => {
        const { changed } = drawField();

        fireEvent.change(field(), { target: { value: ',' } });

        expect((field() as HTMLInputElement).value).toBe('');
        expect(changed).not.toHaveBeenCalled();
    });

    it('writes in what was left in the field when the field is left', () => {
        const { changed } = drawField(['bo@example.invalid']);

        fireEvent.change(field(), { target: { value: 'ada@example.invalid' } });
        fireEvent.blur(field());

        expect(changed).toHaveBeenCalledWith(['bo@example.invalid', 'ada@example.invalid']);
    });

    it('writes nothing in for an empty field, which is every time somebody tabs past it', () => {
        const { changed } = drawField();

        fireEvent.blur(field());

        expect(changed).not.toHaveBeenCalled();
    });

    it('says why a half-written address was refused rather than doing nothing', () => {
        const { changed } = drawField();

        fireEvent.change(field(), { target: { value: 'ada' } });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).not.toHaveBeenCalled();
        expect(screen.getByRole('alert').textContent).toContain('That is not an address yet');
    });

    it('says an address is already written here rather than writing it twice', () => {
        const { changed } = drawField(['ada@example.invalid']);

        fireEvent.change(field(), { target: { value: 'ada@example.invalid' } });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).not.toHaveBeenCalled();
        expect(screen.getByRole('alert').textContent).toContain('is written here already');
    });

    it('says what one header takes once it is full', () => {
        const full = Array.from(
            { length: mostRecipientsInOneHeader },
            (_, at) => `reader${String(at)}@example.invalid`,
        );
        const { changed } = drawField(full);

        fireEvent.change(field(), { target: { value: 'ada@example.invalid' } });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).not.toHaveBeenCalled();
        expect(screen.getByRole('alert').textContent).toContain('at most 256 addresses');
    });

    it('takes one address back off from its own control, named for the address and the header', () => {
        const { changed } = drawField(['ada@example.invalid', 'bo@example.invalid']);

        fireEvent.click(screen.getByRole('button', { name: 'Remove ada@example.invalid from To' }));

        expect(changed).toHaveBeenCalledWith(['bo@example.invalid']);
    });

    it('offers the people in the conversation by a name as well as an address, before anybody in the book', async () => {
        drawField(
            [],
            [
                { address: 'billing@example.invalid', name: 'Anna Billing' },
                { address: 'bo@example.invalid', name: null },
            ],
            { own: { status: 200, contacts: [contact('000000000001', 'Anna Kowalska', ['anna@example.invalid'])] } },
        );

        typeInto('anna');

        const list = await screen.findByRole('listbox', { name: 'Suggested recipients for To' });

        await waitFor(() => {
            expect(within(list).getAllByRole('option')).toHaveLength(2);
        });
        expect(
            within(list)
                .getAllByRole('option')
                .map((option) => readOut(option)),
        ).toStrictEqual(['Anna Billingbilling@example.invalid', 'Anna Kowalskaanna@example.invalid']);
        expect(field().getAttribute('aria-expanded')).toBe('true');
        expect(field().getAttribute('aria-controls')).toBe(list.id);
    });

    it('offers the book where nobody is in a conversation yet, from both halves of it', async () => {
        const { asked } = drawField([], [], {
            own: { status: 200, contacts: [contact('000000000001', 'Ada Lovelace', ['ada@example.invalid'])] },
            collected: {
                status: 200,
                contacts: [contact('000000000002', 'Adam Nowak', ['adam@example.invalid', 'a.nowak@example.invalid'])],
            },
        });

        typeInto('ada');

        await waitFor(() => {
            expect(screen.getAllByRole('option')).toHaveLength(2);
        });
        expect(asked.map((request) => request.path)).toStrictEqual([
            'https://mail.example.invalid/api/client/contacts?pageSize=12&search=ada',
            'https://mail.example.invalid/api/client/contacts/colleagues?pageSize=6&search=ada',
            'https://mail.example.invalid/api/client/contacts/collected?pageSize=6&search=ada',
        ]);
    });

    it('searches the book once for a word typed quickly, rather than once per letter', async () => {
        const { asked } = drawField();

        fireEvent.focus(field());

        for (const text of ['a', 'an', 'ann']) {
            fireEvent.change(field(), { target: { value: text } });
        }

        await waitFor(() => {
            expect(asked).toHaveLength(3);
        });
        expect(asked.every((request) => request.path.endsWith('search=ann'))).toBe(true);
    });

    it('narrows what is already listed with every keystroke, before the next answer arrives', () => {
        drawField(
            [],
            [
                { address: 'anna@example.invalid', name: 'Anna' },
                { address: 'annika@example.invalid', name: 'Annika' },
            ],
        );

        typeInto('ann');

        expect(screen.getAllByRole('option')).toHaveLength(2);

        fireEvent.change(field(), { target: { value: 'anni' } });

        expect(screen.getAllByRole('option').map((option) => readOut(option))).toStrictEqual([
            'Annikaannika@example.invalid',
        ]);
    });

    it('never offers an address the header already holds, however it is spelled', () => {
        drawField(['anna@example.invalid'], [{ address: 'Anna@example.invalid', name: 'Anna' }]);

        typeInto('anna');

        expect(screen.queryByRole('listbox')).toBeNull();
    });

    it('walks the list with the arrows, wrapping at either end, and writes in the one in force on Enter', () => {
        const { changed } = drawField(
            [],
            [
                { address: 'anna@example.invalid', name: 'Anna' },
                { address: 'annika@example.invalid', name: 'Annika' },
            ],
        );

        typeInto('ann');
        fireEvent.keyDown(field(), { key: 'ArrowDown' });
        fireEvent.keyDown(field(), { key: 'ArrowDown' });
        fireEvent.keyDown(field(), { key: 'ArrowDown' });

        const [first] = screen.getAllByRole('option');

        expect(first?.getAttribute('aria-selected')).toBe('true');
        expect(field().getAttribute('aria-activedescendant')).toBe(first?.id);

        fireEvent.keyDown(field(), { key: 'ArrowUp' });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).toHaveBeenCalledWith(['annika@example.invalid']);
        expect((field() as HTMLInputElement).value).toBe('');
    });

    it('brings the entry the arrows put in force into the bounded list’s view', async () => {
        const scrolled = vi.spyOn(Element.prototype, 'scrollIntoView');

        drawField(
            [],
            [
                { address: 'anna@example.invalid', name: 'Anna' },
                { address: 'annika@example.invalid', name: 'Annika' },
            ],
        );

        typeInto('ann');
        fireEvent.keyDown(field(), { key: 'ArrowDown' });
        fireEvent.keyDown(field(), { key: 'ArrowDown' });

        const [, second] = screen.getAllByRole('option');

        await waitFor(() => {
            expect(scrolled.mock.contexts.at(-1)).toBe(second);
        });
        expect(scrolled).toHaveBeenLastCalledWith({ block: 'nearest' });

        scrolled.mockRestore();
    });

    it('writes in an entry pressed with the pointer, rather than the half-typed text the press left behind', () => {
        const { changed } = drawField([], [{ address: 'anna@example.invalid', name: 'Anna' }]);

        typeInto('ann');
        fireEvent.click(screen.getByRole('option', { name: /Anna/u }));

        expect(changed).toHaveBeenCalledTimes(1);
        expect(changed).toHaveBeenCalledWith(['anna@example.invalid']);
    });

    it('puts the list away on Escape, keeps what was typed, and brings it back on an arrow', () => {
        drawField([], [{ address: 'anna@example.invalid', name: 'Anna' }]);

        typeInto('ann');
        fireEvent.keyDown(field(), { key: 'Escape' });

        expect(screen.queryByRole('listbox')).toBeNull();
        expect(field().getAttribute('aria-expanded')).toBe('false');
        expect((field() as HTMLInputElement).value).toBe('ann');

        fireEvent.keyDown(field(), { key: 'ArrowDown' });

        expect(screen.getByRole('listbox')).toBeDefined();
    });

    it('says the book could not be searched, and names no list it did not draw, over a book it never read', async () => {
        drawField([], [], { own: { status: 503 }, collected: { status: 503 } });

        typeInto('ann');

        expect(await screen.findByText(/Not every contact could be searched just now/u)).toBeDefined();
        expect(screen.queryByRole('listbox')).toBeNull();
        expect(field().getAttribute('aria-expanded')).toBe('false');
        expect(field().getAttribute('aria-controls')).toBeNull();
    });

    it('puts the note away on Escape and brings it back on an arrow, as it does the list', async () => {
        drawField([], [], { own: { status: 503 }, collected: { status: 503 } });

        typeInto('ann');
        await screen.findByText(/Not every contact could be searched just now/u);
        fireEvent.keyDown(field(), { key: 'Escape' });

        expect(screen.queryByText(/Not every contact could be searched just now/u)).toBeNull();

        fireEvent.keyDown(field(), { key: 'ArrowDown' });

        expect(screen.getByText(/Not every contact could be searched just now/u)).toBeDefined();
    });

    it('says the book went unread only for the term it was asked about, not for one typed since', async () => {
        vi.useFakeTimers();

        drawField([], [], { own: { status: 503 }, collected: { status: 503 } });

        typeInto('ann');
        await act(() => vi.runAllTimersAsync());

        expect(screen.getByText(/Not every contact could be searched just now/u)).toBeDefined();

        typeInto('anna');

        expect(screen.queryByText(/Not every contact could be searched just now/u)).toBeNull();

        await act(() => vi.runAllTimersAsync());

        expect(screen.getByText(/Not every contact could be searched just now/u)).toBeDefined();
    });

    it('says so when the one half the grants reach went unread, rather than reading as a book with nobody in it', async () => {
        drawField([], [], { own: { status: 403 }, collected: { status: 503 } });

        typeInto('ann');

        expect(await screen.findByText(/Not every contact could be searched just now/u)).toBeDefined();
    });

    it('lists the half that answered, and says the other went unread', async () => {
        drawField([], [], {
            own: { status: 200, contacts: [contact('000000000001', 'Anna Kowalska', ['anna@example.invalid'])] },
            collected: { status: 503 },
        });

        typeInto('ann');

        expect(await screen.findByRole('option', { name: /anna@example\.invalid/u })).toBeDefined();
        expect(screen.getByText(/Not every contact could be searched just now/u)).toBeDefined();
    });

    it('offers nobody from the book, and says nothing about it, to somebody whose grants do not reach it', async () => {
        vi.useFakeTimers();

        const { asked } = drawField([], [], { own: { status: 403 }, collected: { status: 403 } });

        typeInto('ann');
        await act(() => vi.runAllTimersAsync());

        expect(asked).toHaveLength(3);
        expect(screen.queryByText(/could not be searched/u)).toBeNull();
        expect(screen.queryByRole('listbox')).toBeNull();
    });

    it('lists three of each group as soon as the field has the focus, before anything is typed', async () => {
        const { asked } = drawField([], [], {
            own: {
                status: 200,
                contacts: ['1', '2', '3', '4'].map((at) =>
                    contact(`00000000000${at}`, `Reader ${at}`, [`reader${at}@example.invalid`]),
                ),
            },
        });

        fireEvent.focus(field());

        const group = await screen.findByRole('group', { name: 'Your contacts' });

        expect(within(group).getAllByRole('option')).toHaveLength(3);
        expect(asked.map((request) => request.path)).toContain(
            'https://mail.example.invalid/api/client/contacts?pageSize=6',
        );
    });

    it("draws the book in the design's groups, the organization's under its name, and walks them as one list", async () => {
        const member = {
            id: 'member-piotr',
            book: 'Member',
            displayName: 'Piotr Zieliński',
            addresses: ['piotr@nordwind.example'],
            preferredAddress: 'piotr@nordwind.example',
            phoneNumbers: [],
            jobTitle: null,
            organizationName: 'Nordwind',
            timeZone: null,
            photo: null,
            presence: 'Active',
            lastSeen: null,
            outOfOfficeUntil: null,
            usesPhoneApp: false,
        };
        const { changed } = drawField(
            [],
            [],
            {
                colleagues: { status: 200, contacts: [member] },
                own: {
                    status: 200,
                    contacts: [
                        {
                            ...contact('000000000001', 'Globe Travel', ['globe@nordwind.example']),
                            book: 'Organization',
                            organization: '0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f',
                        },
                        contact('000000000002', 'Anna Kowalska', ['anna@nordwind.example']),
                    ],
                },
            },
            'Nordwind',
        );

        typeInto('nordwind');

        const list = await screen.findByRole('listbox', { name: 'Suggested recipients for To' });

        await waitFor(() => {
            expect(
                within(list)
                    .getAllByRole('group')
                    .map((group) => group.getAttribute('aria-labelledby')),
            ).toHaveLength(3);
        });
        expect(
            within(list)
                .getAllByRole('group')
                .map((group) => [
                    document.getElementById(group.getAttribute('aria-labelledby') ?? '')?.textContent,
                    within(group).getAllByRole('option').length,
                ]),
        ).toStrictEqual([
            ['Colleagues', 1],
            ['Nordwind contacts', 1],
            ['Your contacts', 1],
        ]);

        fireEvent.keyDown(field(), { key: 'ArrowDown' });
        fireEvent.keyDown(field(), { key: 'ArrowDown' });
        fireEvent.keyDown(field(), { key: 'ArrowDown' });
        fireEvent.keyDown(field(), { key: 'Enter' });

        expect(changed).toHaveBeenCalledWith(['anna@nordwind.example']);
    });

    it('tells a colleague by their presence and title, and anybody else by their address', async () => {
        drawField([], [], {
            colleagues: {
                status: 200,
                contacts: [
                    {
                        id: 'member-piotr',
                        book: 'Member',
                        displayName: 'Piotr Zieliński',
                        addresses: ['piotr@nordwind.example'],
                        preferredAddress: 'piotr@nordwind.example',
                        phoneNumbers: [],
                        jobTitle: 'Head of Delivery',
                        organizationName: 'Nordwind',
                        timeZone: null,
                        photo: null,
                        presence: 'Offline',
                        lastSeen: null,
                        outOfOfficeUntil: null,
                    },
                ],
            },
            own: { status: 200, contacts: [contact('000000000002', 'Anna Kowalska', ['anna@nordwind.example'])] },
        });

        typeInto('nordwind');

        const list = await screen.findByRole('listbox', { name: 'Suggested recipients for To' });

        await waitFor(() => {
            expect(within(list).getAllByRole('option')).toHaveLength(2);
        });
        expect(
            within(list)
                .getAllByRole('option')
                .map((option) => readOut(option)),
        ).toStrictEqual(['Piotr ZielińskiOffline · Head of Delivery', 'Anna Kowalskaanna@nordwind.example']);
    });

    // The reader's zone is far east of UTC, where that instant already falls on the next day, so the day said is theirs.
    it('adds the last day a suggested colleague is out of office to the line under their name', async () => {
        drawField(
            [],
            [],
            {
                colleagues: {
                    status: 200,
                    contacts: [
                        {
                            id: 'member-piotr',
                            book: 'Member',
                            displayName: 'Piotr Zieliński',
                            addresses: ['piotr@nordwind.example'],
                            preferredAddress: 'piotr@nordwind.example',
                            phoneNumbers: [],
                            jobTitle: 'Head of Delivery',
                            organizationName: 'Nordwind',
                            timeZone: null,
                            photo: null,
                            presence: 'Offline',
                            lastSeen: null,
                            outOfOfficeUntil: '2026-11-04T12:00:00Z',
                        },
                    ],
                },
            },
            null,
            'Pacific/Kiritimati',
        );

        typeInto('piotr');

        const option = await screen.findByRole('option');

        expect(readOut(option)).toBe('Piotr ZielińskiOffline · Head of Delivery · out until 11/05');
    });
});
