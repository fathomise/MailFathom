// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    addAuthorToContacts,
    amendContact,
    eraseContact,
    promoteContact,
    readCollectedContacts,
    readContact,
    readOwnContacts,
    recordContact,
    type ContactRecord,
} from './contacts';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const asserted = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
    displayName: 'Anna Kowalska',
    addresses: ['anna@example.invalid', 'a.kowalska@example.invalid'],
    preferredAddress: 'anna@example.invalid',
    note: 'Renewal owner',
    phoneNumbers: [{ number: '+1 202 555 0143', label: 'Mobile' }],
    origin: 'Asserted',
    book: 'Own',
    recordedAt: '2026-03-01T08:00:00+00:00',
    amendedAt: '2026-03-04T09:01:12+00:00',
    photo: null,
};

const pageBody = JSON.stringify({ contacts: [asserted], nextCursor: 'page-two' });

// The transport is the network boundary and the whole of what a test here fakes. No operation in this module reads a
// header off an answer, so every helper supplies the empty set.
type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

const record: ContactRecord = {
    displayName: 'Anna Kowalska',
    addresses: ['anna@example.invalid'],
    preferredAddress: 'anna@example.invalid',
    note: null,
    phoneNumbers: [{ number: '+1 202 555 0143', label: 'Mobile' }],
};

