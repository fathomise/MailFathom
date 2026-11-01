// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { searchContacts, suggestionsFor, type ContactLookup } from './recipientSuggestions';

// Everybody a lookup offers, in the order the list draws them.
function everybodyIn(found: ContactLookup): readonly unknown[] {
    return found.groups.flatMap((group) => group.suggestions);
}

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

// Both of the reader's books answer with the one person, which is what the deployment does for somebody held in both,
// and the directory with nobody, which is what it answers a reader in no organization.
function answering(addresses: readonly string[], preferredAddress: string): MailFathomTransport {
    const body = JSON.stringify({
        contacts: [
            {
                id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
                displayName: 'Anna Kowalska',
                addresses,
                preferredAddress,
                note: null,
                phoneNumbers: [],
                origin: 'Asserted',
                book: 'Own',
                recordedAt: '2026-03-01T08:00:00+00:00',
                amendedAt: '2026-03-01T08:00:00+00:00',
                photo: null,
                organization: null,
            },
        ],
        nextCursor: null,
    });

    return (request) =>
        Promise.resolve({
            status: 200,
            body: request.path.includes('/contacts/colleagues')
                ? JSON.stringify({ contacts: [], nextCursor: null })
                : body,
            headers: {},
        });
}

function contactCalled(id: string, displayName: string, book: string): unknown {
    return {
        id,
        displayName,
        addresses: [`${id}@nordwind.example`],
        preferredAddress: `${id}@nordwind.example`,
        note: null,
        phoneNumbers: [],
        origin: book === 'Collected' ? 'Collected' : 'Asserted',
        book,
        organization: book === 'Organization' ? '0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f' : null,
        recordedAt: '2026-03-01T08:00:00+00:00',
        amendedAt: '2026-03-01T08:00:00+00:00',
        photo: null,
    };
}

