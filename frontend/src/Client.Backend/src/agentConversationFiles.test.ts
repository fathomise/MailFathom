// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    agentConversationFileUploadRequest,
    readAgentConversationFiles,
    takeAgentConversationFile,
} from './agentConversationFiles';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const conversation = '0198f4a1-0000-7000-8000-00000000a9e1';
const route = `https://mail.example.invalid/api/client/agent/conversations/${conversation}/files`;

const offer = {
    fileId: '0198f4a1-0000-7000-8000-00000000f001',
    name: 'Offer v3.pdf',
    kind: 'document',
    mediaType: 'application/pdf',
    byteLength: 1_200_000,
    addedAt: '2026-11-12T10:00:00+00:00',
    text: { state: 'whole', reason: null, pageCount: 3, pageKind: 'page', characters: 5000 },
};

function answering(status: number, body: unknown = ''): MailFathomTransport {
    return () =>
        Promise.resolve({ status, body: typeof body === 'string' ? body : JSON.stringify(body), headers: {} });
}

function delivering(status: number, body: unknown = ''): {
    deliver: (request: ClientRequest) => Promise<ClientResponse | null>;
    requests: ClientRequest[];
} {
    const requests: ClientRequest[] = [];

    return {
        requests,
        deliver: (request) => {
            requests.push(request);

            return Promise.resolve({
                status,
                body: typeof body === 'string' ? body : JSON.stringify(body),
                headers: {},
            });
        },
    };
}

describe('agentConversationFileUploadRequest', () => {
    it('posts the octets into the conversation, naming the file in an encoded header the service reads', () => {
        const request = agentConversationFileUploadRequest(session, conversation, "Oferta – Łódź (v2)'s.pdf");

        expect(request).toMatchObject({
            method: 'POST',
            path: route,
            headers: {
                Authorization: 'Basic dGVzdA==',
                'Content-Type': 'application/octet-stream',
                'Content-Disposition':
                    "attachment; filename*=UTF-8''Oferta%20%E2%80%93%20%C5%81%C3%B3d%C5%BA%20%28v2%29%27s.pdf",
            },
        });
    });
});

describe('takeAgentConversationFile', () => {
    it('answers with the file the conversation now holds', async () => {
        const { deliver, requests } = delivering(201, offer);

        const taken = await takeAgentConversationFile(session, conversation, 'Offer v3.pdf', deliver);

        expect(taken).toEqual({
            outcome: 'read',
            value: {
                outcome: 'taken',
                file: {
                    fileId: offer.fileId,
                    name: 'Offer v3.pdf',
                    kind: 'document',
                    mediaType: 'application/pdf',
                    byteLength: 1_200_000,
                },
            },
        });
        expect(requests[0]?.path).toBe(route);
    });

    it.each([
        [400, 'empty'],
        [409, 'conversationFull'],
        [413, 'tooLarge'],
        [415, 'notRead'],
    ])('reads a %i as the service refusing the file as %s', async (status, refusal) => {
        const taken = await takeAgentConversationFile(session, conversation, 'x.pdf', delivering(status).deliver);

        expect(taken).toEqual({ outcome: 'read', value: { outcome: 'refused', refusal } });
    });

    it('reads a conversation that is somebody else’s as missing', async () => {
        const taken = await takeAgentConversationFile(session, conversation, 'x.pdf', delivering(404).deliver);

        expect(taken).toMatchObject({ outcome: 'failed', failure: { reason: 'missing' } });
    });

    it('reads nothing having answered as unavailable', async () => {
        const taken = await takeAgentConversationFile(session, conversation, 'x.pdf', () => Promise.resolve(null));

        expect(taken).toMatchObject({ outcome: 'failed', failure: { reason: 'unavailable' } });
    });

    it.each([
        ['a kind this build does not know', { ...offer, kind: 'archive' }],
        ['an inherited name for a kind', { ...offer, kind: 'constructor' }],
        ['a negative size', { ...offer, byteLength: -1 }],
        ['no identifier', { ...offer, fileId: '' }],
        ['a name that is not text', { ...offer, name: 7 }],
    ])('refuses an acceptance naming %s', async (_, body) => {
        const taken = await takeAgentConversationFile(session, conversation, 'x.pdf', delivering(201, body).deliver);

        expect(taken).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });
});

describe('readAgentConversationFiles', () => {
    it('lists the files the conversation holds, a file with no usable name as unnamed', async () => {
        const listed = await readAgentConversationFiles(
            session,
            answering(200, { files: [offer, { ...offer, fileId: 'file-2', name: null, kind: 'picture' }] }),
            conversation,
        );

        expect(listed).toMatchObject({
            outcome: 'read',
            value: [
                { fileId: offer.fileId, name: 'Offer v3.pdf', kind: 'document' },
                { fileId: 'file-2', name: null, kind: 'picture' },
            ],
        });
    });

    it('refuses a list longer than a conversation may hold', async () => {
        const files = Array.from({ length: 21 }, (_, at) => ({ ...offer, fileId: `file-${String(at)}` }));

        const listed = await readAgentConversationFiles(session, answering(200, { files }), conversation);

        expect(listed).toMatchObject({ outcome: 'failed', failure: { reason: 'unreadable' } });
    });

    it('reads a refused grant as the reason it failed', async () => {
        const listed = await readAgentConversationFiles(session, answering(403), conversation);

        expect(listed).toMatchObject({ outcome: 'failed', failure: { reason: 'unauthorized' } });
    });
});