describe('readOwnContacts', () => {
    it('asks for the asserted book with the window it will render', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnContacts(session, transport);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts?pageSize=50');
    });

    it('carries the cursor a previous page answered with', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnContacts(session, transport, { pageSize: 25, cursor: 'page two/3' });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts?pageSize=25&cursor=page%20two%2F3',
        );
    });

    it('narrows the book by what somebody typed, trimmed and escaped for the address', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnContacts(session, transport, { pageSize: 8, search: ' Kowal & Co ' });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts?pageSize=8&search=Kowal%20%26%20Co',
        );
    });

    it('asks for the whole book where the search is blank', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnContacts(session, transport, { search: '   ' });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts?pageSize=50');
    });

    it('asks for no more than the page the route will serve', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readOwnContacts(session, transport, { pageSize: 5000 });

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts?pageSize=200');
    });

    it('reads the page a well-formed answer describes', async () => {
        const result = await readOwnContacts(session, answering({ status: 200, body: pageBody }));

        expect(result).toEqual({
            outcome: 'read',
            value: {
                contacts: [
                    {
                        id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
                        displayName: 'Anna Kowalska',
                        addresses: ['anna@example.invalid', 'a.kowalska@example.invalid'],
                        preferredAddress: 'anna@example.invalid',
                        note: 'Renewal owner',
                        phoneNumbers: [{ number: '+1 202 555 0143', label: 'Mobile' }],
                        origin: 'Asserted',
                        book: 'Own',
                        recordedAt: '2026-03-01T08:00:00+00:00',
                        amendedAt: '2026-03-04T09:01:12+00:00',
                        photo: null,
                        organization: null,
                    },
                ],
                nextCursor: 'page-two',
            },
        });
    });

    it('reads the organization whose book keeps a contact, and refuses one named by nothing', async () => {
        const keptBy = (organization: unknown) =>
            readOwnContacts(
                session,
                answering({
                    status: 200,
                    body: JSON.stringify({
                        contacts: [{ ...asserted, book: 'Organization', organization }],
                        nextCursor: null,
                    }),
                }),
            );

        const kept = await keptBy('0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f');

        expect(kept.outcome === 'read' && kept.value.contacts[0]?.organization).toBe(
            '0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f',
        );
        expect((await keptBy('')).outcome).toBe('failed');
        expect((await keptBy(7)).outcome).toBe('failed');
    });

    it('reads the end of the book as a page carrying no cursor', async () => {
        const body = JSON.stringify({ contacts: [], nextCursor: null });

        const result = await readOwnContacts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'read', value: { contacts: [], nextCursor: null } });
    });

    it('refuses an answer carrying more contacts than the page it asked for', async () => {
        const body = JSON.stringify({ contacts: [asserted, asserted], nextCursor: null });

        const result = await readOwnContacts(session, answering({ status: 200, body }), { pageSize: 1 });

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        ['a body that is not a record', '[]'],
        ['a contact carrying no addresses at all', JSON.stringify({ contacts: [{ ...asserted, addresses: [] }] })],
        ['an origin this surface does not publish', JSON.stringify({ contacts: [{ ...asserted, origin: 'Guessed' }] })],
        ['an address that is not text', JSON.stringify({ contacts: [{ ...asserted, addresses: [7] }] })],
        [
            'a contact that does not say which numbers it holds',
            JSON.stringify({ contacts: [{ ...asserted, phoneNumbers: undefined }] }),
        ],
        ['phone numbers that are not a list', JSON.stringify({ contacts: [{ ...asserted, phoneNumbers: {} }] })],
        ['a phone number that is not a record', JSON.stringify({ contacts: [{ ...asserted, phoneNumbers: ['+1'] }] })],
        [
            'a phone number whose number is not text',
            JSON.stringify({ contacts: [{ ...asserted, phoneNumbers: [{ number: 12025550143, label: 'Work' }] }] }),
        ],
        [
            'a phone label this surface does not publish',
            JSON.stringify({
                contacts: [{ ...asserted, phoneNumbers: [{ number: '+1 202 555 0143', label: 'Pager' }] }],
            }),
        ],
        [
            'more phone numbers than a contact may hold',
            JSON.stringify({
                contacts: [{ ...asserted, phoneNumbers: Array.from({ length: 17 }, () => asserted.phoneNumbers[0]) }],
            }),
        ],
        ['a cursor that is not text', JSON.stringify({ contacts: [], nextCursor: 3 })],
    ])('refuses %s', async (_, body) => {
        const result = await readOwnContacts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a deployment that never answered as one to retry', async () => {
        const result = await readOwnContacts(session, () => Promise.reject(new Error('refused')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('reads a %i as %s', async (status, reason) => {
        const result = await readOwnContacts(session, answering({ status, body: '' }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason, status } });
    });
});

describe('readCollectedContacts', () => {
    it('asks for the collected book rather than the asserted one', async () => {
        const { transport, requests } = recording({ status: 200, body: pageBody });

        await readCollectedContacts(session, transport);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts/collected?pageSize=50');
    });
});

describe('readContact', () => {
    it('asks for the contact the identity names', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(asserted) });

        await readContact(session, transport, '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90');

        expect(requests[0]).toEqual({
            method: 'GET',
            path: 'https://mail.example.invalid/api/client/contacts/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
            headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
            longestAnswer: 32768,
        });
    });

    it('reads the photo a contact names by its stored file', async () => {
        const result = await readContact(
            session,
            answering({
                status: 200,
                body: JSON.stringify({ ...asserted, photo: '0198f4a1-0000-7000-8000-00000000f001' }),
            }),
            asserted.id,
        );

        expect(result).toMatchObject({ outcome: 'read', value: { photo: '0198f4a1-0000-7000-8000-00000000f001' } });
    });

    it.each([
        ['a photo that is not an identifier', { ...asserted, photo: 42 }],
        ['a contact that does not say whether it has a photo', { ...asserted, photo: undefined }],
    ])('refuses %s', async (_, body) => {
        const result = await readContact(session, answering({ status: 200, body: JSON.stringify(body) }), asserted.id);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a contact the organization keeps as one in the organization\'s book', async () => {
        const kept = { ...asserted, book: 'Organization', organization: '0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f' };

        const result = await readContact(session, answering({ status: 200, body: JSON.stringify(kept) }), 'anna');

        expect(result.outcome === 'read' && result.value.book).toBe('Organization');
    });

    it.each([
        ['the organization\'s book naming no organization', { book: 'Organization', organization: null }],
        ['the reader\'s own book naming an organization', { book: 'Own', organization: '0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f' }],
    ])('refuses a contact whose book and organization disagree: %s', async (_, held) => {
        const result = await readContact(
            session,
            answering({ status: 200, body: JSON.stringify({ ...asserted, ...held }) }),
            'anna',
        );

        expect(result.outcome).toBe('failed');
    });

    it('refuses a contact naming no book, or one this surface does not publish', async () => {
        const unnamed = Object.fromEntries(Object.entries(asserted).filter(([field]) => field !== 'book'));

        for (const body of [unnamed, { ...asserted, book: 'Member' }]) {
            const result = await readContact(session, answering({ status: 200, body: JSON.stringify(body) }), 'anna');

            expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
        }
    });

    it('reads a contact the books no longer hold as something to let go of', async () => {
        const result = await readContact(session, answering({ status: 404, body: '' }), 'gone');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });
});

