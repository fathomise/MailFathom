// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it, vi } from 'vitest';
import type { ClientSession } from '@mailfathom/client-backend';
import type { ContactPhotoExchange, PortraitRead } from '../deployment/portraitExchange';
import { contactPhotoReader } from './contactPhotos';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const drawn = 'data:image/png;base64,iVBORw0KGgo=';

function exchangeAnswering(...answers: PortraitRead[]): {
    exchange: ContactPhotoExchange;
    read: ReturnType<typeof vi.fn>;
} {
    const read = vi.fn<ContactPhotoExchange['read']>();

    for (const answer of answers) {
        read.mockResolvedValueOnce(answer);
    }

    return {
        read,
        exchange: {
            read,
            replace: () => Promise.resolve({ outcome: 'stored' }),
            remove: () => Promise.resolve({ outcome: 'stored' }),
        },
    };
}

describe('contactPhotoReader', () => {
    it('asks once for a photo and answers the same photo again from what it drew', async () => {
        const { exchange, read } = exchangeAnswering({ outcome: 'drawn', picture: drawn });
        const reader = contactPhotoReader(session, exchange);

        const first = await reader.read('anna', 'stored-file', new AbortController().signal);
        const second = await reader.read('anna', 'stored-file', new AbortController().signal);

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

        const refused = await reader.read('anna', 'stored-file', new AbortController().signal);
        const asked = await reader.read('anna', 'stored-file', new AbortController().signal);

        expect([refused, asked]).toEqual([null, drawn]);
        expect(read).toHaveBeenCalledTimes(2);
    });
});
