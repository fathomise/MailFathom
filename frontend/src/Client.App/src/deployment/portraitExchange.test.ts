// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { contactPhotoExchange, portraitExchange } from './portraitExchange';

// The credentials mode this module's two `fetch` calls put on a request, for the reason `sendToDeployment.test.ts`
// states: it is a property of what went out, so `fetch` is what is watched. Both calls are asserted rather than the
// read alone, because a write that kept the default would open the browser's dialog over the settings screen exactly
// as the read would over the sign-in one.

const session = { baseAddress: 'https://mail.example', authorization: 'Basic c2FtcGxl' };

afterEach(() => {
    vi.restoreAllMocks();
});

describe('portraitExchange', () => {
    it('omits the credentials on the read and on the write alike', async () => {
        const sent = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }));

        await portraitExchange.read(session, new AbortController().signal);
        await portraitExchange.remove(session);

        expect(sent).toHaveBeenCalledTimes(2);
        for (const [, options] of sent.mock.calls) {
            expect(options?.credentials).toBe('omit');
        }
    });
});

describe('contactPhotoExchange', () => {
    it('reads no photo as none, and puts each request on the route of the contact it names', async () => {
        const sent = vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 204 }));

        const read = await contactPhotoExchange.read(session, 'anna', new AbortController().signal);
        const removed = await contactPhotoExchange.remove(session, 'anna');

        expect(read).toEqual({ outcome: 'none' });
        expect(removed).toEqual({ outcome: 'stored' });
        expect(sent.mock.calls.map(([path, options]) => [path, options?.method, options?.credentials])).toEqual([
            ['https://mail.example/api/client/contacts/anna/photo', 'GET', 'omit'],
            ['https://mail.example/api/client/contacts/anna/photo', 'DELETE', 'omit'],
        ]);
    });

    it('reads a contact the book does not hold as a refusal rather than as no photo', async () => {
        vi.spyOn(globalThis, 'fetch').mockResolvedValue(new Response(null, { status: 404 }));

        const replaced = await contactPhotoExchange.replace(session, 'anna', new Blob(['png']), 'image/png');

        expect(replaced.outcome).toBe('refused');
    });
});