describe('amendContact', () => {
    it('states the whole record at the one contact it amends', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ outcome: 'Written', contact: asserted, addressHolder: null }),
        });

        await amendContact(session, transport, 'a/b', record);

        expect(requests[0]).toEqual({
            method: 'PUT',
            path: 'https://mail.example.invalid/api/client/contacts/a%2Fb',
            headers: {
                Accept: 'application/json',
                Authorization: 'Basic dGVzdA==',
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(record),
            longestAnswer: 32768,
        });
    });

    it('reads an address another contact holds as the outcome the book answered', async () => {
        const body = JSON.stringify({ outcome: 'AddressHeldByAnotherContact', contact: null, addressHolder: 'x' });

        const result = await amendContact(session, answering({ status: 200, body }), 'a', record);

        expect(result.outcome === 'read' && result.value.outcome).toBe('AddressHeldByAnotherContact');
    });

    it('reads the refusal of a contact the organization keeps as an outcome a screen can say', async () => {
        const body = JSON.stringify({ title: 'Read-only', status: 409, errorCode: 51017 });

        const result = await amendContact(session, answering({ status: 409, body }), 'anna', record);

        expect(result).toEqual({
            outcome: 'read',
            value: { outcome: 'OrganizationContactReadOnly', contact: null, addressHolder: null },
        });
    });

    it('reads a conflict naming any other rule as the failure its status names', async () => {
        const body = JSON.stringify({ title: 'Conflict', status: 409, errorCode: 51001 });

        const result = await amendContact(session, answering({ status: 409, body }), 'anna', record);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 409 } });
    });

    it('names the addresses the user vouches for beside the record that keeps them', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ outcome: 'Written', contact: asserted, addressHolder: null }),
        });

        await amendContact(session, transport, 'a', record, ['anna@example.invalid']);

        expect(requests[0]?.body).toBe(JSON.stringify({ ...record, restatedAddresses: ['anna@example.invalid'] }));
    });
});

describe('addAuthorToContacts', () => {
    const written = JSON.stringify({ outcome: 'Written', contact: asserted, addressHolder: null });

    it('records an author nobody holds as a new contact named by the name the message displays', async () => {
        const { transport, requests } = recording({ status: 200, body: written });

        await addAuthorToContacts(session, transport, {
            displayName: 'Anna Kowalska',
            address: 'anna@example.invalid',
            restatedContactId: null,
        });

        expect(requests.map((request) => [request.method, request.path, request.body])).toEqual([
            [
                'POST',
                'https://mail.example.invalid/api/client/contacts',
                JSON.stringify({ ...record, phoneNumbers: [] }),
            ],
        ]);
    });

    it('states the contact already holding the address again, unchanged, with that address restated', async () => {
        const requests: ClientRequest[] = [];
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            return Promise.resolve({
                status: 200,
                body: request.method === 'GET' ? JSON.stringify(asserted) : written,
                headers: {},
            });
        };

        await addAuthorToContacts(session, transport, {
            displayName: 'Anna',
            address: 'a.kowalska@example.invalid',
            restatedContactId: asserted.id,
        });

        expect(requests.map((request) => [request.method, request.body ?? null])).toEqual([
            ['GET', null],
            [
                'PUT',
                JSON.stringify({
                    displayName: asserted.displayName,
                    addresses: asserted.addresses,
                    preferredAddress: asserted.preferredAddress,
                    note: asserted.note,
                    phoneNumbers: asserted.phoneNumbers,
                    restatedAddresses: ['a.kowalska@example.invalid'],
                }),
            ],
        ]);
    });

    it('writes nothing where the contact to state again could not be read', async () => {
        const { transport, requests } = recording({ status: 404, body: '' });

        const result = await addAuthorToContacts(session, transport, {
            displayName: 'Anna',
            address: 'anna@example.invalid',
            restatedContactId: asserted.id,
        });

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
        expect(requests.map((request) => request.method)).toEqual(['GET']);
    });
});

