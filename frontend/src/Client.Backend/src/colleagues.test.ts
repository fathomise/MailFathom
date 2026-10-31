// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { parseColleague, promoteColleague, readColleague, readColleaguePhoto, readColleagues } from './colleagues';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const member = {
    id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
    book: 'Member',
    displayName: 'Piotr Zieliński',
    addresses: ['p.zielinski@nordwind.example'],
    preferredAddress: 'p.zielinski@nordwind.example',
    phoneNumbers: [{ number: '+48 600 100 200', label: 'Mobile' }],
    jobTitle: 'Controller',
    organizationName: 'Nordwind',
    timeZone: 'Europe/Warsaw',
    workingHours: null,
    photo: 'revision-1',
};

type Answer = Omit<ClientResponse, 'headers'>;

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

function answering(response: Answer): MailFathomTransport {
    return recording(response).transport;
}

describe('readColleagues', () => {
    it('asks the directory for the window it will render, narrowed by what was typed', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ contacts: [], nextCursor: null }),
        });

        await readColleagues(session, transport, { search: ' piotr ', pageSize: 5 });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts/colleagues?pageSize=5&search=piotr',
        );
    });

    it('reads the members a well-formed page describes, and where the walk continues', async () => {
        const body = JSON.stringify({ contacts: [member], nextCursor: 'page-two' });

        const result = await readColleagues(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'read', value: { colleagues: [member], nextCursor: 'page-two' } });
    });

    it('reads the empty directory a reader in no organization is answered as one with nobody in it', async () => {
        const body = JSON.stringify({ contacts: [], nextCursor: null });

        const result = await readColleagues(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'read', value: { colleagues: [], nextCursor: null } });
    });

    it('refuses a page carrying more members than it asked for', async () => {
        const body = JSON.stringify({ contacts: [member, member], nextCursor: null });

        const result = await readColleagues(session, answering({ status: 200, body }), { pageSize: 1 });

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a refused credential as one to sign in again with', async () => {
        const result = await readColleagues(session, answering({ status: 401, body: '' }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unauthenticated', status: 401 } });
    });
});

describe('readColleague', () => {
    it('asks for the member the user identity names', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(member) });

        await readColleague(session, transport, member.id);

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts/colleagues/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90',
        );
    });

    it('reads a member who left the organization as somebody to let go of', async () => {
        const result = await readColleague(session, answering({ status: 404, body: '' }), member.id);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });
});

describe('promoteColleague', () => {
    it("asks the member's promotion route for a copy, stating no record", async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ outcome: 'Written', contact: null, addressHolder: null }),
        });

        const result = await promoteColleague(session, transport, member.id);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts/colleagues/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/promotion',
        );
        expect(result).toEqual({ outcome: 'read', value: { outcome: 'Written', contact: null, addressHolder: null } });
    });
});

describe('readColleaguePhoto', () => {
    it("composes the read of the member's portrait for the adapter to deliver", async () => {
        const delivered: ClientRequest[] = [];

        await readColleaguePhoto(
            session,
            member.id,
            (request) => {
                delivered.push(request);

                return Promise.resolve('none');
            },
            () => null,
        );

        expect(delivered[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts/colleagues/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/photo',
        );
        expect(delivered[0]?.headers['Accept']).toBe('image/jpeg, image/png');
    });
});

describe('parseColleague', () => {
    it('reads a member who holds no account as one with no address to write to', () => {
        const unaddressed = { ...member, addresses: [], preferredAddress: null };

        expect(parseColleague(unaddressed)).toEqual(unaddressed);
    });

    it('reads a member who states no title, zone, or portrait as one carrying none of them', () => {
        const bare = { ...member, jobTitle: null, timeZone: null, photo: null };

        expect(parseColleague(bare)).toEqual(bare);
    });

    it('refuses a member whose preference is not one of their addresses, or who has addresses and no preference', () => {
        expect(parseColleague({ ...member, preferredAddress: 'someone@else.example' })).toBeNull();
        expect(parseColleague({ ...member, preferredAddress: null })).toBeNull();
        expect(parseColleague({ ...member, addresses: [], preferredAddress: member.preferredAddress })).toBeNull();
    });

    it('reads working hours as the days Monday first and the range as minutes after midnight', () => {
        const hours = { days: ['Friday', 'Monday'], start: '08:00', end: '16:30' };

        expect(parseColleague({ ...member, workingHours: hours })?.workingHours).toEqual({
            days: ['Monday', 'Friday'],
            start: 8 * 60,
            end: 16 * 60 + 30,
        });
    });

    it('reads a member the service states no working hours for as one with none', () => {
        expect(parseColleague({ ...member, workingHours: undefined })?.workingHours).toBeNull();
    });

    it.each([
        ['no day', { days: [], start: '08:00', end: '16:00' }],
        ['a day the service never names', { days: ['Mon'], start: '08:00', end: '16:00' }],
        ['a time not written HH:mm', { days: ['Monday'], start: '8:00', end: '16:00' }],
        ['a time past the clock', { days: ['Monday'], start: '08:00', end: '24:00' }],
        ['an end not after the start', { days: ['Monday'], start: '16:00', end: '08:00' }],
        [
            'more days than a week has',
            { days: Array.from({ length: 8 }, () => 'Monday'), start: '08:00', end: '16:00' },
        ],
    ])('refuses a member whose working hours carry %s', (_, workingHours) => {
        expect(parseColleague({ ...member, workingHours })).toBeNull();
    });

    it('refuses a record that is not a member of the directory', () => {
        expect(parseColleague({ ...member, book: 'Own' })).toBeNull();
    });

    it('refuses a title longer than the service lets an administrator write', () => {
        expect(parseColleague({ ...member, jobTitle: 'x'.repeat(256) })).not.toBeNull();
        expect(parseColleague({ ...member, jobTitle: 'x'.repeat(257) })).toBeNull();
    });

    it('refuses a field of the wrong shape', () => {
        expect(parseColleague({ ...member, timeZone: 7 })).toBeNull();
        expect(parseColleague({ ...member, organizationName: null })).toBeNull();
        expect(parseColleague({ ...member, phoneNumbers: [{ number: '1', label: 'Fax' }] })).toBeNull();
        expect(
            parseColleague({
                ...member,
                addresses: [
                    member.preferredAddress,
                    ...Array.from({ length: 64 }, (_, at) => `a${String(at)}@b.example`),
                ],
            }),
        ).toBeNull();
    });
});
