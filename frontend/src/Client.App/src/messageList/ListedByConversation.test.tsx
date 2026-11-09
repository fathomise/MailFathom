// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { SignalledChangesContext, nothingSignalled, type SignalListener } from '../signals/signalledChanges';
import { WorkspaceProvider } from '../workspace/Workspace';
import { MessageList } from './MessageList';
import { ListedMailContext, nothingListed, type ListedMailbox } from './useListedMail';

// The message list read by conversation, driven through the transport, which is the network boundary and the whole of
// what these tests fake — so the route, the grouping, the parsing, and the failure mapping are under test rather than
// replaced.

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
        categories: [],
        title: null,
    };
}

function pageOf(conversations: readonly Readonly<Record<string, unknown>>[], nextCursor: string | null = null): string {
    return JSON.stringify({ conversations, nextCursor, previousCursor: null, pageSize: 25 });
}

const answered = (body: string) => ({ status: 200, body });

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

/** A deployment answering each page by the cursor it is asked with, the leading page where it is asked with none. */
function deploymentServing(pages: Readonly<Record<string, string>>): {
    transport: MailFathomTransport;
    asked: ClientRequest[];
} {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            const cursor = new URL(request.path).searchParams.get('cursor') ?? '';
            const body = pages[cursor];

            return Promise.resolve(
                body === undefined ? { status: 503, body: '', headers: {} } : { status: 200, body, headers: {} },
            );
        },
    };
}

interface Drawn {
    readonly say: (signal: Parameters<SignalListener>[0]) => void;
    readonly list: () => ListedMailbox | null;
}

function drawing(
    transport: MailFathomTransport,
    {
        online = true,
        onOpen = () => undefined,
    }: {
        readonly online?: boolean;
        readonly onOpen?: (
            storedEmailId: string,
            subject: string | null,
            threadId: string | null,
            byThread?: boolean,
        ) => void;
    } = {},
): Drawn {
    const listeners: SignalListener[] = [];
    let listedNow: ListedMailbox | null = null;

    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ListedMailContext
                    value={{
                        ...nothingListed,
                        listing: (list) => {
                            listedNow = list;
                        },
                    }}
                >
                    <SignalledChangesContext
                        value={{
                            ...nothingSignalled,
                            listen: (listener) => {
                                listeners.push(listener);

                                return () => {
                                    listeners.splice(listeners.indexOf(listener), 1);
                                };
                            },
                        }}
                    >
                        <MessageList
                            listMode="threads"
                            session={session}
                            transport={transport}
                            scope={{ kind: 'account', accountId: 'work' }}
                            accounts={[]}
                            online={online}
                            onOpen={onOpen}
                        />
                    </SignalledChangesContext>
                </ListedMailContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );

    return {
        say: (signal) => {
            act(() => {
                for (const listener of [...listeners]) {
                    listener(signal);
                }
            });
        },
        list: () => listedNow,
    };
}

function searchOf(request: ClientRequest | undefined): URLSearchParams {
    return new URL(request?.path ?? 'https://invalid.invalid').searchParams;
}

// Lets every answer already given land, so a test can say no further request went out.
async function settled(): Promise<void> {
    await act(async () => {
        await Promise.resolve();
    });
}

