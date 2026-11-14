// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport, MailFolderRole } from '@mailfathom/client-backend';
import { listedSender } from '../../../../tests/fixtures/senders';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { SignalledChangesContext, nothingSignalled, type SignalListener } from '../signals/signalledChanges';
import { ToastsProvider } from '../toasts/Toasts';
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
        acts = {},
    }: {
        readonly online?: boolean;

        /** What the mailbox acts answer, over a client that otherwise changes nothing. */
        readonly acts?: Partial<MailboxActs>;
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
            <ToastsProvider>
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
                            <MailboxActsContext value={{ ...nothingActed, ...acts }}>
                                <MessageList
                                    listMode="threads"
                                    session={session}
                                    transport={transport}
                                    scope={{ kind: 'account', accountId: 'work' }}
                                    accounts={[]}
                                    online={online}
                                    onOpen={onOpen}
                                />
                            </MailboxActsContext>
                        </SignalledChangesContext>
                    </ListedMailContext>
                </WorkspaceProvider>
            </ToastsProvider>
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

/** One message of a conversation as the thread route lists it, kept in the named folder. */
function member(id: string, folder: string, unread = false): Readonly<Record<string, unknown>> {
    return {
        position: 0,
        answeredId: null,
        email: {
            id,
            account: 'work',
            folder,
            threadId: 'thread-1',
            subject: 'The figures',
            receivedAt: '2026-08-31T09:41:00+00:00',
            sentAt: '2026-08-31T09:40:00+00:00',
            senderAddress: 'anna@example.invalid',
            senderDisplayName: 'Anna Nowak',
            toAddresses: ['user@example.invalid'],
            unread,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            categories: [],
            sizeOctets: 1_024,
            preview: 'The figures are attached.',
            sender: listedSender('unrecognized'),
        },
    };
}

/** A deployment listing one conversation and answering the thread route with its messages. */
function deploymentHolding(
    members: readonly Readonly<Record<string, unknown>>[],
    unreadCount = 0,
): MailFathomTransport {
    return (request) =>
        Promise.resolve({
            status: 200,
            headers: {},
            body: request.path.includes('/threads/')
                ? JSON.stringify({
                      threadId: 'thread-1',
                      messages: members,
                      participants: [{ address: 'anna@example.invalid', displayName: 'Anna Nowak', messageCount: 1 }],
                      messageCount: members.length,
                      moreMessagesNotAssembled: false,
                      moreParticipantsNotNamed: false,
                      nextCursor: null,
                      pageSize: 100,
                  })
                : pageOf([{ ...listed('thread-1', 'The figures'), unreadCount }]),
        });
}

const everyFolder = [
    member('received', 'inbox', true),
    member('sent', 'sent'),
    member('trashed', 'trash'),
    member('drafted', 'drafts'),
];

const rolesPlayed: Readonly<Record<string, MailFolderRole>> = {
    inbox: 'Inbox',
    sent: 'Sent',
    trash: 'Trash',
    drafts: 'Drafts',
};

// What the mailbox acts answer in these tests: every act is offered, and a folder plays the role it is named for.
function offering(perform: MailboxActs['perform']): Partial<MailboxActs> {
    return {
        refusalOf: () => null,
        spamOffered: true,
        folderRoleOf: (message) => rolesPlayed[message.folder] ?? null,
        perform,
    };
}

describe('MessageList read by conversation, acting on a conversation', () => {
    it('offers what is done to the whole conversation from the row’s menu, and no flag', async () => {
        drawing(deploymentHolding(everyFolder), { acts: offering(vi.fn()) });

        fireEvent.contextMenu(await screen.findByRole('button', { name: /The figures/ }));

        expect(screen.getAllByRole('menuitem').map((item) => item.textContent)).toStrictEqual([
            'Select threads',
            'Archive',
            'Mark as unread',
            'Spam…',
            'Move…',
            'Delete',
        ]);
    });

    it('marks every unread message of the conversation read where the row holds one', async () => {
        const perform = vi.fn();
        drawing(deploymentHolding(everyFolder, 1), { acts: offering(perform) });
        fireEvent.contextMenu(await screen.findByRole('button', { name: /The figures/ }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Mark as read' }));

        await waitFor(() => {
            expect(perform).toHaveBeenCalledTimes(1);
        });
        expect(perform.mock.calls[0]?.[0]).toBe('markRead');
        expect(
            (perform.mock.calls[0]?.[1] as readonly { storedEmailId: string }[]).map((taken) => taken.storedEmailId),
        ).toStrictEqual(['received']);
    });

    it('archives the received mail of the conversation and leaves what the reader sent where it is', async () => {
        const perform = vi.fn();
        drawing(deploymentHolding(everyFolder), { acts: offering(perform) });
        fireEvent.contextMenu(await screen.findByRole('button', { name: /The figures/ }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Archive' }));

        await waitFor(() => {
            expect(perform).toHaveBeenCalledTimes(1);
        });
        expect(
            (perform.mock.calls[0]?.[1] as readonly { storedEmailId: string }[]).map((taken) => taken.storedEmailId),
        ).toStrictEqual(['received']);
    });

    it('says nothing was changed when the conversation cannot be read, and performs nothing', async () => {
        const perform = vi.fn();
        const holding = deploymentHolding(everyFolder);
        drawing(
            (request) =>
                request.path.includes('/threads/')
                    ? Promise.resolve({ status: 503, body: '', headers: {} })
                    : holding(request),
            { acts: offering(perform) },
        );
        fireEvent.contextMenu(await screen.findByRole('button', { name: /The figures/ }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Archive' }));

        expect(
            await screen.findByText('Nothing was changed, because the conversation could not be read. Try again.'),
        ).toBeDefined();
        expect(perform).not.toHaveBeenCalled();
    });

    it('asks before deleting, counts what it files in the trash, and takes the row away once it is asked', async () => {
        // A delete already stands for the message the row is drawn from — asked from inside the conversation, of that
        // one message — which leaves the conversation listed.
        const perform = vi.fn();
        drawing(deploymentHolding(everyFolder), {
            acts: {
                ...offering(perform),
                asked: new Map([
                    [
                        'thread-1-newest',
                        { act: 'delete', from: 'inbox', leaves: true, destroys: false, awaiting: false },
                    ],
                ]),
            },
        });
        fireEvent.contextMenu(await screen.findByRole('button', { name: /The figures/ }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete' }));

        const asked = await screen.findByRole('dialog', { name: 'Delete 2 messages?' });
        expect(perform).not.toHaveBeenCalled();

        fireEvent.click(within(asked).getByRole('button', { name: 'Move to the trash' }));

        expect(perform.mock.calls[0]?.[0]).toBe('delete');
        expect(
            (perform.mock.calls[0]?.[1] as readonly { storedEmailId: string }[]).map((taken) => taken.storedEmailId),
        ).toStrictEqual(['received', 'sent']);

        // Deleted as a conversation, the row leaves at the press.
        await waitFor(() => {
            expect(screen.queryByRole('button', { name: /The figures/ })).toBeNull();
        });
    });

    it('picks a conversation out from its menu, by the message its row is drawn from', async () => {
        const drawn = drawing(deploymentHolding(everyFolder), { acts: offering(vi.fn()) });
        fireEvent.contextMenu(await screen.findByRole('button', { name: /The figures/ }));

        fireEvent.click(screen.getByRole('menuitem', { name: 'Select threads' }));

        const picked = drawn.list()?.conversations?.(['thread-1-newest']);
        expect(picked?.threads.map((thread) => thread.threadId)).toStrictEqual(['thread-1']);
        expect((await screen.findByRole('button', { name: /The figures/ })).getAttribute('aria-pressed')).toBe('true');
    });
});
