// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { mostPersonPhotos, parsePersonPhoto, readPersonPhotos } from './personPhotos';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const piotr = { book: 'Member', id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90', photo: 'revision-1' } as const;

function answering(response: ClientResponse): { transport: MailFathomTransport; sent: ClientRequest[] } {
    const sent: ClientRequest[] = [];

    return {
        sent,
        transport: (request) => {
            sent.push(request);

            return Promise.resolve(response);
        },
    };
}

describe('parsePersonPhoto', () => {
    it('reads a reference, and an absent or null one as a person drawn by initials', () => {
        expect(parsePersonPhoto(piotr)).toEqual({ photo: piotr });
        expect(parsePersonPhoto(undefined)).toEqual({ photo: null });
        expect(parsePersonPhoto(null)).toEqual({ photo: null });
    });

    it('refuses a reference naming a book that carries no photos', () => {
        expect(parsePersonPhoto({ ...piotr, book: 'Collected' })).toBeNull();
        expect(parsePersonPhoto({ ...piotr, photo: 7 })).toBeNull();
    });
});

describe('readPersonPhotos', () => {
    it('asks with the addresses in the body and answers each under the spelling it was asked by', async () => {
        const { transport, sent } = answering({
            status: 200,
            headers: {},
            body: JSON.stringify({ photos: [{ address: 'Piotr@nordwind.example', photo: piotr }] }),
        });

        const answer = await readPersonPhotos(session, transport, ['Piotr@nordwind.example', 'ola@nordwind.example']);

        expect(answer).toEqual({ outcome: 'read', value: new Map([['Piotr@nordwind.example', piotr]]) });
        expect(sent.map((request) => [request.method, request.path, request.body])).toEqual([
            [
                'POST',
                'https://mail.example.invalid/api/client/contacts/photos',
                JSON.stringify({ addresses: ['Piotr@nordwind.example', 'ola@nordwind.example'] }),
            ],
        ]);
    });

    it('sends nothing for no addresses and refuses more than the route resolves', async () => {
        const { transport, sent } = answering({ status: 200, headers: {}, body: '{"photos":[]}' });

        const none = await readPersonPhotos(session, transport, []);
        const tooMany = await readPersonPhotos(
            session,
            transport,
            Array.from({ length: mostPersonPhotos + 1 }, (_, at) => `person${String(at)}@example.invalid`),
        );

        expect(none).toEqual({ outcome: 'read', value: new Map() });
        expect(tooMany.outcome).toBe('failed');
        expect(sent).toEqual([]);
    });

    it('refuses an answer carrying a malformed reference rather than drawing any of it', async () => {
        const { transport } = answering({
            status: 200,
            headers: {},
            body: JSON.stringify({ photos: [{ address: 'piotr@nordwind.example', photo: { book: 'Own' } }] }),
        });

        const answer = await readPersonPhotos(session, transport, ['piotr@nordwind.example']);

        expect(answer.outcome).toBe('failed');
    });
});