describe('MessageList read by conversation', () => {
    it('says it is reading the conversations before anything has answered', () => {
        drawing(() => new Promise(() => undefined));

        expect(screen.getByRole('status').textContent).toBe('Reading your conversations…');
    });

    it('reads the mail list grouped by conversation for the account picked, and draws one row for each', async () => {
        const { transport, asked } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures'), listed('two', 'The audit')])),
        );

        drawing(transport);

        expect(await screen.findByText('The quarterly figures')).toBeDefined();
        expect(screen.getByText('The audit')).toBeDefined();
        expect(screen.getByText('That is every conversation.')).toBeDefined();
        expect(new URL(asked[0]?.path ?? '').pathname).toBe('/api/client/emails');
        expect(searchOf(asked[0]).get('grouping')).toBe('conversation');
        expect(searchOf(asked[0]).get('account')).toBe('work');
        expect(searchOf(asked[0]).get('folder')).toBeNull();
        expect(searchOf(asked[0]).get('includeJunk')).toBeNull();
    });

    it('says there is no conversation yet rather than drawing an empty list', async () => {
        drawing(deploymentAnswering(answered(pageOf([]))).transport);

        expect(await screen.findByText('There is no conversation here yet.')).toBeDefined();
    });

    it('says the machine is offline rather than reading', () => {
        const { transport, asked } = deploymentAnswering(answered(pageOf([])));

        drawing(transport, { online: false });

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

    it('reads the next page from the cursor the last one answered with as the reader nears its end', async () => {
        const { transport, asked } = deploymentServing({
            '': pageOf([listed('one', 'The quarterly figures')], 'onwards'),
            onwards: pageOf([listed('two', 'The audit')]),
        });

        drawing(transport);

        expect(await screen.findByText('The audit')).toBeDefined();
        expect(screen.getByText('The quarterly figures')).toBeDefined();
        expect(searchOf(asked[1]).get('cursor')).toBe('onwards');
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
        expect(searchOf(asked[2]).get('cursor')).toBe('onwards');
    });

    it('opens the conversation pressed at its newest message, as a conversation', async () => {
        const onOpen = vi.fn();

        drawing(deploymentAnswering(answered(pageOf([listed('one', 'The quarterly figures')]))).transport, { onOpen });

        fireEvent.click(await screen.findByRole('button', { name: /The quarterly figures/u }));

        expect(onOpen).toHaveBeenCalledWith('one-newest', 'The quarterly figures', 'one', true);
    });

    // A conversation that moved to the top stands on the leading page and on the page further down it was read on,
    // until that page is read again.
    it('draws a conversation two held pages both hold once, where the earlier page stands it', async () => {
        const { transport } = deploymentServing({
            '': pageOf([listed('one', 'The quarterly figures'), listed('two', 'The audit')], 'onwards'),
            onwards: pageOf([listed('one', 'The quarterly figures'), listed('three', 'The offsite')]),
        });

        drawing(transport);

        expect(await screen.findByText('The offsite')).toBeDefined();
        expect(screen.getAllByText('The quarterly figures')).toHaveLength(1);
        expect(screen.getAllByRole('listitem')).toHaveLength(3);
    });

    it('reads the grouped list narrowed by an AI filter’s criteria when one is pressed', async () => {
        const { transport, asked } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures')])),
            answered(pageOf([listed('two', 'The audit')])),
        );

        const { list } = drawing(transport);

        await screen.findByText('The quarterly figures');
        act(() => {
            list()?.stand('commitments');
        });

        expect(await screen.findByText('The audit')).toBeDefined();
        expect(screen.queryByText('The quarterly figures')).toBeNull();
        expect(searchOf(asked.at(-1)).get('carriesMark')).toBe('Commitment');
        expect(searchOf(asked.at(-1)).get('grouping')).toBe('conversation');
        expect(searchOf(asked.at(-1)).get('cursor')).toBeNull();
    });

    it('reads the grouped list narrowed by the list’s own filters', async () => {
        const { transport, asked } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures')])),
            answered(pageOf([listed('two', 'The audit')])),
        );

        drawing(transport);

        await screen.findByText('The quarterly figures');
        fireEvent.click(screen.getByRole('button', { name: 'Filters' }));
        fireEvent.click(screen.getByRole('checkbox', { name: 'Only unread' }));

        expect(await screen.findByText('The audit')).toBeDefined();
        expect(searchOf(asked.at(-1)).get('unread')).toBe('true');
        expect(searchOf(asked.at(-1)).get('grouping')).toBe('conversation');
    });

    it('offers neither junk nor the readings switch, which a conversation has no use for', async () => {
        drawing(deploymentAnswering(answered(pageOf([listed('one', 'The quarterly figures')]))).transport);

        await screen.findByText('The quarterly figures');
        fireEvent.click(screen.getByRole('button', { name: 'Filters' }));

        expect(screen.queryByRole('checkbox', { name: /spam/iu })).toBeNull();
        expect(screen.queryByRole('switch')).toBeNull();
    });

    it('reads its leading page again when mail arrives in its account, and not when it arrives in another', async () => {
        const { transport, asked } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures')])),
            answered(pageOf([listed('two', 'The audit'), listed('one', 'The quarterly figures')])),
        );

        const { say } = drawing(transport);

        await screen.findByText('The quarterly figures');
        say({ kind: 'mail.arrived', account: 'home', folder: 'INBOX', count: 1 });
        await settled();

        expect(asked).toHaveLength(1);

        say({ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 1 });

        expect(await screen.findByText('The audit')).toBeDefined();
        expect(asked).toHaveLength(2);
        expect(searchOf(asked[1]).get('cursor')).toBeNull();
    });

    it('reads again the page holding a conversation whose newest message changed, from that page’s own cursor', async () => {
        const { transport, asked } = deploymentServing({
            '': pageOf([listed('one', 'The quarterly figures')], 'onwards'),
            onwards: pageOf([listed('two', 'The audit')]),
        });

        const { say } = drawing(transport);

        await screen.findByText('The audit');
        say({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['two-newest'] });

        await waitFor(() => {
            expect(asked).toHaveLength(3);
        });
        expect(searchOf(asked[2]).get('cursor')).toBe('onwards');
    });

    // A row names only a conversation's newest message, while reading or flagging any of the others changes the unread
    // count it draws — so the statement cannot be matched to a page, and every page on the screen is read again.
    it('reads its pages again when the flags of a message older than any it names change', async () => {
        const { transport, asked } = deploymentServing({
            '': pageOf([listed('one', 'The quarterly figures')], 'onwards'),
            onwards: pageOf([listed('two', 'The audit')]),
        });

        const { say } = drawing(transport);

        await screen.findByText('The audit');
        say({
            kind: 'mail.flags.changed',
            account: 'work',
            folder: 'INBOX',
            flags: [{ email: 'two-earlier', isSeen: true, isFlagged: false }],
        });

        await waitFor(() => {
            expect(asked).toHaveLength(4);
        });
        expect(asked.slice(2).map((request) => searchOf(request).get('cursor'))).toEqual(
            expect.arrayContaining([null, 'onwards']),
        );
    });

    it('reads a list scrolled deep in again page by page from each page’s own cursor on a refresh', async () => {
        const { transport, asked } = deploymentServing({
            '': pageOf([listed('one', 'The quarterly figures')], 'onwards'),
            onwards: pageOf([listed('two', 'The audit')]),
        });

        const { say } = drawing(transport);

        await screen.findByText('The audit');
        say({ kind: 'refresh' });

        await waitFor(() => {
            expect(asked).toHaveLength(4);
        });
        expect(asked.slice(2).map((request) => searchOf(request).get('cursor'))).toEqual(
            expect.arrayContaining([null, 'onwards']),
        );
    });

    it('answers a burst of signals with one read rather than one per signal', async () => {
        const { transport, asked } = deploymentAnswering(answered(pageOf([listed('one', 'The quarterly figures')])));

        const { say } = drawing(transport);

        await screen.findByText('The quarterly figures');
        say({ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 1 });
        say({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['one-newest'] });
        say({ kind: 'mail.arrived', account: 'work', folder: 'INBOX', count: 2 });
        say({ kind: 'refresh' });

        await waitFor(() => {
            expect(asked).toHaveLength(2);
        });
        await settled();

        expect(asked).toHaveLength(2);
    });

    it('reads the list again when the contacts change, which every sender on it is recognized by', async () => {
        const { transport } = deploymentAnswering(
            answered(pageOf([listed('one', 'The quarterly figures')])),
            answered(pageOf([listed('two', 'The audit'), listed('one', 'The quarterly figures')])),
        );

        const { say } = drawing(transport);

        await screen.findByText('The quarterly figures');
        say({ kind: 'contacts.changed' });

        expect(await screen.findByText('The audit')).toBeDefined();
    });

    it('keeps the rows it holds and says nothing when reading a page again after a change fails', async () => {
        const { transport, asked } = deploymentAnswering(answered(pageOf([listed('one', 'The quarterly figures')])), {
            status: 503,
            body: '',
        });

        const { say } = drawing(transport);

        await screen.findByText('The quarterly figures');
        say({ kind: 'mail.changed', account: 'work', folder: 'INBOX', emails: ['one-newest'] });

        await waitFor(() => {
            expect(asked).toHaveLength(2);
        });
        await settled();

        expect(screen.getByText('The quarterly figures')).toBeDefined();
        expect(screen.queryByRole('alert')).toBeNull();
    });
});
