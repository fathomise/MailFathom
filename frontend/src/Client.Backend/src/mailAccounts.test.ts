// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { readMailAccounts } from './mailAccounts';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const directoryBody = JSON.stringify({
    synchronizationEnabled: true,
    accounts: [
        {
            id: 'work',
            displayName: 'Work',
            synchronizationState: 'Synchronized',
            lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
            behind: false,
            filesJunk: false,
            marksExternalMail: false,
            shared: true,
            synchronizationStateSince: null,
            behindSince: null,
            waitingChangeCount: 0,
            oldestWaitingChangeCommittedAt: null,
        },
    ],
});

// The transport is the network boundary and the whole of what a test here fakes: it is a function the caller supplies,
// so nothing has to intercept a global or stand up a server to decide what came back.
// This operation reads no header off an answer, so the tests below name none and each helper supplies the empty set.
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

describe('readMailAccounts', () => {
    it('asks for the accounts route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: directoryBody });

        await readMailAccounts(session, transport);

        expect(requests).toEqual([
            {
                method: 'GET',
                path: 'https://mail.example.invalid/api/client/accounts',
                headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
            },
        ]);
    });

    it('reads the directory a well-formed answer describes', async () => {
        const result = await readMailAccounts(session, answering({ status: 200, body: directoryBody }));

        expect(result).toEqual({
            outcome: 'read',
            value: {
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Synchronized',
                        lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
                        behind: false,
                        filesJunk: false,
                        marksExternalMail: false,
                        shared: true,
                        synchronizationStateSince: null,
                        behindSince: null,
                        waitingChangeCount: 0,
                        oldestWaitingChangeCommittedAt: null,
                    },
                ],
            },
        });
    });

    it('reads an account whose organization marks external mail as one that says so', async () => {
        const marking = directoryBody.replace('"marksExternalMail":false', '"marksExternalMail":true');

        const result = await readMailAccounts(session, answering({ status: 200, body: marking }));

        expect(result).toMatchObject({ outcome: 'read', value: { accounts: [{ marksExternalMail: true }] } });
    });

    it('reads an account that has never synchronized as one with no time on it', async () => {
        const body = JSON.stringify({
            synchronizationEnabled: false,
            accounts: [
                {
                    id: 'personal',
                    displayName: 'Personal',
                    synchronizationState: 'NeverSynchronized',
                    behind: false,
                    filesJunk: false,
                    marksExternalMail: false,
                    shared: false,
                    synchronizationStateSince: null,
                    behindSince: null,
                    waitingChangeCount: 0,
                    oldestWaitingChangeCommittedAt: null,
                },
            ],
        });

        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result).toEqual({
            outcome: 'read',
            value: {
                synchronizationEnabled: false,
                accounts: [
                    {
                        id: 'personal',
                        displayName: 'Personal',
                        synchronizationState: 'NeverSynchronized',
                        lastSynchronizedAt: null,
                        behind: false,
                        filesJunk: false,
                        marksExternalMail: false,
                        shared: false,
                        synchronizationStateSince: null,
                        behindSince: null,
                        waitingChangeCount: 0,
                        oldestWaitingChangeCommittedAt: null,
                    },
                ],
            },
        });
    });

    it('reads since when an account has been unreachable and how many of its changes wait, as the deployment published them', async () => {
        const body = JSON.stringify({
            synchronizationEnabled: true,
            accounts: [
                {
                    id: 'board',
                    displayName: 'Board',
                    synchronizationState: 'Unreachable',
                    behind: true,
                    filesJunk: false,
                    marksExternalMail: false,
                    shared: false,
                    synchronizationStateSince: '2026-08-14T21:20:00+00:00',
                    behindSince: '2026-08-14T21:05:00+00:00',
                    waitingChangeCount: 3,
                    oldestWaitingChangeCommittedAt: '2026-08-15T07:31:00+00:00',
                },
            ],
        });

        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result.outcome === 'read' ? result.value.accounts[0] : null).toMatchObject({
            synchronizationStateSince: '2026-08-14T21:20:00+00:00',
            behindSince: '2026-08-14T21:05:00+00:00',
            waitingChangeCount: 3,
            oldestWaitingChangeCommittedAt: '2026-08-15T07:31:00+00:00',
        });
    });

    // The four are required by the contract, so one that is absent is refused rather than read as nothing waiting: an
    // account read as having no waiting changes would turn the freshness line green over changes that are waiting.
    // `undefined` is how a case leaves a field out, since `JSON.stringify` writes no key for it.
    it.each([
        ['a waiting count that is negative', { waitingChangeCount: -1 }],
        ['a waiting count that is not a whole number', { waitingChangeCount: 1.5 }],
        ['an oldest waiting change that is not an instant', { oldestWaitingChangeCommittedAt: 'yesterday' }],
        ['a state start that is not a string', { synchronizationStateSince: 12 }],
        ['no state start', { synchronizationStateSince: undefined }],
        ['no instant it fell behind', { behindSince: undefined }],
        ['no waiting count', { waitingChangeCount: undefined }],
        ['no oldest waiting change', { oldestWaitingChangeCommittedAt: undefined }],
    ])('refuses an account carrying %s', async (_, field) => {
        const body = JSON.stringify({
            synchronizationEnabled: true,
            accounts: [
                {
                    id: 'work',
                    displayName: 'Work',
                    synchronizationState: 'Synchronized',
                    behind: false,
                    filesJunk: false,
                    marksExternalMail: false,
                    shared: false,
                    synchronizationStateSince: null,
                    behindSince: null,
                    waitingChangeCount: 0,
                    oldestWaitingChangeCommittedAt: null,
                    ...field,
                },
            ],
        });

        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ])('answers a %i with the failure it stands for rather than throwing', async (status, reason) => {
        const result = await readMailAccounts(session, answering({ status, body: '' }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a connection that never answered as one to try again, rather than throwing at the screen', async () => {
        const result = await readMailAccounts(session, () => Promise.reject(new TypeError('Failed to fetch')));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    // Every shape below is an answer the service could not have meant, and each is refused rather than read as a
    // directory with a hole in it: a screen that renders a missing display name as `undefined` is a defect reaching a
    // person, and `unreadable` is the reason that says the body was the problem rather than the deployment.
    it.each([
        ['a body that is not JSON', 'not json at all'],
        ['a body that is not an object', JSON.stringify([])],
        ['a directory with no synchronization switch', JSON.stringify({ accounts: [] })],
        ['a directory whose accounts are not an array', JSON.stringify({ synchronizationEnabled: true, accounts: {} })],
        [
            'an account with no display name',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [{ id: 'work', synchronizationState: 'Synchronized', behind: false, filesJunk: false }],
            }),
        ],
        [
            'an account whose behind flag is not a boolean',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Synchronized',
                        behind: 'maybe',
                        filesJunk: false,
                        marksExternalMail: false,
                        shared: false,
                        synchronizationStateSince: null,
                        behindSince: null,
                        waitingChangeCount: 0,
                        oldestWaitingChangeCommittedAt: null,
                    },
                ],
            }),
        ],
        [
            'an account that does not say whether it files spam',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [{ id: 'work', displayName: 'Work', synchronizationState: 'Synchronized', behind: false }],
            }),
        ],
        [
            'an account whose spam switch is not a boolean',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Synchronized',
                        behind: false,
                        filesJunk: 'yes',
                        shared: false,
                        synchronizationStateSince: null,
                        behindSince: null,
                        waitingChangeCount: 0,
                        oldestWaitingChangeCommittedAt: null,
                    },
                ],
            }),
        ],
        [
            'an account that does not say whether it is shared',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Synchronized',
                        behind: false,
                        filesJunk: false,
                        marksExternalMail: false,
                    },
                ],
            }),
        ],
        [
            'an account in a synchronization state this client has no name for',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Rebuilding',
                        behind: false,
                        filesJunk: false,
                        marksExternalMail: false,
                        shared: false,
                        synchronizationStateSince: null,
                        behindSince: null,
                        waitingChangeCount: 0,
                        oldestWaitingChangeCommittedAt: null,
                    },
                ],
            }),
        ],
        [
            'an account whose last synchronization is not a time',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Synchronized',
                        lastSynchronizedAt: 1756633260,
                        behind: false,
                        filesJunk: false,
                        marksExternalMail: false,
                        shared: false,
                        synchronizationStateSince: null,
                        behindSince: null,
                        waitingChangeCount: 0,
                        oldestWaitingChangeCommittedAt: null,
                    },
                ],
            }),
        ],
        [
            'an account that does not say whether its organization marks external mail',
            JSON.stringify({
                synchronizationEnabled: true,
                accounts: [
                    {
                        id: 'work',
                        displayName: 'Work',
                        synchronizationState: 'Synchronized',
                        lastSynchronizedAt: null,
                        behind: false,
                        filesJunk: false,
                        shared: false,
                        synchronizationStateSince: null,
                        behindSince: null,
                        waitingChangeCount: 0,
                        oldestWaitingChangeCommittedAt: null,
                    },
                ],
            }),
        ],
    ])('refuses %s as unreadable', async (_, body) => {
        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a directory larger than the ceiling before it walks it', async () => {
        const body = JSON.stringify({
            synchronizationEnabled: true,
            accounts: Array.from({ length: 257 }, (_, index) => ({
                id: `account-${String(index)}`,
                displayName: `Account ${String(index)}`,
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: null,
                behind: false,
                filesJunk: false,
                marksExternalMail: false,
                shared: false,
                synchronizationStateSince: null,
                behindSince: null,
                waitingChangeCount: 0,
                oldestWaitingChangeCommittedAt: null,
            })),
        });

        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a directory at the ceiling, so the bound refuses more than it rather than the maximum', async () => {
        const body = JSON.stringify({
            synchronizationEnabled: true,
            accounts: Array.from({ length: 256 }, (_, index) => ({
                id: `account-${String(index)}`,
                displayName: `Account ${String(index)}`,
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: null,
                behind: false,
                filesJunk: false,
                marksExternalMail: false,
                shared: false,
                synchronizationStateSince: null,
                behindSince: null,
                waitingChangeCount: 0,
                oldestWaitingChangeCommittedAt: null,
            })),
        });

        const result = await readMailAccounts(session, answering({ status: 200, body }));

        expect(result.outcome).toBe('read');
    });
});
