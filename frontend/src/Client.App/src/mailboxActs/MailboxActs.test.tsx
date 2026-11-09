// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ReactNode } from 'react';
import { act, fireEvent, renderHook, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    MailMutationOutcome,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadMarkingContext, nothingMarkedRead, type ReadMarking } from '../readMarking/useReadMarking';
import { ListedMailContext, nothingListed, type DrawnMail, type ListedMail } from '../messageList/useListedMail';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { PendingChangesProvider } from '../pendingChanges/PendingChanges';
import { unansweredWriteRetryDelay } from '../pendingChanges/usePendingChanges';
import { TelemetryContext, noTelemetry, type ClientTelemetry } from '../telemetry/clientTelemetry';
import { recordingTelemetry, recordsOf } from '../telemetry/telemetry.harness';
import { ToastsProvider } from '../toasts/Toasts';
import { mostMessagesPerMutation } from '@mailfathom/client-backend';
import { standsInTheWay } from './drawnActs';
import { MailboxActsProvider } from './MailboxActs';
import type { MoveDestination } from './mailboxDestinations';
import { useMailboxActs, type ActedMessage, type MailboxAct, type MailboxActs } from './useMailboxActs';

// The provider is driven the way a control drives it — through the hook — because what is being proven is what the
// deployment was asked for and what the person was told afterwards. Nothing here reaches a mail server: an act writes
// a record and answers, so every assertion is about the request that carried it and the toast that reported it.
//
// The pending-changes queue is mounted above it as the frame mounts it, because what an answer said beyond the change
// being made — a refusal, or a submission nobody answered — is the queue's to say, and saying it once for every act is
// what is being proven alongside the act.

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const invoice: ActedMessage = {
    storedEmailId: 'message-1',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
    spam: null,
};
const receipt: ActedMessage = {
    storedEmailId: 'message-2',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
    spam: null,
};
const unsent: ActedMessage = {
    storedEmailId: 'message-4',
    account: 'work',
    folder: 'work-drafts',
    unread: false,
    flagged: false,
    spam: null,
};

const discarded: ActedMessage = {
    storedEmailId: 'message-3',
    account: 'work',
    folder: 'work-trash',
    unread: false,
    flagged: false,
    spam: null,
};

const folders = JSON.stringify({
    synchronizationEnabled: true,
    accounts: [
        {
            account: {
                id: 'work',
                displayName: 'Work',
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: null,
                behind: false,
                filesJunk: false,
                shared: false,
                synchronizationStateSince: null,
                behindSince: null,
                waitingChangeCount: 0,
                oldestWaitingChangeCommittedAt: null,
            },
            folders: [
                {
                    id: 'work-inbox',
                    role: 'Inbox',
                    path: ['INBOX'],
                    storedEmailCount: 0,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                },
                {
                    id: 'work-archive',
                    role: 'Archive',
                    path: ['Archive'],
                    storedEmailCount: 0,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                },
                {
                    id: 'work-drafts',
                    role: 'Drafts',
                    path: ['Drafts'],
                    storedEmailCount: 0,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                },
                {
                    id: 'work-trash',
                    role: 'Trash',
                    path: ['Trash'],
                    storedEmailCount: 0,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                },
                {
                    id: 'work-junk',
                    role: 'Junk',
                    path: ['Junk'],
                    storedEmailCount: 0,
                    unreadEmailCount: 0,
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                },
            ],
        },
    ],
});

interface Deployment {
    readonly transport: MailFathomTransport;
    readonly requests: ClientRequest[];
}

/**
 * A deployment answering the folders it has, and every submitted batch with the outcomes it was told to answer.
 *
 * A recorded message is answered with one record named after it, so a release can be read back as the records it
 * named; a release leaves each record pending, the answer the route gives.
 */
function deploymentAnswering(outcomes: Readonly<Record<string, MailMutationOutcome>> = {}, status = 200): Deployment {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            if (request.path.endsWith('/folders')) {
                return Promise.resolve({ status: 200, body: folders, headers: {} });
            }

            if (request.path.endsWith('/releases')) {
                const { recordIds } = JSON.parse(request.body ?? '{}') as { recordIds: string[] };

                return Promise.resolve({
                    status,
                    body: JSON.stringify({
                        changes: recordIds.map((recordId) => ({
                            recordId,
                            storedEmailId: recordId.replace('record-', ''),
                            state: 'pending',
                            outcomeUnknown: false,
                        })),
                    }),
                    headers: {},
                });
            }

            const asked = JSON.parse(request.body ?? '{}') as {
                changes?: { storedEmailId: string }[];
                moves?: { storedEmailId: string }[];
                deletes?: { storedEmailId: string }[];
            };

            // The flag route answers a list of the records a message's change became, and the move and delete routes
            // the one record each writes — the shapes the service answers, which the client reads differently.
            const writtenFor = (storedEmailId: string) => {
                const outcome = outcomes[storedEmailId] ?? 'recorded';
                const record =
                    outcome === 'recorded' ? { recordId: `record-${storedEmailId}`, state: 'pending' } : null;

                return asked.changes === undefined
                    ? { storedEmailId, outcome, change: record }
                    : { storedEmailId, outcome, changes: record === null ? [] : [record] };
            };

            return Promise.resolve({
                status,
                body: JSON.stringify({
                    results: [...(asked.changes ?? []), ...(asked.moves ?? []), ...(asked.deletes ?? [])].map(
                        ({ storedEmailId }) => writtenFor(storedEmailId),
                    ),
                }),
                headers: {},
            });
        },
    };
}

/**
 * The same deployment with its answers to submissions held back from `hold` on until `answer`, which is how an answer
 * is made to arrive after somebody else has signed in.
 */