describe('recordContact', () => {
    it('states the whole record on the asserted book', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ outcome: 'Written', contact: asserted, addressHolder: null }),
        });

        await recordContact(session, transport, record);

        expect(requests[0]).toEqual({
            method: 'POST',
            path: 'https://mail.example.invalid/api/client/contacts',
            headers: {
                Accept: 'application/json',
                Authorization: 'Basic dGVzdA==',
                'Content-Type': 'application/json',
            },
            body: JSON.stringify(record),
            longestAnswer: 32768,
        });
    });

    it('reads a write the book performed as the record it settled', async () => {
        const body = JSON.stringify({ outcome: 'Written', contact: asserted, addressHolder: null });

        const result = await recordContact(session, answering({ status: 200, body }), record);

        expect(result).toEqual({
            outcome: 'read',
            value: {
                outcome: 'Written',
                contact: {
                    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
                    displayName: 'Anna Kowalska',
                    addresses: ['anna@example.invalid', 'a.kowalska@example.invalid'],
                    preferredAddress: 'anna@example.invalid',
                    note: 'Renewal owner',
                    phoneNumbers: [{ number: '+1 202 555 0143', label: 'Mobile' }],
                    origin: 'Asserted',
                    book: 'Own',
                    recordedAt: '2026-03-01T08:00:00+00:00',
                    amendedAt: '2026-03-04T09:01:12+00:00',
                    photo: null,
                    organization: null,
                },
                addressHolder: null,
            },
        });
    });

    it('reads an address another contact already holds as an outcome rather than a failure', async () => {
        const body = JSON.stringify({
            outcome: 'AddressHeldByAnotherContact',
            contact: null,
            addressHolder: '0198f4c2-7d8e-7b2f-a041-52c637d8e9fa',
        });

        const result = await recordContact(session, answering({ status: 200, body }), record);

        expect(result).toEqual({
            outcome: 'read',
            value: {
                outcome: 'AddressHeldByAnotherContact',
                contact: null,
                addressHolder: '0198f4c2-7d8e-7b2f-a041-52c637d8e9fa',
            },
        });
    });

    it('refuses an outcome this surface does not publish', async () => {
        const body = JSON.stringify({ outcome: 'Maybe', contact: null, addressHolder: null });

        const result = await recordContact(session, answering({ status: 200, body }), record);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a record the request itself broke as a refusal naming the rule', async () => {
        const result = await recordContact(session, answering({ status: 400, body: '' }), record);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: 400 } });
    });
});

describe('promoteContact', () => {
    it('asks the promotion route for the contact it names, stating no record', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ outcome: 'Written', contact: null, addressHolder: null }),
        });

        await promoteContact(session, transport, '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90');

        expect(requests[0]).toEqual({
            method: 'POST',
            path: 'https://mail.example.invalid/api/client/contacts/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/promotion',
            headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
            longestAnswer: 32768,
        });
    });

    it('reads a promotion of somebody already asserted as an outcome rather than a failure', async () => {
        const body = JSON.stringify({ outcome: 'AlreadyAsserted', contact: null, addressHolder: null });

        const result = await promoteContact(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({
            outcome: 'read',
            value: { outcome: 'AlreadyAsserted', contact: null, addressHolder: null },
        });
    });
});

describe('eraseContact', () => {
    it('asks the contact route to erase the person it names', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ contact: 'anna', wasHeld: true, addressesErased: 2 }),
        });

        await eraseContact(session, transport, 'anna');

        expect(requests[0]?.method).toBe('DELETE');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/contacts/anna');
    });

    it('reads what the erasure removed', async () => {
        const body = JSON.stringify({ contact: 'anna', wasHeld: true, addressesErased: 2 });

        const result = await eraseContact(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'read', value: { contact: 'anna', wasHeld: true, addressesErased: 2 } });
    });

    it('reads a book that held nobody of that identity as an erasure that removed nothing', async () => {
        const body = JSON.stringify({ contact: 'anna', wasHeld: false, addressesErased: 0 });

        const result = await eraseContact(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'read', value: { contact: 'anna', wasHeld: false, addressesErased: 0 } });
    });

    it('reads the refusal to erase a contact the organization keeps as that refusal', async () => {
        const body = JSON.stringify({ title: 'Read-only', status: 409, errorCode: 51017 });

        const result = await eraseContact(session, answering({ status: 409, body }), 'anna');

        expect(result).toEqual({ outcome: 'read', value: 'OrganizationContactReadOnly' });
    });

    it('refuses an answer that does not say whether the book held them', async () => {
        const body = JSON.stringify({ contact: 'anna', addressesErased: 0 });

        const result = await eraseContact(session, answering({ status: 200, body }), 'anna');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
