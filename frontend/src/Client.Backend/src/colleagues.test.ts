// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    mostColleaguesInStructure,
    parseColleague,
    promoteColleague,
    readColleague,
    readColleagueAt,
    readColleaguePhoto,
    readColleagues,
    readColleagueStructure,
} from './colleagues';
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
    presence: 'Busy',
    lastSeen: null,
    outOfOfficeUntil: null,
    usesPhoneApp: false,
    statusMessage: null,
    workMode: null,
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

describe('readColleagueAt', () => {
    it('asks for the member the address belongs to, with the address carried as a query value', async () => {
        const { transport, requests } = recording({ status: 200, body: JSON.stringify(member) });

        const result = await readColleagueAt(session, transport, 'p.zielinski+team@nordwind.example');

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts/colleagues/by-address?address=p.zielinski%2Bteam%40nordwind.example',
        );
        expect(result.outcome).toBe('read');
    });

    it('reads an address no member holds as nobody to warn about', async () => {
        const result = await readColleagueAt(session, answering({ status: 404, body: '' }), 'someone@contoso.example');

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });
});

describe('readColleagueStructure', () => {
    const manager = { ...member, id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a91', displayName: 'Zofia Krawczyk' };
    const report = { ...member, id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a92', displayName: 'Hanna Mazur' };

    it("asks for the member's structure the user identity names", async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ managers: [], directReports: [], readerId: report.id }),
        });

        await readColleagueStructure(session, transport, member.id);

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/contacts/colleagues/0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90/structure',
        );
    });

    it('reads the managers above the member and the members reporting to them', async () => {
        const body = JSON.stringify({ managers: [manager], directReports: [report], readerId: report.id });

        const result = await readColleagueStructure(session, answering({ status: 200, body }), member.id);

        expect(result).toEqual({
            outcome: 'read',
            value: { managers: [manager], directReports: [report], readerId: report.id },
        });
    });

    it('refuses a structure naming no reader, or holding a row that is not a member', async () => {
        const nameless = JSON.stringify({ managers: [], directReports: [] });
        const foreign = JSON.stringify({
            managers: [{ ...manager, book: 'Own' }],
            directReports: [],
            readerId: report.id,
        });

        const unnamed = await readColleagueStructure(session, answering({ status: 200, body: nameless }), member.id);
        const mixed = await readColleagueStructure(session, answering({ status: 200, body: foreign }), member.id);

        expect([unnamed, mixed]).toEqual([
            { outcome: 'failed', failure: { reason: 'unreadable', status: 200 } },
            { outcome: 'failed', failure: { reason: 'unreadable', status: 200 } },
        ]);
    });

    it('refuses a structure holding more managers or more direct reports than a directory can', async () => {
        const tooMany = Array.from({ length: mostColleaguesInStructure + 1 }, () => report);
        const managersPastTheBound = JSON.stringify({ managers: tooMany, directReports: [], readerId: report.id });
        const reportsPastTheBound = JSON.stringify({ managers: [], directReports: tooMany, readerId: report.id });

        const managers = await readColleagueStructure(
            session,
            answering({ status: 200, body: managersPastTheBound }),
            member.id,
        );
        const reports = await readColleagueStructure(
            session,
            answering({ status: 200, body: reportsPastTheBound }),
            member.id,
        );

        expect([managers, reports]).toEqual([
            { outcome: 'failed', failure: { reason: 'unreadable', status: 200 } },
            { outcome: 'failed', failure: { reason: 'unreadable', status: 200 } },
        ]);
    });

    it('reads a member who left the organization as somebody to let go of', async () => {
        const result = await readColleagueStructure(session, answering({ status: 404, body: '' }), member.id);

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

    it.each(['Active', 'Away', 'Busy', 'DoNotDisturb', 'Offline'])('reads a member who reads as %s', (presence) => {
        expect(parseColleague({ ...member, presence })?.presence).toBe(presence);
    });

    it('reads when an offline member was last seen', () => {
        const away = { ...member, presence: 'Offline', lastSeen: '2026-10-30T18:20:00+00:00' };

        expect(parseColleague(away)).toEqual(away);
    });

    it('reads a member with no last-seen time stated as one whose last sighting is not known', () => {
        const unstated = Object.fromEntries(Object.entries(member).filter(([field]) => field !== 'lastSeen'));

        expect(parseColleague(unstated)?.lastSeen).toBeNull();
    });

    it('refuses a member drawn as hidden, which no colleague ever reads as, and one with no presence at all', () => {
        expect(parseColleague({ ...member, presence: 'Hidden' })).toBeNull();
        expect(parseColleague({ ...member, presence: 'busy' })).toBeNull();
        expect(parseColleague({ ...member, presence: undefined })).toBeNull();
    });

    it('refuses a last-seen time on a member who is present, and one that is not an instant', () => {
        expect(parseColleague({ ...member, presence: 'Away', lastSeen: '2026-10-30T18:20:00+00:00' })).toBeNull();
        expect(parseColleague({ ...member, presence: 'Offline', lastSeen: 'yesterday' })).toBeNull();
    });

    it('reads until when a member is out of office, and one with none stated as somebody who is not away', () => {
        const away = { ...member, outOfOfficeUntil: '2026-11-04T00:00:00+01:00' };
        const unstated = Object.fromEntries(Object.entries(member).filter(([field]) => field !== 'outOfOfficeUntil'));

        expect(parseColleague(away)).toEqual(away);
        expect(parseColleague(unstated)?.outOfOfficeUntil).toBeNull();
    });

    it('reads what a member says they are doing and where they work from, stated or not', () => {
        const said = {
            ...member,
            statusMessage: { text: 'Ask me about the UAT environment', clears: null, clearsAt: null },
            workMode: { mode: 'Office', endsAt: '2026-10-31T23:00:00Z' },
        };
        const unstated = Object.fromEntries(
            Object.entries(member).filter(([field]) => field !== 'statusMessage' && field !== 'workMode'),
        );

        expect(parseColleague(said)).toEqual(said);
        expect(parseColleague(unstated)).toMatchObject({ statusMessage: null, workMode: null });
    });

    it('refuses a status message or a place of work of the wrong shape', () => {
        expect(parseColleague({ ...member, statusMessage: 'On call' })).toBeNull();
        expect(parseColleague({ ...member, workMode: { mode: 'Home', endsAt: null } })).toBeNull();
    });

    it('refuses an out-of-office end that is not an instant', () => {
        expect(parseColleague({ ...member, outOfOfficeUntil: 'next week' })).toBeNull();
    });

    it('reads whether a member uses the phone app, and refuses an answer of the wrong shape', () => {
        expect(parseColleague({ ...member, usesPhoneApp: true })?.usesPhoneApp).toBe(true);
        expect(parseColleague({ ...member, usesPhoneApp: 'yes' })).toBeNull();
    });

    it('reads a member an answer says nothing about the phone app for as not using it', () => {
        const unstated = Object.fromEntries(Object.entries(member).filter(([field]) => field !== 'usesPhoneApp'));

        expect(parseColleague(unstated)?.usesPhoneApp).toBe(false);
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
