// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { conversationPageSize, readMailConversations, type MailTimelineQuery } from './mailTimeline';
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
    categories: [],
    title: {
        topic: 'The 0.8.0 release',
        standing: 'Waiting on the notes review.',
        derivedFromMessageCount: 4,
        derivedFromLatestArrival: '2026-08-15T11:20:00+00:00',
    },
};

const page = { conversations: [thread], nextCursor: 'AbCd', previousCursor: null, pageSize: conversationPageSize };

const leadingPage: MailTimelineQuery = {
    account: null,
    folder: null,
    includeJunk: false,
    unread: null,
    flagged: null,
    hasAttachments: null,
    receivedOnOrAfter: null,
    receivedBefore: null,
    carriesMark: null,
    markDueOnOrAfter: null,
    markDueBefore: null,
    order: 'newestFirst',
    direction: 'forward',
    pageSize: conversationPageSize,
    cursor: null,
};

function reading(transport: MailFathomTransport, query: Partial<MailTimelineQuery> = {}) {
    return readMailConversations(session, transport, { ...leadingPage, ...query });
}

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

describe('readMailConversations', () => {
    it('asks for the mail list grouped by conversation, under the same query string a page read by message names', async () => {
        const { transport, requests } = recording(200, page);

        await reading(transport);

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/emails?sort=receivedAt&order=newestFirst&direction=forward&pageSize=25&grouping=conversation',
        );
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
    });

    it('narrows the grouped list by the account, the filters, and the cursor it continues from', async () => {
        const { transport, requests } = recording(200, page);

        await reading(transport, { account: 'work & home', unread: true, carriesMark: 'Commitment', cursor: 'Ab/Cd' });

        expect(requests[0]?.path).toBe(
            'https://mail.example.invalid/api/client/emails?sort=receivedAt&order=newestFirst&direction=forward&pageSize=25&account=work%20%26%20home&carriesMark=Commitment&unread=true&cursor=Ab%2FCd&grouping=conversation',
        );
    });

    it('reads every conversation on the page as the deployment described it', async () => {
        const answer = await reading(recording(200, page).transport);

        expect(answer).toStrictEqual({ outcome: 'read', value: page });
    });

    it('reads a conversation with no title and nobody else writing in it', async () => {
        const untitled = { ...thread, newestReceivedSender: null, title: null };

        const answer = await reading(
            recording(200, { ...page, conversations: [untitled], nextCursor: null }).transport,
        );

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: {
                conversations: [untitled],
                nextCursor: null,
                previousCursor: null,
                pageSize: conversationPageSize,
            },
        });
    });

    it('reads every category the conversation holds', async () => {
        const categories = [{ identity: 'Legal', name: 'Legal' }];

        const answer = await reading(recording(200, { ...page, conversations: [{ ...thread, categories }] }).transport);

        expect(answer.outcome === 'read' ? answer.value.conversations[0]?.categories : null).toStrictEqual(categories);
    });

    it('reads a title whose standing is empty, where nothing is left open', async () => {
        const settled = { ...thread, title: { ...thread.title, standing: '' } };

        const answer = await reading(recording(200, { ...page, conversations: [settled] }).transport);

        expect(answer.outcome === 'read' ? answer.value.conversations[0]?.title?.standing : null).toBe('');
    });

    it.each([
        ['a row with no newest message', { ...thread, newest: null }],
        ['a title with no topic', { ...thread, title: { ...thread.title, topic: '' } }],
        ['a participant with no address', { ...thread, participants: [{ displayName: 'Nobody', reader: false }] }],
        ['a negative count', { ...thread, unreadCount: -1 }],
        ['sender facts that are not sender facts', { ...thread, newestReceivedSender: 'Authenticated' }],
        [
            'more participants than a row names',
            {
                ...thread,
                participants: Array.from({ length: 17 }, (_, at) => ({
                    address: `person-${String(at)}@example.invalid`,
                    displayName: null,
                    reader: false,
                })),
            },
        ],
        ['an attachment mark that is not a yes or a no', { ...thread, hasAttachments: 'yes' }],
        ['a say on unnamed participants that is not a yes or a no', { ...thread, moreParticipantsNotNamed: 1 }],
        ['a row saying nothing about its categories', { ...thread, categories: undefined }],
    ])('refuses %s', async (_, row) => {
        const answer = await reading(recording(200, { ...page, conversations: [row] }).transport);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a page holding more conversations than it asked for', async () => {
        const answer = await reading(
            recording(200, { ...page, conversations: Array.from({ length: conversationPageSize + 1 }, () => thread) })
                .transport,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        ['an empty cursor', { ...page, nextCursor: '' }],
        ['a cursor longer than any the deployment issues', { ...page, nextCursor: 'c'.repeat(4097) }],
        ['a page that is not an object', [page]],
        ['the rows of the list read by message instead', { ...page, conversations: undefined, emails: [] }],
    ])('refuses a page carrying %s', async (_, body) => {
        const answer = await reading(recording(200, body).transport);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer that is not JSON', async () => {
        const answer = await reading(() => Promise.resolve({ status: 200, body: '<html>', headers: {} }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it.each([
        [401, 'unauthenticated'],
        [403, 'unauthorized'],
        [500, 'unavailable'],
    ] as const)('reports a %i by the reason it names', async (status, reason) => {
        const answer = await reading(recording(status, {}).transport);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment that did not answer as unavailable', async () => {
        const answer = await reading(() => Promise.reject(new TypeError('Failed to fetch')));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
