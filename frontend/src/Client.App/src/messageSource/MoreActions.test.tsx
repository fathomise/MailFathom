// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
    readMailSource,
    type ClientRequest,
    type ClientResponse,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MessageSourceReadingContext } from './messageSourceReading';
import { MoreActions } from './MoreActions';

// The transport is the whole of what these tests fake, so the route, the parsing, and the failure mapping are the
// real ones rather than replaced.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const source = 'From: Billing <billing@example.invalid>\r\nSubject: Quarterly invoice\r\n\r\nThe invoice is attached.';

/** A deployment answering each source request with the next answer it was given, recording what it was asked. */
function deploymentAnswering(...answers: readonly (ClientResponse | 'pending')[]): {
    readonly transport: MailFathomTransport;
    readonly asked: ClientRequest[];
} {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            const answer = answers[asked.length] ?? answers[answers.length - 1];

            asked.push(request);

            return answer === 'pending' || answer === undefined
                ? new Promise<ClientResponse>(() => undefined)
                : Promise.resolve(answer);
        },
    };
}

function drawMoreActions(transport: MailFathomTransport, online = true) {
    render(
        <LocalizationProvider>
            <MessageSourceReadingContext
                value={{ read: (storedEmailId) => readMailSource(session, transport, storedEmailId), online }}
            >
                <MoreActions storedEmailId={messageId} subject="Quarterly invoice" />
            </MessageSourceReadingContext>
        </LocalizationProvider>,
    );
}

function showSource(): void {
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
    fireEvent.click(screen.getByRole('menuitem', { name: 'Show source' }));
}

describe('MoreActions', () => {
    it('opens a menu headed by the subject that offers the source, and asks the deployment for nothing yet', () => {
        const { transport, asked } = deploymentAnswering({ status: 200, body: source, headers: {} });

        drawMoreActions(transport);
        fireEvent.click(screen.getByRole('button', { name: 'More actions' }));

        expect(screen.getByRole('menu', { name: 'Quarterly invoice' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Show source' })).toBeDefined();
        expect(asked).toEqual([]);
    });

    it('closes the menu on Escape and puts focus back on the control that opened it', () => {
        const { transport } = deploymentAnswering({ status: 200, body: source, headers: {} });

        drawMoreActions(transport);
        fireEvent.click(screen.getByRole('button', { name: 'More actions' }));
        fireEvent.keyDown(screen.getByRole('menuitem', { name: 'Show source' }), { key: 'Escape' });

        expect(screen.queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'More actions' }));
    });

    it('reads the source when Show source is pressed and draws it as received', async () => {
        const { transport, asked } = deploymentAnswering({ status: 200, body: source, headers: {} });

        drawMoreActions(transport);
        showSource();

        const surface = await screen.findByRole('dialog', { name: 'Message source' });

        expect(await within(surface).findByText(/The invoice is attached\./u)).toBeDefined();
        expect(asked.map((request) => request.path)).toEqual([
            `https://mail.example.invalid/api/client/messages/${messageId}/source`,
        ]);
    });

    it('stands a skeleton in the source while it is on the wire', async () => {
        const { transport } = deploymentAnswering('pending');

        drawMoreActions(transport);
        showSource();

        expect(await screen.findByRole('status', { name: 'Reading the message source' })).toBeDefined();
    });

    it('says why the source could not be read and reads it again when asked', async () => {
        const { transport, asked } = deploymentAnswering(
            { status: 404, body: '', headers: {} },
            { status: 200, body: source, headers: {} },
        );

        drawMoreActions(transport);
        showSource();

        expect((await screen.findByRole('alert')).textContent).toContain(
            'The message source could not be read: no longer there.',
        );

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(await screen.findByText(/The invoice is attached\./u)).toBeDefined();
        expect(asked).toHaveLength(2);
    });

    it('says the source is withheld where the deployment screens the mail it serves', async () => {
        const { transport } = deploymentAnswering({ status: 409, body: '', headers: {} });

        drawMoreActions(transport);
        showSource();

        expect(await screen.findByText(/a message's raw source cannot be screened/u)).toBeDefined();
    });

    it('says where a cut source stops and how large the whole message is', async () => {
        const { transport } = deploymentAnswering({
            status: 200,
            body: source,
            headers: { 'mailfathom-source-truncated-from': '8400000' },
        });

        drawMoreActions(transport);
        showSource();

        expect((await screen.findByRole('note')).textContent).toBe(
            'This message is 8.4 MB, more than can be shown at once, so its source stops after the first 1 MB.',
        );
    });

    it('asks nothing while there is no network and says so', async () => {
        const { transport, asked } = deploymentAnswering({ status: 200, body: source, headers: {} });

        drawMoreActions(transport, false);
        showSource();

        expect(await screen.findByText(/offline|network/iu)).toBeDefined();
        expect(asked).toEqual([]);
    });
});
