// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { longestMailFootersAnswer, readMailFooters } from './mailFooters';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const footerDocument = {
    schemaVersion: 1,
    blocks: [
        {
            type: 'paragraph',
            version: 1,
            content: [{ text: 'Ada Lovelace', emphasis: 'Bold', foreground: null, link: null }],
            alignment: 'Inherited',
        },
    ],
    refusal: 'None',
    removedRemoteReferenceCount: 0,
    retainedRemoteImageCount: 0,
    inlineImageCount: 0,
    undrawnInlineImageCount: 0,
    truncated: false,
};

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function footersAnswering(footers: readonly unknown[]): MailFathomTransport {
    return answering({ status: 200, body: JSON.stringify({ footers }) });
}

describe('readMailFooters', () => {
    it('asks for the footers route on the client surface, bounded as a footer at its largest is', async () => {
        const requests: ClientRequest[] = [];

        await readMailFooters(session, (request) => {
            requests.push(request);

            return Promise.resolve({ status: 200, body: '{"footers":[]}', headers: {} });
        });

        expect(requests).toHaveLength(1);
        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/mail-footers');
        expect(requests[0]?.headers['Authorization']).toBe('Basic dGVzdA==');
        expect(requests[0]?.longestAnswer).toBe(longestMailFootersAnswer);
    });

    it('reads each account’s footer as the document it is drawn from, and whose it is', async () => {
        const answer = await readMailFooters(
            session,
            footersAnswering([
                { account: 'work', source: 'organization', document: footerDocument },
                { account: 'personal', source: 'own', document: null },
            ]),
        );

        expect(answer.outcome).toBe('read');
        if (answer.outcome !== 'read') {
            return;
        }

        expect(answer.value.map(({ account, source }) => ({ account, source }))).toStrictEqual([
            { account: 'work', source: 'organization' },
            { account: 'personal', source: 'own' },
        ]);
        expect(answer.value[0]?.document?.blocks).toHaveLength(1);
        expect(answer.value[1]?.document).toBeNull();
    });

    it.each([
        ['a body that is not an answer at all', 'not json'],
        ['an answer holding no list of footers', JSON.stringify({})],
        ['a footer naming no account', JSON.stringify({ footers: [{ source: 'own', document: null }] })],
        [
            'a footer whose source is not one the surface publishes',
            JSON.stringify({ footers: [{ account: 'work', source: 'shared', document: null }] }),
        ],
        [
            'a footer whose document is not one this client reads',
            JSON.stringify({ footers: [{ account: 'work', source: 'own', document: { schemaVersion: 2 } }] }),
        ],
    ])('refuses %s', async (_, body) => {
        const answer = await readMailFooters(session, answering({ status: 200, body }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses more footers than a person holds accounts', async () => {
        const footers = Array.from({ length: 257 }, (_, at) => ({ account: `a${String(at)}`, source: 'own' }));

        const answer = await readMailFooters(session, footersAnswering(footers));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reports a credential that may not read the profile as the refusal it is', async () => {
        const answer = await readMailFooters(session, answering({ status: 403, body: '' }));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('reports a deployment it could not reach at all', async () => {
        const answer = await readMailFooters(session, () => Promise.reject(new Error('the connection was refused')));

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
