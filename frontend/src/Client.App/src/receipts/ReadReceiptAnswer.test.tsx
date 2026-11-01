// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type {
    ClientRequest,
    ClientResponse,
    ClientSession,
    MailFathomTransport,
    MailReadReceiptRequest,
} from '@mailfathom/client-backend';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { ReadReceiptAnswer } from './ReadReceiptAnswer';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const asked: MailReadReceiptRequest = { address: 'marta@example.invalid', namesAnotherAddress: false, sentAt: null };

/** The connection status at one level, as the frame hands it. */
function statusAt(level: 'online' | 'disconnected'): ConnectionStatus {
    const shared = {
        attempts: null,
        retry: () => undefined,
        detailsOpen: false,
        showDetails: () => undefined,
        disconnectedNow: () => level === 'disconnected',
    };

    return level === 'online' ? { level, ...shared } : { level, cause: 'unanswered', since: 0, ...shared };
}

/** A transport answering every request with one response, keeping what it was asked, and answering when told to. */
function answering(response: ClientResponse) {
    const requests: ClientRequest[] = [];
    let answer: () => void = () => undefined;
    const transport: MailFathomTransport = (request) => {
        requests.push(request);

        return new Promise((resolve) => {
            answer = () => {
                resolve(response);
            };
        });
    };

    return {
        requests,
        transport,
        answer: () => {
            answer();
        },
    };
}

function drawing(
    transport: MailFathomTransport,
    request: MailReadReceiptRequest = asked,
    status: 'online' | 'disconnected' = 'online',
) {
    render(
        <LocalizationProvider>
            <ConnectionStatusContext value={statusAt(status)}>
                <ReadReceiptAnswer
                    session={session}
                    transport={transport}
                    storedEmailId="message-1"
                    request={request}
                    sender="Marta Nowak"
                />
            </ConnectionStatusContext>
        </LocalizationProvider>,
    );
}

const sentAnswer: ClientResponse = { status: 200, body: '{"sentAt":"2026-08-29T08:52:00+00:00"}', headers: {} };

describe('ReadReceiptAnswer', () => {
    it('offers to send the receipt and says nothing goes unless the reader sends it', () => {
        drawing(answering(sentAnswer).transport);

        expect(screen.getByText('Marta Nowak asked for a read receipt.')).toBeDefined();
        expect(
            screen.getByText('Sending one tells them you opened this message. Nothing is sent unless you send it.'),
        ).toBeDefined();
        expect(screen.getByRole('button', { name: 'Send read receipt' })).toBeDefined();
    });

    it('names the address a receipt would go to where it is not the sender’s', () => {
        drawing(answering(sentAnswer).transport, {
            ...asked,
            namesAnotherAddress: true,
            address: 'track@example.invalid',
        });

        expect(
            screen.getByText(
                'Marta Nowak asked for a read receipt, to be sent to track@example.invalid rather than to them.',
            ),
        ).toBeDefined();
    });

    it('sends nothing until the reader presses it', () => {
        const sending = answering(sentAnswer);

        drawing(sending.transport);

        expect(sending.requests).toHaveLength(0);
    });

    it('says it is sending, and refuses a second press while it is', () => {
        const sending = answering(sentAnswer);

        drawing(sending.transport);
        fireEvent.click(screen.getByRole('button', { name: 'Send read receipt' }));
        fireEvent.click(screen.getByRole('button', { name: 'Sending…' }));

        expect(screen.getByRole('button', { name: 'Sending…' }).getAttribute('aria-disabled')).toBe('true');
        expect(sending.requests).toHaveLength(1);
    });

    it('replaces the offer for good with the line saying when the receipt went and where, and focuses it', async () => {
        const sending = answering(sentAnswer);

        drawing(sending.transport);
        fireEvent.click(screen.getByRole('button', { name: 'Send read receipt' }));
        sending.answer();

        const line = await screen.findByRole('status');

        expect(line.textContent).toMatch(/^Read receipt sent .+ to marta@example\.invalid\.$/u);
        expect(document.activeElement).toBe(line);
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('takes a receipt the deployment says went earlier as sent', async () => {
        const sending = answering({
            status: 409,
            body: '{"reason":"AlreadySent","sentAt":"2026-08-29T08:52:00+00:00"}',
            headers: {},
        });

        drawing(sending.transport);
        fireEvent.click(screen.getByRole('button', { name: 'Send read receipt' }));
        sending.answer();

        expect((await screen.findByRole('status')).textContent).toMatch(
            /^Read receipt sent .+ to marta@example\.invalid\.$/u,
        );
    });

    it('opens on the line for a receipt sent before, offering nothing', () => {
        drawing(answering(sentAnswer).transport, { ...asked, sentAt: '2026-08-29T08:52:00+00:00' });

        expect(screen.getByText(/^Read receipt sent .+ to marta@example\.invalid\.$/u)).toBeDefined();
        expect(screen.queryByRole('button')).toBeNull();
        expect(screen.queryByRole('status')).toBeNull();
    });

    it('says why a receipt was not sent, and offers it again', async () => {
        const sending = answering({ status: 503, body: '', headers: {} });

        drawing(sending.transport);
        fireEvent.click(screen.getByRole('button', { name: 'Send read receipt' }));
        sending.answer();

        expect((await screen.findByRole('alert')).textContent).toBe('The read receipt was not sent: unavailable.');
        expect(screen.getByRole('button', { name: 'Send read receipt' }).getAttribute('aria-disabled')).toBeNull();
    });

    it('says the server refused to send one', async () => {
        const sending = answering({ status: 409, body: '{"reason":"Spam"}', headers: {} });

        drawing(sending.transport);
        fireEvent.click(screen.getByRole('button', { name: 'Send read receipt' }));
        sending.answer();

        expect((await screen.findByRole('alert')).textContent).toBe(
            'The read receipt was not sent: the server refused to send it.',
        );
    });

    it('refuses to send one with no connection to the server', () => {
        const sending = answering(sentAnswer);

        drawing(sending.transport, asked, 'disconnected');
        fireEvent.click(screen.getByRole('button', { name: 'Send read receipt' }));

        expect(screen.getByRole('button', { name: 'Send read receipt' }).getAttribute('aria-disabled')).toBe('true');
        expect(sending.requests).toHaveLength(0);
    });
});
