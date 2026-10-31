// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it, vi } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import type { ContactPhotoExchange, PortraitRead } from '../deployment/portraitExchange';
import { contactPhotoReader, photosOfAddresses } from './contactPhotos';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const drawn = 'data:image/png;base64,iVBORw0KGgo=';

const anna = { id: 'anna', book: 'Own' } as const;

function exchangeAnswering(...answers: PortraitRead[]): {
    exchange: ContactPhotoExchange;
    read: ReturnType<typeof vi.fn>;
    readColleague: ReturnType<typeof vi.fn>;
} {
    const read = vi.fn<ContactPhotoExchange['read']>();
    const readColleague = vi.fn<ContactPhotoExchange['readColleague']>();

    for (const answer of answers) {
        read.mockResolvedValueOnce(answer);
        readColleague.mockResolvedValueOnce(answer);
    }

    return {
        read,
        readColleague,
        exchange: {
            read,
            readColleague,
            replace: () => Promise.resolve({ outcome: 'stored' }),
            remove: () => Promise.resolve({ outcome: 'stored' }),
        },
    };
}

describe('contactPhotoReader', () => {
    it('asks once for a photo and answers the same photo again from what it drew', async () => {
        const { exchange, read } = exchangeAnswering({ outcome: 'drawn', picture: drawn });
        const reader = contactPhotoReader(session, exchange);

        const first = await reader.read(anna, 'stored-file', new AbortController().signal);
        const second = await reader.read(anna, 'stored-file', new AbortController().signal);

        expect([first, second]).toEqual([drawn, drawn]);
        expect(read).toHaveBeenCalledTimes(1);
    });

    // A refusal or an abandoned read is not a person with no photo, so the next row asking for it asks again.
    it('keeps nothing of a read that drew no photo', async () => {
        const { exchange, read } = exchangeAnswering(
            { outcome: 'refused', reason: 'unavailable' },
            { outcome: 'drawn', picture: drawn },
        );
        const reader = contactPhotoReader(session, exchange);

        const refused = await reader.read(anna, 'stored-file', new AbortController().signal);
        const asked = await reader.read(anna, 'stored-file', new AbortController().signal);

        expect([refused, asked]).toEqual([null, drawn]);
        expect(read).toHaveBeenCalledTimes(2);
    });

    // A member's portrait is their own record's, read at the directory, and its revision names nothing in the books.
    it("reads a colleague's portrait at the directory, apart from a contact's photo of the same name", async () => {
        const { exchange, read, readColleague } = exchangeAnswering({ outcome: 'drawn', picture: drawn });
        const reader = contactPhotoReader(session, exchange);

        const portrait = await reader.read({ id: 'piotr', book: 'Member' }, 'revision-1', new AbortController().signal);

        expect(portrait).toBe(drawn);
        expect(readColleague).toHaveBeenCalledWith(session, 'piotr', expect.any(AbortSignal));
        expect(read).not.toHaveBeenCalled();
    });
});

describe('photosOfAddresses', () => {
    // An AI answer names people by address alone, so they are resolved in one request rather than one each.
    it('resolves the people a block names in one request, and nobody where the deployment refused', async () => {
        const piotr = { book: 'Member', id: 'piotr', photo: 'revision-1' } as const;
        const sent: string[] = [];
        let refusing = false;
        const transport: MailFathomTransport = (request) => {
            sent.push(request.body ?? '');

            return Promise.resolve(
                refusing
                    ? { status: 403, headers: {}, body: '' }
                    : {
                          status: 200,
                          headers: {},
                          body: JSON.stringify({ photos: [{ address: 'piotr@nordwind.example', photo: piotr }] }),
                      },
            );
        };

        const resolved = await photosOfAddresses(session, transport, [
            'piotr@nordwind.example',
            'ola@nordwind.example',
        ]);
        refusing = true;
        const refused = await photosOfAddresses(session, transport, ['piotr@nordwind.example']);

        expect([...resolved]).toEqual([['piotr@nordwind.example', piotr]]);
        expect(refused.size).toBe(0);
        expect(sent).toEqual([
            JSON.stringify({ addresses: ['piotr@nordwind.example', 'ola@nordwind.example'] }),
            JSON.stringify({ addresses: ['piotr@nordwind.example'] }),
        ]);
    });
});
