// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { longestMailSource, mailSourceRoute, readMailSource } from './mailSource';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const source = 'From: sender@example.invalid\r\nSubject: Figures\r\n\r\nBody';

function answering(response: ClientResponse, asked: ClientRequest[] = []) {
    return (request: ClientRequest) => {
        asked.push(request);

        return Promise.resolve(response);
    };
}

describe('mailSourceRoute', () => {
    it('encodes the identifier it is given rather than writing it into the path as it stands', () => {
        expect(mailSourceRoute('a/../b')).toBe('/messages/a%2F..%2Fb/source');
    });
});

describe('readMailSource', () => {
    it('asks for text under the bound the service cuts a source at', async () => {
        const asked: ClientRequest[] = [];

        await readMailSource(session, answering({ status: 200, body: source, headers: {} }, asked), messageId);

        expect(asked).toEqual([
            expect.objectContaining({
                method: 'GET',
                path: `https://mail.example.invalid/api/client/messages/${messageId}/source`,
                headers: { Accept: 'text/plain', Authorization: 'Basic dGVzdA==' },
                longestAnswer: longestMailSource,
            }),
        ]);
    });

    it('answers the text as received, whole', async () => {
        const result = await readMailSource(session, answering({ status: 200, body: source, headers: {} }), messageId);

        expect(result).toEqual({ outcome: 'read', value: { withheld: false, text: source, wholeOctets: null } });
    });

    it('reads how large the whole message is where the source was cut', async () => {
        const result = await readMailSource(
            session,
            answering({ status: 200, body: source, headers: { 'mailfathom-source-truncated-from': '8808038' } }),
            messageId,
        );

        expect(result).toEqual({ outcome: 'read', value: { withheld: false, text: source, wholeOctets: 8_808_038 } });
    });

    it('ignores a size it cannot read as a count rather than inventing one', async () => {
        const result = await readMailSource(
            session,
            answering({ status: 200, body: source, headers: { 'mailfathom-source-truncated-from': '-1' } }),
            messageId,
        );

        expect(result).toEqual({ outcome: 'read', value: { withheld: false, text: source, wholeOctets: null } });
    });

    it('answers a screened deployment as withheld rather than as a failure', async () => {
        const result = await readMailSource(session, answering({ status: 409, body: '', headers: {} }), messageId);

        expect(result).toEqual({ outcome: 'read', value: { withheld: true } });
    });

    it('answers a message with no local copy as missing', async () => {
        const result = await readMailSource(session, answering({ status: 404, body: '', headers: {} }), messageId);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it('refuses a source answered with nothing, which is how the transport hands over an answer past the bound', async () => {
        const result = await readMailSource(session, answering({ status: 200, body: '', headers: {} }), messageId);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('answers a deployment that did not answer as unavailable', async () => {
        const result = await readMailSource(session, () => Promise.reject(new Error('refused')), messageId);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});
