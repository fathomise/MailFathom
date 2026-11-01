// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, ListedMailThread, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { SignalledChangesContext, nothingSignalled, type SignalListener } from '../signals/signalledChanges';
import { WorkspaceProvider } from '../workspace/Workspace';
import { ThreadList } from './ThreadList';

// The network boundary is the transport and it is the whole of what these tests fake, so the route, the parsing, and
// the failure mapping are under test rather than replaced.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

function listed(id: string, subject: string): Readonly<Record<string, unknown>> {
    return {
        threadId: id,
        account: 'work',
        messageCount: 1,
        unreadCount: 0,
        hasAttachments: false,
        participants: [{ address: 'anna@example.invalid', displayName: 'Anna Nowak', reader: false }],
        moreParticipantsNotNamed: false,
        newest: {
            id: `${id}-newest`,
            subject,
            receivedAt: '2026-08-31T09:41:00+00:00',
            senderAddress: 'anna@example.invalid',
            senderDisplayName: 'Anna Nowak',
            preview: 'The figures are attached.',
            own: false,
            senderPhoto: null,
        },
        newestReceivedSender: null,
        title: null,
    };
}

function pageOf(threads: readonly Readonly<Record<string, unknown>>[], nextCursor: string | null = null): string {
    return JSON.stringify({ threads, nextCursor, pageSize: 25 });
}

/** A deployment answering each request with the next of what it was given, the last answer standing after that. */
function deploymentAnswering(...answers: readonly { status: number; body: string }[]): {
    transport: MailFathomTransport;
    asked: ClientRequest[];
} {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            const answer = answers[Math.min(asked.length, answers.length - 1)] ?? { status: 503, body: '' };

            asked.push(request);

            return Promise.resolve({ ...answer, headers: {} });
        },
    };
}

const answered = (body: string) => ({ status: 200, body });

function drawing(
    transport: MailFathomTransport,
    online = true,
    onOpen: (thread: ListedMailThread) => void = () => undefined,
    listen: (listener: SignalListener) => () => void = nothingSignalled.listen,
): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <SignalledChangesContext value={{ ...nothingSignalled, listen }}>
                    <ThreadList
                        session={session}
                        transport={transport}
                        account="work"
                        online={online}
                        onOpen={onOpen}
                    />
                </SignalledChangesContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

describe('ThreadList', () => {
    it('says it is reading the conversations before anything has answered', () => {
        drawing(() => new Promise(() => undefined));

        expect(screen.getByRole('status').textContent).toBe('Reading your conversations…');
    });

    it('draws one row for each conversation of the account picked, and says when it has read them all', async () => {
        const { transport, asked } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures'), listed('two', 'The audit')])),
        );

        drawing(transport);

        expect(await screen.findByText('The quarterly figures')).toBeDefined();
        expect(screen.getByText('The audit')).toBeDefined();
        expect(screen.getByText('That is every conversation.')).toBeDefined();
        expect(asked[0]?.path).toContain('account=work');
    });

    it('says there is no conversation yet rather than drawing an empty list', async () => {
        drawing(deploymentAnswering(answered(pageOf([]))).transport);

        expect(await screen.findByText('There is no conversation here yet.')).toBeDefined();
    });

    it('says the machine is offline rather than reading', () => {
        const { transport, asked } = deploymentAnswering(answered(pageOf([])));

        drawing(transport, false);

        expect(screen.getByText(/offline/i)).toBeDefined();
        expect(asked).toHaveLength(0);
    });

    it('says what failed, and reads again when asked to', async () => {
        drawing(
            deploymentAnswering({ status: 503, body: '' }, answered(pageOf([listed('one', 'The quarterly figures')])))
                .transport,
        );

        expect((await screen.findByRole('alert')).textContent).toMatch(/^The conversations could not be read/u);

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(await screen.findByText('The quarterly figures')).toBeDefined();
    });

    it('reads the next page as the reader nears the end of what it holds', async () => {
        const { transport, asked } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures')], 'onwards')),
            answered(pageOf([listed('two', 'The audit')])),
        );

        drawing(transport);

        expect(await screen.findByText('The audit')).toBeDefined();
        expect(screen.getByText('The quarterly figures')).toBeDefined();
        expect(asked[1]?.path).toContain('cursor=onwards');
    });

    it('keeps what it holds when a later page fails, says which part is missing, and reads that page when asked', async () => {
        const { transport, asked } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures')], 'onwards')),
            { status: 503, body: '' },
            answered(pageOf([listed('two', 'The audit')])),
        );

        drawing(transport);

        expect((await screen.findByRole('alert')).textContent).toMatch(/^Some of the conversations could not be read/u);
        expect(screen.getByText('The quarterly figures')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(await screen.findByText('The audit')).toBeDefined();
        expect(screen.getByText('The quarterly figures')).toBeDefined();
        expect(asked[2]?.path).toContain('cursor=onwards');
    });

    it('opens the conversation pressed', async () => {
        const onOpen = vi.fn();

        drawing(
            deploymentAnswering(answered(pageOf([listed('one', 'The quarterly figures')]))).transport,
            true,
            onOpen,
        );

        fireEvent.click(await screen.findByRole('button', { name: /The quarterly figures/u }));

        expect(onOpen).toHaveBeenCalledWith(expect.objectContaining({ threadId: 'one' }));
    });

    it('reads the list again when the deployment says mail changed, and draws what it answers', async () => {
        let say: SignalListener = () => undefined;
        const { transport } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures')])),
            answered(pageOf([listed('two', 'The audit'), listed('one', 'The quarterly figures')])),
        );

        drawing(
            transport,
            true,
            () => undefined,
            (listener) => {
                say = listener;

                return () => undefined;
            },
        );

        await screen.findByText('The quarterly figures');

        act(() => {
            say({ kind: 'mail.changed', account: 'work', folder: null, emails: ['two'] });
        });

        expect(await screen.findByText('The audit')).toBeDefined();
    });

    it('keeps the list it holds when reading it again after a change fails', async () => {
        let say: SignalListener = () => undefined;
        const { transport, asked } = deploymentAnswering(answered(pageOf([listed('one', 'The quarterly figures')])), {
            status: 503,
            body: '',
        });

        drawing(
            transport,
            true,
            () => undefined,
            (listener) => {
                say = listener;

                return () => undefined;
            },
        );

        await screen.findByText('The quarterly figures');

        act(() => {
            say({ kind: 'mail.changed', account: 'work', folder: null, emails: ['two'] });
        });

        await waitFor(() => {
            expect(asked).toHaveLength(2);
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(screen.getByText('The quarterly figures')).toBeDefined();
        expect(screen.queryByRole('alert')).toBeNull();
    });
});
