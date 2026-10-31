// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { choosePresence, closePresenceClient, readOwnPresence, renewPresenceClient } from './presence';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const client = '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90';

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

function answering(status: number, body: unknown = ''): MailFathomTransport {
    return recording({ status, body: typeof body === 'string' ? body : JSON.stringify(body) }).transport;
}

describe('readOwnPresence', () => {
    it('reads the state the member sees themselves in, with the choice in effect and when it ends', async () => {
        const presence = {
            state: 'Busy',
            chosenState: 'Busy',
            choiceDuration: '1h',
            choiceEndsAt: '2026-10-31T15:30:00+01:00',
        };

        expect(await readOwnPresence(session, answering(200, presence))).toEqual({ outcome: 'read', value: presence });
    });

    it('reads a member whose clients decide their presence as one who chose nothing', async () => {
        const presence = { state: 'Away', chosenState: null, choiceDuration: null, choiceEndsAt: null };

        expect(await readOwnPresence(session, answering(200, presence))).toEqual({ outcome: 'read', value: presence });
    });

    it('reads a member who chose to hide as hidden, which only they ever read', async () => {
        const answer = await readOwnPresence(session, answering(200, { state: 'Hidden', chosenState: 'Hidden' }));

        expect(answer).toEqual({
            outcome: 'read',
            value: { state: 'Hidden', chosenState: 'Hidden', choiceDuration: null, choiceEndsAt: null },
        });
    });

    it('reads a caller in no organization as one with no presence at all', async () => {
        expect(await readOwnPresence(session, answering(404))).toEqual({
            outcome: 'failed',
            failure: { reason: 'missing', status: 404 },
        });
    });

    it.each([
        ['a state this client does not know', { state: 'Lunch', chosenState: null, choiceEndsAt: null }],
        [
            'a while with nothing chosen',
            { state: 'Active', chosenState: null, choiceDuration: '1h', choiceEndsAt: '2026-10-31T15:30Z' },
        ],
        [
            'an end that is not an instant',
            { state: 'Busy', chosenState: 'Busy', choiceDuration: '1h', choiceEndsAt: 'soon' },
        ],
        [
            'a while this client does not know',
            { state: 'Busy', chosenState: 'Busy', choiceDuration: '3h', choiceEndsAt: '2026-10-31T15:30Z' },
        ],
        ['an end with no while', { state: 'Busy', chosenState: 'Busy', choiceEndsAt: '2026-10-31T15:30Z' }],
        ['a while with no end', { state: 'Busy', chosenState: 'Busy', choiceDuration: 'today', choiceEndsAt: null }],
    ])('refuses %s', async (_, body) => {
        expect(await readOwnPresence(session, answering(200, body))).toEqual({
            outcome: 'failed',
            failure: { reason: 'unreadable', status: 200 },
        });
    });
});

describe('choosePresence', () => {
    it('states the state and how long it lasts together, in one request', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({
                state: 'Busy',
                chosenState: 'Busy',
                choiceDuration: '1h',
                choiceEndsAt: '2026-10-31T16:30:00Z',
            }),
        });

        await choosePresence(session, transport, 'Busy', '1h');

        expect(requests[0]).toMatchObject({
            method: 'PUT',
            path: 'https://mail.example.invalid/api/client/presence/choice',
            body: JSON.stringify({ state: 'Busy', duration: '1h' }),
        });
    });

    it('states no duration for a state chosen until changed, and reads what the member then reads as', async () => {
        const { transport, requests } = recording({
            status: 200,
            body: JSON.stringify({ state: 'Active', chosenState: null, choiceDuration: null, choiceEndsAt: null }),
        });

        const answer = await choosePresence(session, transport, 'Active', null);

        expect(requests[0]?.body).toBe(JSON.stringify({ state: 'Active', duration: null }));
        expect(answer).toEqual({
            outcome: 'read',
            value: { state: 'Active', chosenState: null, choiceDuration: null, choiceEndsAt: null },
        });
    });
});

describe('renewPresenceClient', () => {
    it('renews the client under its own identifier, saying whether anybody is using it, and once', async () => {
        const { transport, requests } = recording({ status: 204, body: '' });

        const answer = await renewPresenceClient(session, transport, client, 'idle');

        expect(answer).toEqual({ outcome: 'read', value: null });
        expect(requests[0]).toMatchObject({
            method: 'PUT',
            path: `https://mail.example.invalid/api/client/presence/clients/${client}`,
            body: JSON.stringify({ activity: 'idle' }),
            retriedByCaller: true,
        });
    });

    it('reads a caller in no organization as the answer that stops the renewing', async () => {
        expect(await renewPresenceClient(session, answering(404), client, 'active')).toEqual({
            outcome: 'failed',
            failure: { reason: 'missing', status: 404 },
        });
    });

    it('reads a refusal of one client too many as a failure that does not stop it', async () => {
        expect(await renewPresenceClient(session, answering(409), client, 'active')).toEqual({
            outcome: 'failed',
            failure: { reason: 'unavailable', status: 409 },
        });
    });
});

describe('closePresenceClient', () => {
    it('says the client closed in a request the browser carries past the page', async () => {
        const { transport, requests } = recording({ status: 204, body: '' });

        await closePresenceClient(session, transport, client);

        expect(requests[0]).toMatchObject({
            method: 'DELETE',
            path: `https://mail.example.invalid/api/client/presence/clients/${client}`,
            outlivesThePage: true,
        });
        expect(requests[0]?.headers['Authorization']).toBe(session.authorization);
    });
});
