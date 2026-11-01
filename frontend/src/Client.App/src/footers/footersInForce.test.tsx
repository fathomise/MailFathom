// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { useFootersInForce } from './footersInForce';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic YWRh',
};

const document = {
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

function answering(status: number, body: unknown): MailFathomTransport {
    return vi.fn<MailFathomTransport>(() =>
        Promise.resolve({ status, body: typeof body === 'string' ? body : JSON.stringify(body), headers: {} }),
    );
}

// The hook is read through a component, which is how it is used: what a test asserts is what a screen was handed.
function Reading({ who, transport }: { readonly who: ClientSession | null; readonly transport: MailFathomTransport }) {
    const footers = useFootersInForce(who, transport);
    const drawn = [...footers.keys()].join(', ');

    return <p>{drawn === '' ? 'no footer' : drawn}</p>;
}

describe('useFootersInForce', () => {
    it('holds each account’s footer by the account, and none for an account sending without one', async () => {
        const transport = answering(200, {
            footers: [
                { account: 'work', source: 'organization', document },
                { account: 'home', source: 'own', document: null },
            ],
        });

        render(<Reading who={session} transport={transport} />);

        expect(await screen.findByText('work')).toBeDefined();
        expect(transport).toHaveBeenCalledOnce();
    });

    it('reads nothing where nobody may read the profile', () => {
        const transport = answering(200, { footers: [] });

        render(<Reading who={null} transport={transport} />);

        expect(screen.getByText('no footer')).toBeDefined();
        expect(transport).not.toHaveBeenCalled();
    });

    it('holds no footer where the footers could not be read, which is all a failed read costs', async () => {
        const transport = answering(500, '');

        render(<Reading who={session} transport={transport} />);

        await vi.waitFor(() => {
            expect(transport).toHaveBeenCalledOnce();
        });
        expect(screen.getByText('no footer')).toBeDefined();
    });
});
