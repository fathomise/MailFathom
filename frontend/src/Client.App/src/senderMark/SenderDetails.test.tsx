// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import {
    readMailMessage,
    type ClientRequest,
    type ClientResponse,
    type ClientSession,
    type MailFathomTransport,
    type MailMessage,
} from '@mailfathom/client-backend';
import { newsletterId, newsletterMessage } from '../../../../tests/fixtures/messages';
import { messageSenderEvidence, type SenderKind } from '../../../../tests/fixtures/senders';
import { LocalizationProvider } from '../localization/Localization';
import { MessageSenderDetails, SenderDetails, type AskedSender } from './SenderDetails';

// What the harness's own control says, held as a value rather than written into the markup.
const opening = 'Explain';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

function wire(kind: SenderKind): unknown {
    return { ...newsletterMessage, ...messageSenderEvidence(kind) };
}

function answering(kind: SenderKind): ClientResponse {
    return { status: 200, body: JSON.stringify(wire(kind)), headers: {} };
}

async function read(kind: SenderKind): Promise<MailMessage> {
    const result = await readMailMessage(session, () => Promise.resolve(answering(kind)), newsletterId);

    if (result.outcome === 'failed') {
        throw new Error('The corpus message is not one the client reads.');
    }

    return result.value;
}

const fromRow: AskedSender = { storedEmailId: newsletterId, message: null, anchor: null };

function Explaining({ transport }: { readonly transport: MailFathomTransport }) {
    const dialog = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    dialog.current?.showModal();
                }}
            >
                {opening}
            </button>

            <SenderDetails asked={fromRow} session={session} transport={transport} dialog={dialog} />
        </>
    );
}

function explain(transport: MailFathomTransport): void {
    render(
        <LocalizationProvider>
            <Explaining transport={transport} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: opening }));
}

function ExplainingInHand({ message }: { readonly message: MailMessage }) {
    const dialog = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    dialog.current?.showModal();
                }}
            >
                {opening}
            </button>

            <MessageSenderDetails message={message} anchor={null} dialog={dialog} />
        </>
    );
}

describe('SenderDetails', () => {
    it('reads the message it explains, and says so while it does', async () => {
        const asked: ClientRequest[] = [];

        explain((request) => {
            asked.push(request);

            return Promise.resolve(answering('trusted'));
        });

        expect(screen.getByRole('status').textContent).toBe('Reading what was checked…');
        expect(await screen.findByRole('heading', { name: 'Trusted sender' })).toBeDefined();
        expect(asked.map((request) => request.path)).toEqual([
            `https://mail.example.invalid/api/client/messages/${newsletterId}`,
        ]);
    });

    it('lists every step under the three groups, each with its result', async () => {
        explain(() => Promise.resolve(answering('verified')));

        const signature = await screen.findByRole('region', { name: 'Signature' });

        expect(within(signature).getByText('Certificate authority')).toBeDefined();
        expect(within(signature).getAllByText('Passed')).toHaveLength(6);
        expect(screen.getByRole('region', { name: 'Sender authentication' })).toBeDefined();
        expect(screen.getByRole('region', { name: 'Trust' })).toBeDefined();
    });

    it('says why the mark is the mark, under who the message is from', async () => {
        explain(() => Promise.resolve(answering('forgedSignature')));

        expect(
            await screen.findByText(
                'Its signature does not match its content: it was changed after it was signed, or the signature is forged.',
            ),
        ).toBeDefined();
        expect(screen.getByText('news@example.invalid')).toBeDefined();
    });

    it('unfolds the certificate’s details from the step that names its authority', async () => {
        explain(() => Promise.resolve(answering('verified')));

        const unfold = await screen.findByRole('button', { name: 'Certificate details' });

        fireEvent.click(unfold);

        expect(unfold.getAttribute('aria-expanded')).toBe('true');
        expect(screen.getByText('Harbourline Public Email CA R4', { selector: 'dd' })).toBeDefined();
    });

    it('says what failed and offers to read again where the server could not be reached', async () => {
        const transport = vi
            .fn<MailFathomTransport>()
            .mockResolvedValueOnce({ status: 503, body: '', headers: {} })
            .mockResolvedValueOnce(answering('trusted'));

        explain(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Try again' }));

        expect(await screen.findByRole('heading', { name: 'Trusted sender' })).toBeDefined();
        expect(transport).toHaveBeenCalledTimes(2);
    });

    it('closes from its own control', async () => {
        explain(() => Promise.resolve(answering('trusted')));

        await screen.findByRole('heading', { name: 'Trusted sender' });
        fireEvent.click(screen.getByRole('button', { name: 'Close' }));

        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('offers nothing to act on the sender with, trusting one included', async () => {
        explain(() => Promise.resolve(answering('unrecognized')));

        const details = await screen.findByRole('dialog');

        await within(details).findByRole('heading', { name: 'Sender not recognized' });

        expect(within(details).getAllByRole('button')).toEqual([
            within(details).getByRole('button', { name: 'Close' }),
        ]);
    });
});

describe('MessageSenderDetails', () => {
    it('draws from the message already read, asking for nothing', async () => {
        const message = await read('failedAuthentication');

        render(
            <LocalizationProvider>
                <ExplainingInHand message={message} />
            </LocalizationProvider>,
        );

        fireEvent.click(screen.getByRole('button', { name: opening }));

        expect(screen.getByRole('heading', { name: 'Suspicious - sender authentication failed' })).toBeDefined();
        expect(
            screen.getByText(
                'example.invalid is not established as the author. lists.example.invalid authenticated the message instead.',
            ),
        ).toBeDefined();
    });
});
