// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { readMailThreadList, threadListPageSize } from './mailThreadList';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const sender = {
    authorAuthentication: 'Authenticated',
    deploymentTrust: 'Unknown',
    signatureOutcome: 'NotSigned',
    signerMatchesAuthor: false,
};

const thread = {
    threadId: '9b2a1c74-4a4e-4c93-9a2e-3f6f0a1b2c3d',
    account: 'work',
    messageCount: 4,
    unreadCount: 1,
    hasAttachments: true,
    participants: [
        { address: 'releases@example.invalid', displayName: 'Example Releases', reader: false },
        { address: 'user@example.invalid', displayName: null, reader: true },
    ],
    moreParticipantsNotNamed: false,
    newest: {
        id: '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60',
        subject: 'Re: Release 0.8.0 is out',
        receivedAt: '2026-08-15T11:20:00+00:00',
        senderAddress: 'user@example.invalid',
        senderDisplayName: null,
        preview: 'Thanks, the notes read well.',
        own: true,
        senderPhoto: null,
    },
    newestReceivedSender: sender,
    title: {
        topic: 'The 0.8.0 release',
        standing: 'Waiting on the notes review.',
        derivedFromMessageCount: 4,
        derivedFromLatestArrival: '2026-08-15T11:20:00+00:00',
    },
};

const page = { threads: [thread], nextCursor: 'AbCd', pageSize: threadListPageSize };

// The transport is the network boundary and the whole of what a test here fakes.
function recording(status: number, body: unknown): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            const response: ClientResponse = { status, body: JSON.stringify(body), headers: {} };

            return Promise.resolve(response);
        },
    };
}

describe('readMailThreadList', () => {
    it('asks for one page of every account’s conversations where no account is named', async () => {
        const { transport, requests } = recording(200, page);

        await readMailThreadList(session, transport, null, null);

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/threads?pageSize=25');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('names the account and the cursor it continues from', async () => {
        const { transport, requests } = recording(200, page);

        await readMailThreadList(session, transport, 'work & home', 'Ab/Cd');

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/threads?pageSize=25&account=work%20%26%20home&cursor=Ab%2FCd',
        );
    });

    it('reads every conversation on the page as the deployment described it', async () => {
        const answer = await readMailThreadList(session, recording(200, page).transport, null, null);

        expect(answer).toStrictEqual({ outcome: 'read', value: page });
    });

    it('reads a conversation with no title and nobody else writing in it', async () => {
        const untitled = { ...thread, newestReceivedSender: null, title: null };

        const answer = await readMailThreadList(
            session,
            recording(200, { ...page, threads: [untitled], nextCursor: null }).transport,
            null,
            null,
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: { threads: [untitled], nextCursor: null, pageSize: threadListPageSize },
        });
    });

    it('reads a title whose standing is empty, where nothing is left open', async () => {
        const settled = { ...thread, title: { ...thread.title, standing: '' } };

        const answer = await readMailThreadList(
            session,
            recording(200, { ...page, threads: [settled] }).transport,
            null,
            null,
        );

        expect(answer.outcome === 'read' ? answer.value.threads[0]?.title?.standing : null).toBe('');
    });

    it.each([
        ['a row with no newest message', { ...thread, newest: null }],
        ['a title with no topic', { ...thread, title: { ...thread.title, topic: '' } }],
        ['a participant with no address', { ...thread, participants: [{ displayName: 'Nobody', reader: false }] }],
        ['a negative count', { ...thread, unreadCount: -1 }],
        ['sender facts that are not sender facts', { ...thread, newestReceivedSender: 'Authenticated' }],
    ])('refuses %s', async (_, row) => {
        const answer = await readMailThreadList(
            session,
            recording(200, { ...page, threads: [row] }).transport,
            null,
            null,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a page holding more conversations than it asked for', async () => {
        const answer = await readMailThreadList(
            session,
            recording(200, { ...page, threads: Array.from({ length: threadListPageSize + 1 }, () => thread) })
                .transport,
            null,
            null,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a refusal by the reason its status names', async () => {
        const answer = await readMailThreadList(session, recording(403, {}).transport, null, null);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('reports a deployment that did not answer as unavailable', async () => {
        const answer = await readMailThreadList(
            session,
            () => Promise.reject(new TypeError('Failed to fetch')),
            null,
            null,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