function answeringLate(deployment: Deployment): {
    readonly deployment: Deployment;
    readonly hold: () => void;
    readonly answer: () => void;
} {
    let holding = false;
    const waiting: (() => void)[] = [];

    return {
        deployment: {
            requests: deployment.requests,
            transport: (request) =>
                holding && request.path.includes('/mutations/')
                    ? new Promise((resolve) => {
                          waiting.push(() => {
                              resolve(deployment.transport(request));
                          });
                      })
                    : deployment.transport(request),
        },
        hold: () => {
            holding = true;
        },
        answer: () => {
            for (const release of waiting.splice(0)) {
                release();
            }
        },
    };
}

/** Somebody else, signed in on the same tab, which is a different credential handed to providers that stay mounted. */
const someoneElse: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic b3RoZXI=' };

/**
 * The provider standing over a hook that reads it, and the way to sign somebody else in on the same tab.
 *
 * `held` answers what the hook last published, and it is published by an effect — after the commit that drew whatever
 * the same answer put on the screen. So a toast on the screen says nothing yet about what `held` answers, however
 * surely the provider wrote the two in one render, and a case that awaited the one waits for the other.
 */
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

function acting(
    deployment: Deployment,
    {
        flags = true,
        moves = true,
        deletes = true,
        composes = false,
        telemetry = noTelemetry,
        marking = nothingMarkedRead,
        status = 'online',
        listed = nothingListed,
    }: {
        flags?: boolean;
        moves?: boolean;
        deletes?: boolean;
        composes?: boolean;
        telemetry?: ClientTelemetry;
        marking?: ReadMarking;
        status?: 'online' | 'disconnected';
        listed?: ListedMail;
    } = {},
): {
    readonly held: () => MailboxActs;
    readonly signIn: (next: ClientSession) => void;
    readonly renew: (next: ClientSession) => void;
} {
    let signedIn = session;
    let person = session.authorization;

    function Surrounded({ children }: { readonly children: ReactNode }) {
        return (
            <LocalizationProvider>
                <TelemetryContext value={telemetry}>
                    <ReadMarkingContext value={marking}>
                        <ToastsProvider>
                            <ConnectionStatusContext value={statusAt(status)}>
                                <ListedMailContext value={listed}>
                                    <PendingChangesProvider signedInAs={person}>
                                        <MailboxActsProvider
                                            session={signedIn}
                                            signedInAs={person}
                                            transport={deployment.transport}
                                            online
                                            flags={flags}
                                            moves={moves}
                                            deletes={deletes}
                                            composes={composes}
                                        >
                                            {children}
                                        </MailboxActsProvider>
                                    </PendingChangesProvider>
                                </ListedMailContext>
                            </ConnectionStatusContext>
                        </ToastsProvider>
                    </ReadMarkingContext>
                </TelemetryContext>
            </LocalizationProvider>
        );
    }

    const drawn = renderHook(() => useMailboxActs(), { wrapper: Surrounded });

    return {
        held: () => drawn.result.current,
        signIn: (next) => {
            signedIn = next;
            person = next.authorization;
            drawn.rerender();
        },
        renew: (next) => {
            signedIn = next;
            drawn.rerender();
        },
    };
}

/**
 * What was submitted to a mutation route, which is the whole of what the deployment was asked to write down.
 *
 * Every act carries the key it is repeated under, which is asserted here and left out of the body each test compares:
 * the key is generated, and what a test states is what the act asked for. A release names records rather than mail
 * and takes none.
 */
function submitted(deployment: Deployment): { readonly path: string; readonly body: unknown }[] {
    return deployment.requests
        .filter((request) => request.path.includes('/mutations/'))
        .map((request) => {
            const { requestId, ...body } = JSON.parse(request.body ?? '{}') as { requestId?: string };

            if (!request.path.endsWith('/releases')) {
                expect(requestId).toMatch(/\S/u);
            }

            return { path: request.path, body };
        });
}

function perform(held: () => MailboxActs, ...asked: Parameters<MailboxActs['perform']>): void {
    act(() => {
        held().perform(...asked);
    });
}

/** Moves the fake clock on, letting every answer already on its way land first. */
async function pass(milliseconds: number): Promise<void> {
    await act(async () => {
        await vi.advanceTimersByTimeAsync(milliseconds);
    });
}

/** A folder chosen in the move dialog, which is what asking a move again has to name a second time. */
const chosen: MoveDestination = { folderId: 'work-archive', name: 'Archive', role: 'Archive' };

afterEach(() => {
    vi.useRealTimers();
});

/** More messages in the trash than one call may name, which makes deleting them two submissions rather than one. */
const heapedInTrash: ActedMessage[] = Array.from({ length: mostMessagesPerMutation + 1 }, (_, at) => ({
    storedEmailId: `message-${String(at)}`,
    account: 'work',
    folder: 'work-trash',
    unread: false,
    flagged: false,
    spam: null,
}));

/** The records each call to a delete's release route named, one list per call. */
function recordIdsReleased(deployment: Deployment): string[][] {
    return submitted(deployment)
        .filter(({ path }) => path.endsWith('/mutations/deletes/releases'))
        .map(({ body }) => (body as { recordIds: string[] }).recordIds);
}

/**
 * What the toast surface is holding, which is how a case asserts that an act said nothing.
 *
 * The surface is in the document from the first paint and empty, so what says nothing was reported is that it holds no
 * card — never that the region is absent.
 */
function said(): readonly HTMLElement[] {
    return within(screen.getByRole('list', { name: 'Notices' })).queryAllByRole('listitem');
}