function colleagueCalled(id: string, displayName: string, addresses = [`${id}@nordwind.example`]): unknown {
    return {
        id,
        book: 'Member',
        displayName,
        addresses,
        preferredAddress: addresses[0] ?? null,
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
}

function page(contacts: readonly unknown[]): string {
    return JSON.stringify({ contacts, nextCursor: null });
}

describe('searchContacts', () => {
    it('offers somebody found by one of their addresses at that address, which is the one being typed', async () => {
        const found = await searchContacts(
            session,
            answering(['anna@example.invalid', 'a.kowalska@work.example.invalid'], 'anna@example.invalid'),
            'work',
        );

        expect(found.incomplete).toBe(false);
        expect(everybodyIn(found)).toStrictEqual([
            { address: 'a.kowalska@work.example.invalid', name: 'Anna Kowalska' },
            { address: 'a.kowalska@work.example.invalid', name: 'Anna Kowalska' },
        ]);
    });

    it('offers somebody found by their name at the address they prefer', async () => {
        const found = await searchContacts(
            session,
            answering(['a.kowalska@work.example.invalid', 'anna@example.invalid'], 'anna@example.invalid'),
            'Anna K',
        );

        expect(everybodyIn(found)).toMatchObject([{ address: 'anna@example.invalid' }, {}]);
    });

    it.each([
        ['one half the grants reach failed beside one they do not', 403, 503, true],
        ['both halves failed', 503, 503, true],
        ['the grants reach neither half', 403, 403, false],
    ])('says whether a half went unread when %s', async (_, own, collected, incomplete) => {
        const transport: MailFathomTransport = (request) =>
            Promise.resolve({
                status: request.path.includes('/contacts/collected') ? collected : own,
                body: '',
                headers: {},
            });

        const found = await searchContacts(session, transport, 'anna');

        expect(found.incomplete).toBe(incomplete);
        expect(everybodyIn(found)).toStrictEqual([]);
    });
});

describe('searchContacts across the directory', () => {
    // The design's groups: the organization's members, then the contacts it keeps, then the reader's own, with what
    // their mail collected after the reader's own in that last group.
    it("offers colleagues, the organization's contacts, and the reader's own with the collected, each under its group", async () => {
        const transport: MailFathomTransport = (request) => {
            const body = request.path.includes('/contacts/colleagues')
                ? page([colleagueCalled('piotr', 'Piotr Zieliński')])
                : request.path.includes('/contacts/collected')
                  ? page([contactCalled('celina', 'Celina Wrona', 'Collected')])
                  : page([
                        contactCalled('anna', 'Anna Kowalska', 'Own'),
                        contactCalled('globe', 'Globe Travel', 'Organization'),
                    ]);

            return Promise.resolve({ status: 200, body, headers: {} });
        };

        const found = await searchContacts(session, transport, 'example');

        expect(
            found.groups.map((group) => [group.name, group.suggestions.map((suggestion) => suggestion.name)]),
        ).toEqual([
            ['colleagues', ['Piotr Zieliński']],
            ['organization', ['Globe Travel']],
            ['own', ['Anna Kowalska', 'Celina Wrona']],
        ]);
        expect(found.incomplete).toBe(false);
    });

    it('offers a colleague with their presence and title, and anybody else with neither', async () => {
        const transport: MailFathomTransport = (request) =>
            Promise.resolve({
                status: 200,
                body: request.path.includes('/contacts/colleagues')
                    ? page([
                          {
                              ...(colleagueCalled('piotr', 'Piotr Zieliński') as object),
                              presence: 'Offline',
                              jobTitle: 'Head of Delivery',
                          },
                      ])
                    : page(
                          request.path.includes('/contacts/collected')
                              ? []
                              : [contactCalled('anna', 'Anna Kowalska', 'Own')],
                      ),
                headers: {},
            });

        expect(everybodyIn(await searchContacts(session, transport, ''))).toStrictEqual([
            {
                address: 'piotr@nordwind.example',
                name: 'Piotr Zieliński',
                colleague: { presence: 'Offline', jobTitle: 'Head of Delivery', outOfOfficeUntil: null },
            },
            { address: 'anna@nordwind.example', name: 'Anna Kowalska' },
        ]);
    });

    // A suggestion is drawn as its person is everywhere else, so one with a photo names it and one without names none.
    it('names the photo a suggested person is drawn by, and none for somebody who has no photo', async () => {
        const transport: MailFathomTransport = (request) =>
            Promise.resolve({
                status: 200,
                body: request.path.includes('/contacts/colleagues')
                    ? page([{ ...(colleagueCalled('piotr', 'Piotr Zieliński') as object), photo: 'revision-1' }])
                    : page(
                          request.path.includes('/contacts/collected')
                              ? []
                              : [
                                    {
                                        ...(contactCalled('anna', 'Anna Kowalska', 'Own') as object),
                                        photo: 'stored-file',
                                    },
                                    contactCalled('bea', 'Bea Lind', 'Own'),
                                ],
                      ),
                headers: {},
            });

        const offered = everybodyIn(await searchContacts(session, transport, '')) as readonly { photo?: unknown }[];

        expect(offered.map((suggestion) => suggestion.photo)).toStrictEqual([
            { id: 'piotr', book: 'Member', photo: 'revision-1' },
            { id: 'anna', book: 'Own', photo: 'stored-file' },
            undefined,
        ]);
    });

    it('asks the directory for the term being typed', async () => {
        const asked: string[] = [];
        const transport: MailFathomTransport = (request) => {
            asked.push(request.path);

            return Promise.resolve({ status: 200, body: page([]), headers: {} });
        };

        await searchContacts(session, transport, 'piotr');

        expect(asked).toContain('https://mail.example.invalid/api/client/contacts/colleagues?pageSize=6&search=piotr');
    });

    it('asks each route for the three a group lists while nothing is typed', async () => {
        const asked: string[] = [];
        const transport: MailFathomTransport = (request) => {
            asked.push(request.path);

            return Promise.resolve({ status: 200, body: page([]), headers: {} });
        };

        await searchContacts(session, transport, '');

        expect(asked).toStrictEqual([
            'https://mail.example.invalid/api/client/contacts?pageSize=6',
            'https://mail.example.invalid/api/client/contacts/colleagues?pageSize=3',
            'https://mail.example.invalid/api/client/contacts/collected?pageSize=3',
        ]);
    });

    it('offers nobody for a colleague who holds no address to write to', async () => {
        const transport: MailFathomTransport = (request) =>
            Promise.resolve({
                status: 200,
                body: request.path.includes('/contacts/colleagues')
                    ? page([colleagueCalled('igor', 'Igor Pawlak', [])])
                    : page([]),
                headers: {},
            });

        const found = await searchContacts(session, transport, 'Igor');

        expect(everybodyIn(found)).toStrictEqual([]);
    });

    it('says the directory went unread where it failed', async () => {
        const transport: MailFathomTransport = (request) =>
            Promise.resolve({
                status: request.path.includes('/contacts/colleagues') ? 503 : 200,
                body: request.path.includes('/contacts/colleagues') ? '' : page([]),
                headers: {},
            });

        const found = await searchContacts(session, transport, 'anna');

        expect(found.incomplete).toBe(true);
    });
});

describe('suggestionsFor', () => {
    const many = (prefix: string) =>
        Array.from({ length: 8 }, (_, at) => ({ address: `${prefix}${at.toFixed(0)}@example.invalid`, name: null }));

    it('lists six a group once something is typed, and three while nothing is', () => {
        const book = [
            { name: 'colleagues' as const, suggestions: many('colleague') },
            { name: 'own' as const, suggestions: many('reader') },
        ];

        expect(suggestionsFor('example', [], book, []).groups.map((group) => group.suggestions.length)).toEqual([6, 6]);
        expect(suggestionsFor('', [], book, []).groups.map((group) => group.suggestions.length)).toEqual([3, 3]);
    });

    it('leaves out a group nobody in it matches', () => {
        const offered = suggestionsFor(
            'anna',
            [],
            [
                { name: 'colleagues', suggestions: [{ address: 'piotr@example.invalid', name: 'Piotr' }] },
                { name: 'own', suggestions: [{ address: 'anna@example.invalid', name: 'Anna' }] },
            ],
            [],
        );

        expect(offered.groups.map((group) => group.name)).toEqual(['own']);
    });

    it('lists one person once, however many places they were found in', () => {
        const offered = suggestionsFor(
            'anna',
            [{ address: 'anna@example.invalid', name: 'Anna' }],
            [
                { name: 'colleagues', suggestions: [{ address: 'ANNA@example.invalid', name: 'Anna Kowalska' }] },
                { name: 'own', suggestions: [{ address: 'ANNA@example.invalid', name: 'Anna Kowalska' }] },
            ],
            [],
        );

        expect(offered).toStrictEqual({
            participants: [{ address: 'anna@example.invalid', name: 'Anna' }],
            groups: [],
        });
    });
});
