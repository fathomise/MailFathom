// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { draftPassageRewritingRoute, longestRewriteInstruction, rewriteDraftPassages } from './draftPassageRewriting';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

const asked = {
    text: 'Thanks for the offer. We accept the price.\nSee you on Tuesday.',
    passages: [
        { start: 22, length: 20 },
        { start: 43, length: 19 },
    ],
    instruction: 'Make it more formal.',
};

describe('rewriteDraftPassages', () => {
    it('posts the text, the parts to rewrite and how, and reads each part back in order', async () => {
        const requests: ClientRequest[] = [];
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            return Promise.resolve({
                status: 200,
                headers: {},
                body: JSON.stringify({
                    rewritten: true,
                    passages: ['We are pleased to accept the price.', 'We look forward to Tuesday.'],
                }),
            });
        };

        const result = await rewriteDraftPassages(session, transport, asked);

        expect(result).toStrictEqual({
            outcome: 'read',
            value: {
                outcome: 'rewritten',
                passages: ['We are pleased to accept the price.', 'We look forward to Tuesday.'],
            },
        });
        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client${draftPassageRewritingRoute}`);
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual(asked);
    });

    it('shortens an instruction to the bound the deployment reads rather than having it refused', async () => {
        const requests: ClientRequest[] = [];
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            return Promise.resolve({
                status: 200,
                headers: {},
                body: JSON.stringify({ rewritten: false, passages: [] }),
            });
        };

        await rewriteDraftPassages(session, transport, {
            ...asked,
            instruction: 'x'.repeat(longestRewriteInstruction + 9),
        });

        expect((JSON.parse(requests[0]?.body ?? '') as { instruction: string }).instruction).toHaveLength(
            longestRewriteInstruction,
        );
    });

    it('reads a deployment that rewrote nothing as the draft as it was', async () => {
        const result = await rewriteDraftPassages(
            session,
            answering({ status: 200, body: JSON.stringify({ rewritten: false, passages: [] }) }),
            asked,
        );

        expect(result).toStrictEqual({ outcome: 'read', value: { outcome: 'notRewritten' } });
    });

    it('tells a spent allowance apart from a deployment that rewrote nothing', async () => {
        const result = await rewriteDraftPassages(session, answering({ status: 429, body: '' }), asked);

        expect(result).toStrictEqual({ outcome: 'read', value: { outcome: 'allowanceSpent' } });
    });

    it('reports a grant that does not carry asking', async () => {
        const result = await rewriteDraftPassages(session, answering({ status: 403, body: '' }), asked);

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('refuses an answer carrying a different number of parts than were asked for', async () => {
        const result = await rewriteDraftPassages(
            session,
            answering({ status: 200, body: JSON.stringify({ rewritten: true, passages: ['Only one.'] }) }),
            asked,
        );

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a part that came back empty', async () => {
        const result = await rewriteDraftPassages(
            session,
            answering({ status: 200, body: JSON.stringify({ rewritten: true, passages: ['Fine.', '  '] }) }),
            asked,
        );

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses an answer that is not JSON at all', async () => {
        const result = await rewriteDraftPassages(session, answering({ status: 200, body: 'not json' }), asked);

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});