describe('MailboxActsProvider', () => {
    it('stops carrying once a drag ends, which the window hears while the dragged row is still drawn', () => {
        const { held } = acting(deploymentAnswering());

        act(() => {
            held().carry([invoice, receipt]);
        });

        expect(held().carried).toStrictEqual([invoice, receipt]);

        fireEvent.dragEnd(window);

        expect(held().carried).toStrictEqual([]);
    });

    it('stops carrying at the first pointer movement after a drag whose row left the document before it ended', () => {
        const { held } = acting(deploymentAnswering());

        act(() => {
            held().carry([invoice]);
        });

        fireEvent.pointerMove(window);

        expect(held().carried).toStrictEqual([]);
    });

    it('asks a deployment to leave a flag where the act puts it, and offers no way back from a flag', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)).toStrictEqual([
                {
                    path: 'https://mail.example.invalid/api/client/mutations/flags',
                    body: { changes: [{ storedEmailId: 'message-1', flags: { flagged: true } }] },
                },
            ]);
        });

        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
    });

    it('asks a deployment to take a flag off, which is the other direction of the same change', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'unflag', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)).toStrictEqual([
                {
                    path: 'https://mail.example.invalid/api/client/mutations/flags',
                    body: { changes: [{ storedEmailId: 'message-1', flags: { flagged: false } }] },
                },
            ]);
        });

        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
    });

    it('marks unread by writing that flag alone, so a message that was starred stays starred', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'markUnread', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)[0]?.body).toStrictEqual({
                changes: [{ storedEmailId: 'message-1', flags: { seen: false } }],
            });
        });
    });

    it('marks read by writing that flag alone, which is the other direction of the same change', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'markRead', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)[0]?.body).toStrictEqual({
                changes: [{ storedEmailId: 'message-1', flags: { seen: true } }],
            });
        });
    });

    // What an act that writes a flag does is the mark the row draws from the press, so there is nothing left for a card
    // in the corner to tell anybody. The three that file a message elsewhere report, because the message has left the
    // screen it was on and the toast is the only place the way back is offered.
    it.each<{ named: string; asked: MailboxAct }>([
        { named: 'flagging', asked: 'flag' },
        { named: 'taking a flag off', asked: 'unflag' },
        { named: 'marking unread', asked: 'markUnread' },
        { named: 'marking read', asked: 'markRead' },
    ])('says nothing in the corner about $named, the row having drawn the mark already', async ({ asked }) => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, asked, [invoice]);

        await waitFor(() => {
            expect(submitted(deployment).length).toBe(1);
        });
        await act(async () => {
            await Promise.resolve();
        });

        expect(said()).toStrictEqual([]);
    });

    it('archives into the folder the account labels as its archive, and says how many went', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });

        perform(held, 'archive', [invoice, receipt]);

        await screen.findByText('Archived');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/moves',
            body: {
                moves: [
                    { storedEmailId: 'message-1', destinationFolder: 'work-archive' },
                    { storedEmailId: 'message-2', destinationFolder: 'work-archive' },
                ],
            },
        });
        expect(screen.getByText('2 messages')).toBeDefined();
    });

    it('files a delete in the trash folder while the message is somewhere else, and offers the way back', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('delete', [invoice])).toBeNull();
        });

        perform(held, 'delete', [invoice]);

        await screen.findByText('Moved to the trash');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/moves',
            body: { moves: [{ storedEmailId: 'message-1', destinationFolder: 'work-trash' }] },
        });
        expect(screen.getByRole('button', { name: 'Undo' })).toBeDefined();
    });

    // The way back from deleting a draft would file it out of the trash into the drafts folder, which the deployment
    // refuses, so the toast offers none rather than a control that can only fail.
    it('files a deleted draft in the trash and offers no way back', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('delete', [unsent])).toBeNull();
        });

        perform(held, 'delete', [unsent]);

        await screen.findByText('Moved to the trash');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/moves',
            body: { moves: [{ storedEmailId: 'message-4', destinationFolder: 'work-trash' }] },
        });
        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
    });

    // Deleting from the trash is said plainly and offered no way back, because the question before it already said
    // the messages cannot be recovered — an Undo after that would contradict the confirmation somebody just answered.
    it('deletes a message already in the trash, saying so plainly and offering no way back', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        await screen.findByText('Deleting…');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/deletes',
            body: { deletes: [{ storedEmailId: 'message-3' }] },
        });
        expect(screen.queryByRole('button', { name: 'Undo' })).toBeNull();
        await waitFor(() => {
            expect(held().asked.get('message-3')).toStrictEqual({
                act: 'delete',
                from: 'work-trash',
                leaves: true,
                destroys: true,
                awaiting: false,
            });
        });
    });

    // Nobody is offered the way back, so nothing is waited for: the deployment is told to carry the delete as soon as
    // it has written it down, rather than left to sit out a window nobody can use.
    it('asks the deployment to stop waiting as soon as the delete is written down', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        await waitFor(() => {
            expect(submitted(deployment)[1]).toStrictEqual({
                path: 'https://mail.example.invalid/api/client/mutations/deletes/releases',
                body: { recordIds: ['record-message-3'] },
            });
        });
    });

    it('lets go of a delete of more messages than one call may name, a batch at a time', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently(heapedInTrash)).toBe(true);
        });

        perform(held, 'delete', heapedInTrash);

        await waitFor(() => {
            expect(recordIdsReleased(deployment)).toHaveLength(2);
        });

        const posted = recordIdsReleased(deployment);

        expect(posted.map((batch) => batch.length).sort((left, right) => left - right)).toStrictEqual([
            1,
            mostMessagesPerMutation,
        ]);
        expect(new Set(posted.flat())).toStrictEqual(
            new Set(heapedInTrash.map(({ storedEmailId }) => `record-${storedEmailId}`)),
        );
    });

    // Filing into the trash is what deleting is, whichever control asked for it, so a move naming the trash reports,
    // animates, and offers the way back exactly as the delete does rather than as a move into some other folder.
    it('performs a move into the trash as the delete it is', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('delete', [invoice])).toBeNull();
        });

        perform(held, 'move', [invoice], { folderId: 'work-trash', name: 'Trash', role: 'Trash' });

        await screen.findByText('Moved to the trash');

        expect(submitted(deployment)[0]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/moves',
            body: { moves: [{ storedEmailId: 'message-1', destinationFolder: 'work-trash' }] },
        });
        expect(screen.getByRole('button', { name: 'Undo' })).toBeDefined();
        await waitFor(() => {
            expect(held().asked.get('message-1')?.act).toBe('delete');
        });
    });

    it('files a selection reaching outside the trash rather than destroying the part of it already there', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('delete', [invoice])).toBeNull();
        });

        perform(held, 'delete', [discarded, invoice]);

        await screen.findByText('Moved to the trash');

        expect(submitted(deployment)[0]?.path).toBe('https://mail.example.invalid/api/client/mutations/moves');
    });

    it('takes an archive back by filing each message where it was, rather than by unsaying the first record', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });

        perform(held, 'archive', [invoice]);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        await screen.findByText('Put back where it was');

        expect(submitted(deployment)[1]).toStrictEqual({
            path: 'https://mail.example.invalid/api/client/mutations/moves',
            body: { moves: [{ storedEmailId: 'message-1', destinationFolder: 'work-inbox' }] },
        });
    });

    // A way back nobody answered most likely landed, so it is sent again rather than asked afresh: under the key it
    // first went with, which the deployment answers with the first attempt's outcome.
    it('sends a way back nobody answered again, under the key it first went with', async () => {
        vi.useFakeTimers();

        const archived = deploymentAnswering();
        const unanswered = deploymentAnswering({}, 504);
        let answering = false;
        const deployment: Deployment = {
            requests: archived.requests,
            transport: (request) =>
                (request.body ?? '').includes('work-inbox') && !answering
                    ? unanswered.transport(request)
                    : archived.transport(request),
        };
        const { held } = acting(deployment);

        await pass(0);
        perform(held, 'archive', [invoice]);
        await pass(0);
        fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
        await pass(0);

        expect(screen.getByText('Not known whether this reached the server')).toBeDefined();

        answering = true;
        await pass(unansweredWriteRetryDelay);

        // The silent attempt was recorded by the deployment that did not answer, the repeat by the one that did.
        const ways = [...unanswered.requests, ...archived.requests].filter((request) =>
            (request.body ?? '').includes('work-inbox'),
        );

        expect(ways).toHaveLength(2);
        expect(ways[1]?.body).toBe(ways[0]?.body);
        expect(screen.getByText('Put back where it was')).toBeDefined();
        expect(held().asked.get('message-1')).toMatchObject({ act: 'move', from: 'work-archive', awaiting: false });
    });

    // The mailbox may have moved on between the act and the press, so the way back is read per message exactly as the
    // act is: a row whose reverse move was not written down is still on its way to where the act put it.
    it('counts only the messages the way back was written down for, and goes on claiming the rest', async () => {
        const archived = deploymentAnswering();
        const refusingTheWayBack = deploymentAnswering({ 'message-2': 'message-not-found' });

        // The archive is written down for both, and the reverse move — the one naming the folder they came from — is
        // refused for one of them, which is the mailbox having moved on between the press and the way back.
        const deployment: Deployment = {
            requests: archived.requests,
            transport: (request) =>
                (request.body ?? '').includes('work-inbox')
                    ? refusingTheWayBack.transport(request)
                    : archived.transport(request),
        };

        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });

        perform(held, 'archive', [invoice, receipt]);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        await screen.findByText('Put back where it was');
        await screen.findByText('One message was not put back.');

        expect(screen.getByText('That mail is no longer in the mailbox this server reads.')).toBeDefined();
        expect(screen.getByText('1 message')).toBeDefined();
        await waitFor(() => {
            expect(held().asked.get('message-2')?.act).toBe('archive');
        });
        expect(held().asked.get('message-1')).toStrictEqual({
            act: 'move',
            from: 'work-archive',
            leaves: true,
            destroys: false,
            awaiting: false,
        });
    });

    // A reader who opened the folder the act filed a message in is looking at a list the deployment last answered with
    // the message in it, so the way back takes it out of there from the press — as the act took it out of where it was —
    // rather than leaving it drawn there while it is drawn back where it came from.
    it('takes a message put back out of the folder the act filed it in, from the press', async () => {
        const { held } = acting(deploymentAnswering());

        await waitFor(() => {
            expect(held().refusalOf('delete', [invoice])).toBeNull();
        });

        perform(held, 'delete', [invoice]);

        fireEvent.click(await screen.findByRole('button', { name: 'Undo' }));

        expect(held().asked.get('message-1')).toStrictEqual({
            act: 'move',
            from: 'work-trash',
            leaves: true,
            destroys: false,
            awaiting: true,
        });
    });

    it('says a message is being acted on from the press, which is what a row draws while an account is unreachable', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        expect(held().asked.get('message-1')?.act).toBe('flag');

        await waitFor(() => {
            expect(submitted(deployment).length).toBe(1);
        });

        await waitFor(() => {
            expect(held().asked.get('message-1')?.act).toBe('flag');
        });
    });

    // Marking a message unread and then flagging it are two changes to two flags, and a row draws both: a flag asked
    // second that replaced the first claim would draw the message read again from a listing read before either.
    it('keeps a message’s claim on one flag when an act on the other flag is asked after it', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'markUnread', [invoice]);
        perform(held, 'flag', [invoice]);

        expect(held().asked.get('message-1')).toStrictEqual({
            act: 'flag',
            from: 'work-inbox',
            leaves: false,
            destroys: false,
            awaiting: true,
            beside: { act: 'markUnread', from: 'work-inbox', leaves: false, destroys: false, awaiting: true },
        });

        await waitFor(() => {
            expect(submitted(deployment).length).toBe(2);
        });
    });

    it('lets go of one flag’s claim alone where the deployment wrote the other one down', async () => {
        const writing = deploymentAnswering();
        const refusing = deploymentAnswering({ 'message-1': 'message-not-found' });

        // Writes the read flag down and refuses the other, which is the one arrangement that tells the two claims apart.
        const deployment: Deployment = {
            requests: writing.requests,
            transport: (request) =>
                request.body?.includes('"flagged"') === true ? refusing.transport(request) : writing.transport(request),
        };
        const { held } = acting(deployment);

        perform(held, 'markUnread', [invoice]);
        perform(held, 'flag', [invoice]);

        await waitFor(() => {
            expect(held().asked.get('message-1')).toStrictEqual({
                act: 'markUnread',
                from: 'work-inbox',
                leaves: false,
                destroys: false,
                awaiting: false,
            });
        });
    });

    // A flag asked, then taken off again before either landed: the refusal of the first answers for an act the second
    // already superseded, so it holds no claim to take back and the claim on the read flag stands untouched beside.
    it('keeps the claim on the other flag where an act a later one superseded is refused', async () => {
        const writing = deploymentAnswering();
        const refusing = deploymentAnswering({ 'message-1': 'message-not-found' });
        const deployment: Deployment = {
            requests: writing.requests,
            transport: (request) =>
                request.body?.includes('"flagged":true') === true
                    ? refusing.transport(request)
                    : writing.transport(request),
        };
        const { held } = acting(deployment);

        perform(held, 'markUnread', [invoice]);
        perform(held, 'flag', [invoice]);
        perform(held, 'unflag', [invoice]);

        await screen.findByText('One message was not flagged.');

        await waitFor(() => {
            expect(held().asked.get('message-1')).toStrictEqual({
                act: 'unflag',
                from: 'work-inbox',
                leaves: false,
                destroys: false,
                awaiting: false,
                beside: { act: 'markUnread', from: 'work-inbox', leaves: false, destroys: false, awaiting: false },
            });
        });
    });

    // ADR 0026: marking unread is the reader's own choice about a message, so it outranks the read this session drew
    // from opening it — and what is forgotten is exactly the messages the deployment wrote the change down for.
    it('forgets the read an opening drew for each message the deployment recorded as marked unread, and for no other', async () => {
        const forget = vi.fn();
        const deployment = deploymentAnswering({ 'message-2': 'message-not-found' });
        const { held } = acting(deployment, { marking: { ...nothingMarkedRead, forget } });

        perform(held, 'markUnread', [invoice, receipt]);

        await waitFor(() => {
            expect(forget).toHaveBeenCalledWith(['message-1']);
        });

        perform(held, 'flag', [invoice]);
        perform(held, 'markRead', [receipt]);

        await waitFor(() => {
            expect(submitted(deployment).length).toBe(3);
        });
        await screen.findByText('One message was not marked read.');

        expect(forget).toHaveBeenCalledTimes(1);
    });

    it('stops claiming a message the deployment answered for without writing anything down', async () => {
        const deployment = deploymentAnswering({ 'message-2': 'message-not-found' });
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice, receipt]);

        await screen.findByText('One message was not flagged.');

        await waitFor(() => {
            expect(held().asked.has('message-2')).toBe(false);
        });

        expect(held().asked.get('message-1')?.act).toBe('flag');
    });

    // An account the deployment holds alone answers `applied`: the change is already made, so the act stands exactly as a
    // recorded one does and nothing about it is a refusal.
    it('keeps claiming an archive the deployment applied without a record, says it went, and reports no refusal', async () => {
        const recording = recordingTelemetry();
        const deployment = deploymentAnswering({ 'message-1': 'applied' });
        const { held } = acting(deployment, { telemetry: recording.telemetry });

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });

        perform(held, 'archive', [invoice]);

        await screen.findByText('Archived');

        await waitFor(() => {
            expect(held().asked.get('message-1')?.act).toBe('archive');
        });
        expect(recordsOf(recording.recorded, 'act_refused')).toStrictEqual([]);
    });

    it('reports a delete in the trash the deployment applied as a move into the trash, never as a permanent delete', async () => {
        const deployment = deploymentAnswering({ 'message-3': 'applied' });
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().deletesPermanently([discarded])).toBe(true);
        });

        perform(held, 'delete', [discarded]);

        await screen.findByText('Moved to the trash');

        expect(screen.queryByText('Deleting…')).toBeNull();
        await waitFor(() => {
            expect(held().asked.has('message-3')).toBe(false);
        });
    });

    it('says an act the server refused changed nothing, and claims nothing about the message', async () => {
        const deployment = deploymentAnswering({}, 403);
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        await screen.findByText('The server refused this change.');

        await waitFor(() => {
            expect(held().asked.has('message-1')).toBe(false);
        });
    });

    // A gateway answering for the server may have handed the request on first, so a `504` on a write settles nothing:
    // the act most likely landed, and what it drew stays drawn until the server says otherwise.
    it('says an act a gateway answered for is of unknown outcome, and keeps drawing it', async () => {
        const deployment = deploymentAnswering({}, 504);
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);

        await screen.findByText('Not known whether this reached the server');

        expect(held().asked.get('message-1')?.act).toBe('flag');
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });

    // The grant's refusal is for good and the connection's is for now, so the first is what is said: an act this
    // credential may never take is not drawn back in, worded as a passing loss of the server.
    it('keeps the grant’s own refusal while the server is out of reach', async () => {
        const { held } = acting(deploymentAnswering(), { status: 'disconnected', moves: false, deletes: false });

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBe('notOffered');
        });
        expect(held().refusalOf('flag', [invoice])).toBe('noConnection');
    });

    it('refuses every act before it is pressed while the server is out of reach', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment, { status: 'disconnected' });

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBe('noConnection');
        });
        expect(held().refusalOf('flag', [invoice])).toBe('noConnection');

        perform(held, 'flag', [invoice]);

        expect(deployment.requests.filter((request) => request.path.includes('/mutations'))).toStrictEqual([]);
    });

    // A submission over the bound is several batches and they do not answer together. What one of them was written
    // down for is written down, however the batch beside it ended: a row cleared on the strength of somebody else's
    // failure is a client disagreeing with the mailbox about mail the deployment is holding.
    it('keeps what one batch wrote down when the batch beside it was never answered', async () => {
        const requests: ClientRequest[] = [];
        const spread = Array.from({ length: mostMessagesPerMutation + 1 }, (_, at) => ({
            storedEmailId: `message-${String(at)}`,
            account: 'work',
            folder: 'work-inbox',
            unread: false,
            flagged: false,
            spam: null,
        }));

        const deployment: Deployment = {
            requests,
            transport: (request) => {
                requests.push(request);

                if (request.path.endsWith('/folders')) {
                    return Promise.resolve({ status: 200, body: folders, headers: {} });
                }

                const asked = JSON.parse(request.body ?? '{}') as { changes?: { storedEmailId: string }[] };
                const changes = asked.changes ?? [];

                // The full batch is written down and the one message left over never gets an answer at all.
                return Promise.resolve(
                    changes.length === mostMessagesPerMutation
                        ? {
                              status: 200,
                              body: JSON.stringify({
                                  results: changes.map(({ storedEmailId }) => ({ storedEmailId, outcome: 'recorded' })),
                              }),
                              headers: {},
                          }
                        : { status: 503, body: '', headers: {} },
                );
            },
        };

        const { held } = acting(deployment);

        perform(held, 'flag', spread);

        await screen.findByText('Not known whether this reached the server');

        // The batch nobody answered most likely landed too, so its message goes on being drawn as asked.
        expect(held().asked.get(`message-${String(mostMessagesPerMutation)}`)?.act).toBe('flag');
        expect(held().asked.get('message-0')?.act).toBe('flag');
    });

    // What an operator reads to find a deployment refusing writes it accepted the request for, and the count is the
    // whole of the record: which messages is never written down.
    it('reports how many messages a deployment declined after answering for them', async () => {
        const recording = recordingTelemetry();
        const deployment = deploymentAnswering({ 'message-2': 'message-not-found' });
        const { held } = acting(deployment, { telemetry: recording.telemetry });

        perform(held, 'flag', [invoice, receipt]);

        await waitFor(() => {
            expect(recordsOf(recording.recorded, 'act_refused')).toStrictEqual([
                {
                    event: 'act_refused',
                    attributes: { 'mailfathom.client.act': 'flag', 'mailfathom.client.messages': 1 },
                },
            ]);
        });
    });

    // A batch that never reached the deployment leaves its messages unwritten exactly as a declined one does, so a
    // count taken over both would put every dropped connection into the record above and leave nothing in it that
    // means what it says. That half is `request_failed`, at the level a transport failure belongs to.
    it('counts nothing as refused from a batch that never reached the deployment', async () => {
        const recording = recordingTelemetry();
        const deployment = deploymentAnswering({}, 503);
        const { held } = acting(deployment, { telemetry: recording.telemetry });

        perform(held, 'flag', [invoice]);

        await screen.findByText('Not known whether this reached the server');

        expect(recordsOf(recording.recorded, 'act_asked')).toHaveLength(1);
        expect(recordsOf(recording.recorded, 'act_refused')).toStrictEqual([]);
    });

    // One sentence per outcome whichever surface asked: the reason is the deployment's answer and the same for every
    // act, and what did not happen is the act's own.
    it.each<{ act: MailboxAct; destination?: MoveDestination; said: string }>([
        { act: 'flag', said: 'One message was not flagged.' },
        { act: 'unflag', said: 'The flag was not removed from one message.' },
        { act: 'markUnread', said: 'One message was not marked unread.' },
        { act: 'archive', said: 'One message was not archived.' },
        { act: 'delete', said: 'One message was not deleted.' },
        { act: 'move', destination: chosen, said: 'One message was not filed.' },
    ])('words a refused $act the way the queue words every refusal', async ({ act, destination, said }) => {
        const { held } = acting(deploymentAnswering({ 'message-1': 'message-not-found' }));

        await waitFor(() => {
            expect(held().refusalOf(act, [invoice])).toBeNull();
        });

        perform(held, act, [invoice], destination);

        expect(await screen.findByText(said)).toBeDefined();
        expect(screen.getByText('That mail is no longer in the mailbox this server reads.')).toBeDefined();
    });

    // An act answers after the render that asked has gone, and somebody else may have signed in on the tab by then:
    // what the first person's act came to is neither the next person's to be told about nor their queue's to follow.
    it('tells nobody what an act came to when somebody else signed in before it answered', async () => {
        vi.useFakeTimers();

        const late = answeringLate(deploymentAnswering());
        const { held, signIn } = acting(late.deployment);

        await pass(0);
        late.hold();
        perform(held, 'archive', [invoice]);
        signIn(someoneElse);
        late.answer();
        await pass(0);

        expect(screen.queryByText('Archived')).toBeNull();
    });

    it('tells nobody a message was put back when somebody else signed in before the way back answered', async () => {
        vi.useFakeTimers();

        const late = answeringLate(deploymentAnswering());
        const { held, signIn } = acting(late.deployment);

        await pass(0);
        perform(held, 'archive', [invoice]);
        await pass(0);
        late.hold();
        fireEvent.click(screen.getByRole('button', { name: 'Undo' }));
        signIn(someoneElse);
        late.answer();
        await pass(0);

        expect(screen.queryByText('Put back where it was')).toBeNull();
    });

    // Until the deployment answers, pressing the act again would send the same request a second time. Once it has, the
    // change is made or refused, and what the control offers next is a new act.
    it('refuses the same act on a message while its request is unanswered, and no longer once it is answered', async () => {
        const late = answeringLate(deploymentAnswering());
        const { held } = acting(late.deployment);

        late.hold();
        perform(held, 'flag', [invoice]);

        expect(standsInTheWay(held(), 'flag', [invoice])).toBe('underway');

        late.answer();

        await waitFor(() => {
            expect(standsInTheWay(held(), 'flag', [invoice])).toBeNull();
        });
        expect(held().asked.get('message-1')).toMatchObject({ act: 'flag', awaiting: false });
    });

    // A write with no answer is held in this tab and sent again once the server answers, under the key it first went
    // with: the deployment answers a repeat under that key with the first attempt's outcome, so it lands once.
    it('sends a write nobody answered again under the key it first went with, and says so once', async () => {
        vi.useFakeTimers();

        const silent = deploymentAnswering({}, 504);
        const answering = deploymentAnswering();
        let back = false;
        const deployment: Deployment = {
            requests: answering.requests,
            transport: (request) =>
                request.path.includes('/mutations/') && !back
                    ? silent.transport(request)
                    : answering.transport(request),
        };
        const { held } = acting(deployment);

        await pass(0);
        perform(held, 'flag', [invoice]);
        await pass(0);

        expect(
            screen.getByText(
                'The connection went as this change was made. It stays shown as you made it and is confirmed once the connection is back. Closing this tab before then drops it.',
            ),
        ).toBeDefined();
        expect(standsInTheWay(held(), 'flag', [invoice])).toBe('underway');

        back = true;
        await pass(unansweredWriteRetryDelay);

        // The silent attempt was recorded by the deployment that did not answer, the repeat by the one that did.
        const sentAs = [...silent.requests, ...answering.requests]
            .filter((request) => request.path.includes('/mutations/flags'))
            .map((request) => (JSON.parse(request.body ?? '{}') as { requestId: string }).requestId);

        expect(sentAs).toHaveLength(2);
        expect(sentAs[1]).toBe(sentAs[0]);
        expect(screen.getAllByText('Not known whether this reached the server')).toHaveLength(1);
        expect(held().asked.get('message-1')).toMatchObject({ act: 'flag', awaiting: false });
    });

    // A renewal hands the same person a new credential while the write waits, and the repeat goes under that one: the
    // credential it first went with may already be refused.
    it('sends a write nobody answered again under the credential the same person holds by then', async () => {
        vi.useFakeTimers();

        const silent = deploymentAnswering({}, 504);
        const answering = deploymentAnswering();
        let back = false;
        const deployment: Deployment = {
            requests: answering.requests,
            transport: (request) =>
                request.path.includes('/mutations/') && !back
                    ? silent.transport(request)
                    : answering.transport(request),
        };
        const { held, renew } = acting(deployment);
        const renewed: ClientSession = { ...session, authorization: 'Bearer cmVuZXdlZA==' };

        await pass(0);
        perform(held, 'flag', [invoice]);
        await pass(0);
        renew(renewed);

        back = true;
        await pass(unansweredWriteRetryDelay);

        const first = silent.requests.filter((request) => request.path.includes('/mutations/flags'));
        const repeat = answering.requests.filter((request) => request.path.includes('/mutations/flags'));
        const keyOf = (request: ClientRequest | undefined) =>
            (JSON.parse(request?.body ?? '{}') as { requestId?: string }).requestId;

        expect(repeat).toHaveLength(1);
        expect(repeat[0]?.headers['Authorization']).toBe('Bearer cmVuZXdlZA==');
        expect(keyOf(repeat[0])).toBe(keyOf(first[0]));
        expect(held().asked.get('message-1')).toMatchObject({ act: 'flag', awaiting: false });
    });

    // Asking again after that is a new act of the same person's, so it goes under the renewed credential too and is
    // answered as theirs: an answer read as somebody else's would leave the act under way for the rest of the tab.
    it('asks again under the renewed credential after a repeat the server refused, and settles the answer', async () => {
        vi.useFakeTimers();

        const silent = deploymentAnswering({}, 504);
        const refusing = deploymentAnswering({}, 403);
        const answering = deploymentAnswering();
        let phase: 'silent' | 'refusing' | 'answering' = 'silent';
        const deployment: Deployment = {
            requests: answering.requests,
            transport: (request) =>
                !request.path.includes('/mutations/') || phase === 'answering'
                    ? answering.transport(request)
                    : phase === 'silent'
                      ? silent.transport(request)
                      : refusing.transport(request),
        };
        const { held, renew } = acting(deployment);
        const renewed: ClientSession = { ...session, authorization: 'Bearer cmVuZXdlZA==' };

        await pass(0);
        perform(held, 'flag', [invoice]);
        await pass(0);
        renew(renewed);

        phase = 'refusing';
        await pass(unansweredWriteRetryDelay);

        phase = 'answering';
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));
        await pass(0);

        const asked = answering.requests.filter((request) => request.path.includes('/mutations/flags'));

        expect(asked).toHaveLength(1);
        expect(asked[0]?.headers['Authorization']).toBe('Bearer cmVuZXdlZA==');
        expect(held().asked.get('message-1')).toMatchObject({ act: 'flag', awaiting: false });
    });

    it('gives every act a key of its own, so a second press is a second change rather than a repeat of the first', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        perform(held, 'flag', [invoice]);
        await waitFor(() => {
            expect(held().asked.get('message-1')?.awaiting).toBe(false);
        });
        perform(held, 'unflag', [invoice]);

        await waitFor(() => {
            expect(submitted(deployment)).toHaveLength(2);
        });

        const keys = deployment.requests
            .filter((request) => request.path.includes('/mutations/'))
            .map((request) => (JSON.parse(request.body ?? '{}') as { requestId: string }).requestId);

        expect(new Set(keys).size).toBe(2);
    });

    // A change the deployment made is in every listing read after it, so that listing is what the server holds — the
    // change, or the change undone once the mail server refused it. A page asked for before the answer says neither.
    it('lets a change the deployment made go at the first page read after its answer, so a change undone is drawn undone', async () => {
        const observers: ((emails: readonly DrawnMail[], askedAt: number) => void)[] = [];
        const listed: ListedMail = {
            ...nothingListed,
            observe: (observer) => {
                observers.push(observer);

                return () => undefined;
            },
        };
        const { held } = acting(deploymentAnswering({ 'message-1': 'applied' }), { listed });
        const drawn: DrawnMail = {
            id: 'message-1',
            account: 'work',
            folder: 'work-inbox',
            unread: false,
            flagged: false,
            spam: null,
        };

        perform(held, 'flag', [invoice]);

        await waitFor(() => {
            expect(held().asked.get('message-1')?.awaiting).toBe(false);
        });

        function draw(askedAt: number): void {
            act(() => {
                for (const observer of observers) {
                    observer([drawn], askedAt);
                }
            });
        }

        draw(Date.now() - 60_000);

        expect(held().asked.get('message-1')?.act).toBe('flag');

        draw(Date.now() + 1);

        expect(held().asked.has('message-1')).toBe(false);
    });

    // A change only recorded reaches no listing until the mailbox converges, so a page read after its answer still
    // shows the message as it was, and the claim is what keeps the person's own act on the screen.
    it('keeps claiming a change only recorded whatever a page read after its answer draws', async () => {
        const observers: ((emails: readonly DrawnMail[], askedAt: number) => void)[] = [];
        const listed: ListedMail = {
            ...nothingListed,
            observe: (observer) => {
                observers.push(observer);

                return () => undefined;
            },
        };
        const { held } = acting(deploymentAnswering(), { listed });

        perform(held, 'flag', [invoice]);

        await waitFor(() => {
            expect(held().asked.get('message-1')?.awaiting).toBe(false);
        });

        act(() => {
            for (const observer of observers) {
                observer(
                    [
                        {
                            id: 'message-1',
                            account: 'work',
                            folder: 'work-inbox',
                            unread: false,
                            flagged: false,
                            spam: null,
                        },
                    ],
                    Date.now() + 1,
                );
            }
        });

        expect(held().asked.get('message-1')?.act).toBe('flag');
    });

    it('asks a deployment for nothing where the credential may not write what the act writes', () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment, { flags: false });

        perform(held, 'flag', [invoice]);

        expect(submitted(deployment)).toStrictEqual([]);
    });

    it('says the folders were not read and offers the read again, rather than refusing as if the account had none', async () => {
        const deployment = deploymentAnswering();
        let answered = 503;

        const failing: Deployment = {
            requests: deployment.requests,
            transport: (request) => {
                if (request.path.endsWith('/folders') && answered !== 200) {
                    deployment.requests.push(request);

                    return Promise.resolve({ status: answered, body: '', headers: {} });
                }

                return deployment.transport(request);
            },
        };

        const { held } = acting(failing);

        await screen.findByText('Your folders were not read: unavailable.');

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBe('foldersUnknown');
        });

        answered = 200;
        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        await waitFor(() => {
            expect(held().refusalOf('archive', [invoice])).toBeNull();
        });
    });

    // What a folder is *for* is the account's own label rather than anything about its name, and it is the same
    // reading `archive` and `delete` are resolved through — which is why the answer comes from here rather than from
    // a second reader beside the one screen that asks.
    it('names what the account labels the folder a message is in, which is how a drafts folder is recognised', async () => {
        const { held } = acting(deploymentAnswering());

        await waitFor(() => {
            expect(held().folderRoleOf(invoice)).toBe('Inbox');
        });
    });

    it('names none for a folder the account never described, rather than guessing one', async () => {
        const { held } = acting(deploymentAnswering());

        await waitFor(() => {
            expect(held().folderRoleOf(invoice)).toBe('Inbox');
        });

        expect(held().folderRoleOf({ ...invoice, folder: 'work-clients' })).toBeNull();
        expect(held().folderRoleOf({ ...invoice, account: 'nobody' })).toBeNull();
    });

    // A session that may neither file mail nor delete it reads no folders at all, so nothing here says what any of
    // them is for.
    it('names none where the folders were never read, which is a session that files and deletes nothing', () => {
        const { held } = acting(deploymentAnswering(), { moves: false, deletes: false });

        expect(held().folderRoleOf(invoice)).toBeNull();
    });

    // Writing a draft is not an act on the mailbox, so it offers none of them — but a draft is opened where it was
    // written only if the list can tell it is one, and that is the account's own label on the folder it stands in.
    it('names the folder a message is in for a credential that writes drafts and acts on no mail', async () => {
        const { held } = acting(deploymentAnswering(), { flags: false, moves: false, deletes: false, composes: true });

        await waitFor(() => {
            expect(held().folderRoleOf(invoice)).toBe('Inbox');
        });

        expect(held().refusalOf('archive', [invoice])).toBe('notOffered');
    });

    it('reads no folders for a credential that may neither mark spam, file mail, nor delete it, every act being refused', () => {
        const deployment = deploymentAnswering();

        acting(deployment, { flags: false, moves: false, deletes: false });

        expect(deployment.requests).toStrictEqual([]);
    });

    it('reads the folders for a credential that may only delete, which is what says where the trash is', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment, { moves: false });

        await waitFor(() => {
            expect(held().refusalOf('delete', [discarded])).toBeNull();
        });

        expect(held().refusalOf('delete', [invoice])).toBe('notOffered');
    });

    // Marking spam writes a flag-shaped mark, and *Not spam* asks whether a message sits in the Spam folder — which only
    // the folders say.
    it('reads the folders for a credential that may only mark, which is what says where the Spam folder is', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment, { moves: false, deletes: false });

        await waitFor(() => {
            expect(held().folderRoleOf(invoice)).toBe('Inbox');
        });

        expect(deployment.requests.some((request) => request.path.endsWith('/folders'))).toBe(true);
    });

    // A move into the Spam folder is the same act as pressing *Spam*, so it is drawn spam from the press and asks for
    // the kind of spam it is.
    it('draws a message moved into the Spam folder as spam and asks what kind of spam it is', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().folderRoleOf(invoice)).toBe('Inbox');
        });

        perform(held, 'move', [invoice], { folderId: 'work-junk', name: 'Junk', role: 'Junk' });

        expect(await screen.findByRole('dialog', { name: 'Moved to Spam' })).toBeDefined();
        expect(held().spamMarked.get('message-1')?.verdict.verdict).toBe('Spam');
    });

    it('asks no kind of spam for a message moved into an ordinary folder', async () => {
        const deployment = deploymentAnswering();
        const { held } = acting(deployment);

        await waitFor(() => {
            expect(held().folderRoleOf(invoice)).toBe('Inbox');
        });

        perform(held, 'move', [invoice], chosen);

        await waitFor(() => {
            expect(submitted(deployment)).toHaveLength(1);
        });
        expect(screen.queryByRole('dialog', { name: 'Moved to Spam' })).toBeNull();
        expect(held().spamMarked.size).toBe(0);
    });
});
