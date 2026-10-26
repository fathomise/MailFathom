// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
    isSpam,
    mostMessagesPerMutation,
    type ClientRequest,
    type ClientResponse,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderDirectory,
    type MailFolderRole,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import type { ActedMessage, MarkedSpam } from '../mailboxActs/useMailboxActs';
import { ToastsProvider } from '../toasts/Toasts';
import { useSpamMarks } from './useSpamMarks';

// The hook is driven the way the spam control drives it — a press over a set of messages, with the questions it can
// stand in front of drawn beside it — and what is proven is what the deployment was asked for and what the person was
// told. The control below reads the way `SpamControl` does: *Not spam* while every message under it is drawn spam.

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const fromTheServer = {
    verdict: 'Spam',
    decidedBy: 'Deterministic',
    spamSubcategory: null,
    spamSubcategoryNamedBy: null,
    phishingRequest: null,
    impersonatedParty: null,
} as const;

const invoice: ActedMessage = {
    storedEmailId: 'message-1',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
    spam: null,
};

const lottery: ActedMessage = {
    storedEmailId: 'message-2',
    account: 'work',
    folder: 'work-junk',
    unread: false,
    flagged: false,
    spam: fromTheServer,
};

function directory(filesJunk: boolean): MailFolderDirectory {
    return {
        synchronizationEnabled: true,
        accounts: [
            {
                account: {
                    id: 'work',
                    displayName: 'Work',
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: null,
                    behind: false,
                    filesJunk,
                },
                folders: [],
                outbox: null,
            },
        ],
    };
}

const receipt: ActedMessage = { ...invoice, storedEmailId: 'message-3' };

const roles: Readonly<Record<string, MailFolderRole>> = { 'work-inbox': 'Inbox', 'work-junk': 'Junk' };

interface Deployment {
    readonly transport: MailFathomTransport;
    readonly requests: ClientRequest[];
}

/** A deployment answering every spam mark as `answer` says, recording what it was asked. */
function deploymentAnswering(answer: (storedEmailIds: readonly string[]) => ClientResponse): Deployment {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            const { storedEmailIds } = JSON.parse(request.body ?? '{}') as { storedEmailIds: string[] };

            return Promise.resolve(answer(storedEmailIds));
        },
    };
}

function answeredWith(outcome: string, moved = false): (storedEmailIds: readonly string[]) => ClientResponse {
    return (storedEmailIds) => ({
        status: 200,
        body: JSON.stringify({
            results: storedEmailIds.map((storedEmailId) => ({ storedEmailId, outcome, moved, changes: [] })),
        }),
        headers: {},
    });
}

const recording = () => deploymentAnswering(answeredWith('applied'));

/** How a message is drawn: the verdict over it, who decided it, and whether the mark takes it out of its folder. */
function drawnMark(message: ActedMessage, own: MarkedSpam | undefined): string {
    const verdict = own?.verdict ?? message.spam;
    const judged = verdict === null ? 'unjudged' : `${verdict.verdict} by ${verdict.decidedBy}`;

    return `${message.storedEmailId}: ${judged}${own?.leaves === true ? ', leaves' : ''}`;
}

function SpamToggle({
    messages,
    deployment,
    filesJunk,
    onMarked,
}: {
    readonly messages: readonly ActedMessage[];
    readonly deployment: Deployment;
    readonly filesJunk: boolean;
    readonly onMarked: () => void;
}) {
    const signedIn = useRef<ClientSession | null>(session);
    const spam = useSpamMarks({
        session,
        transport: deployment.transport,
        signedIn,
        directory: directory(filesJunk),
        folderRoleOf: (message) => roles[message.folder] ?? null,
        counted: (count) => `${String(count)} messages`,
    });
    const allSpam = messages.every((message) =>
        isSpam(spam.marked.get(message.storedEmailId)?.verdict ?? message.spam),
    );

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    spam.toggle(messages, onMarked);
                }}
            >
                {allSpam ? 'Not spam' : 'Mark spam'}
            </button>
            {spam.questions}
            <ul>
                {messages.map((message) => (
                    <li key={message.storedEmailId}>{drawnMark(message, spam.marked.get(message.storedEmailId))}</li>
                ))}
            </ul>
        </>
    );
}

function drawn(
    messages: readonly ActedMessage[],
    deployment: Deployment,
    filesJunk = false,
    onMarked: () => void = () => undefined,
): void {
    render(
        <LocalizationProvider>
            <ToastsProvider>
                <SpamToggle messages={messages} deployment={deployment} filesJunk={filesJunk} onMarked={onMarked} />
            </ToastsProvider>
        </LocalizationProvider>,
    );
}

function submitted(deployment: Deployment): unknown[] {
    return deployment.requests.map((request) => ({
        path: request.path,
        body: JSON.parse(request.body ?? '{}') as unknown,
    }));
}

/** Opens the chooser over the messages, picks the kind, and confirms it. */
async function markedSpam(kind: RegExp): Promise<void> {
    fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));

    const chooser = await screen.findByRole('dialog', { name: /as spam$/u });

    fireEvent.click(within(chooser).getByRole('radio', { name: kind }));
    fireEvent.click(within(chooser).getByRole('button', { name: 'Mark as spam' }));
}

describe('useSpamMarks', () => {
    it('refuses a selection mixing spam with other mail, and sends nothing', async () => {
        const deployment = recording();

        drawn([invoice, lottery], deployment);
        fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));

        await screen.findByText('The selection mixes spam with other mail. Select only one of the two.');
        expect(screen.getByText('Nothing was marked')).toBeDefined();
        expect(deployment.requests).toEqual([]);
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('asks for the kind before marking mail spam, and sends nothing until it is picked', async () => {
        const deployment = recording();

        drawn([invoice], deployment);
        fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));

        await screen.findByRole('dialog', { name: 'Mark 1 message as spam' });
        expect(deployment.requests).toEqual([]);
    });

    it('marks the messages spam of the kind picked on the spam route', async () => {
        const deployment = recording();

        drawn([invoice], deployment);
        await markedSpam(/^Scam/u);

        await waitFor(() => {
            expect(submitted(deployment)).toEqual([
                {
                    path: 'https://mail.example.invalid/api/client/mutations/spam',
                    body: { verdict: 'Spam', spamSubcategory: 'Scam', storedEmailIds: ['message-1'] },
                },
            ]);
        });
        expect(await screen.findByText('Marked as spam - 1 messages')).toBeDefined();
    });

    it('marks spam not spam at once where nothing would move it out of the Spam folder', async () => {
        const deployment = recording();

        drawn([lottery], deployment, false);
        fireEvent.click(screen.getByRole('button', { name: 'Not spam' }));

        await waitFor(() => {
            expect(submitted(deployment)).toEqual([
                {
                    path: 'https://mail.example.invalid/api/client/mutations/spam',
                    body: { verdict: 'NotSpam', spamSubcategory: null, storedEmailIds: ['message-2'] },
                },
            ]);
        });
        expect(await screen.findByText('Marked not spam - 1 messages')).toBeDefined();
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('asks before marking spam not spam where the account would move it to the Inbox', async () => {
        const deployment = recording();

        drawn([lottery], deployment, true);
        fireEvent.click(screen.getByRole('button', { name: 'Not spam' }));

        await screen.findByRole('dialog', { name: 'Mark this message not spam?' });
        expect(deployment.requests).toEqual([]);
    });

    it('draws the mark from the press, before the deployment has answered', async () => {
        const unanswered: Deployment = { requests: [], transport: () => new Promise(() => undefined) };

        drawn([invoice], unanswered);
        await markedSpam(/^Marketing/u);

        expect(await screen.findByRole('button', { name: 'Not spam' })).toBeDefined();
    });

    it.each([
        ['refused', () => ({ status: 403, body: '', headers: {} }), 'unauthorized'],
        ['no longer held', answeredWith('message-not-found'), '1 messages'],
    ] as const)('forgets a mark the deployment %s, and says nothing was marked', async (_, answer, said) => {
        drawn([invoice], deploymentAnswering(answer));
        await markedSpam(/^Phishing/u);

        await screen.findByText('Nothing was marked');
        expect(screen.getByText(said)).toBeDefined();
        await screen.findByRole('button', { name: 'Mark spam' });
        expect(screen.queryByText(/^Marked as spam/u)).toBeNull();
    });

    it('puts back the mark that stood before a press the deployment did not write', async () => {
        let asked = 0;
        const deployment = deploymentAnswering((storedEmailIds) => {
            asked += 1;

            return asked === 1 ? answeredWith('applied')(storedEmailIds) : { status: 503, body: '', headers: {} };
        });

        drawn([invoice], deployment);
        await markedSpam(/^Scam/u);
        fireEvent.click(await screen.findByRole('button', { name: 'Not spam' }));

        await screen.findByText('Nothing was marked');
        expect(await screen.findByRole('button', { name: 'Not spam' })).toBeDefined();
        expect(deployment.requests).toHaveLength(2);
    });

    it('says a spam mark moved the message, and draws it leaving its folder, where the deployment moved it', async () => {
        drawn([invoice], deploymentAnswering(answeredWith('applied', true)), true);
        await markedSpam(/^Scam/u);

        expect(await screen.findByText('Scam. It is in Spam now.')).toBeDefined();
        expect(screen.getByText('message-1: Spam by Person, leaves')).toBeDefined();
    });

    it('says a not-spam mark moved the message to the Inbox, and draws it leaving Spam, where the deployment moved it', async () => {
        drawn([lottery], deploymentAnswering(answeredWith('applied', true)), true);
        fireEvent.click(screen.getByRole('button', { name: 'Not spam' }));

        const question = await screen.findByRole('dialog', { name: 'Mark this message not spam?' });

        fireEvent.click(within(question).getByRole('button', { name: 'Not spam, move to Inbox' }));

        expect(await screen.findByText('It is in the Inbox now.')).toBeDefined();
        expect(screen.getByText('Marked not spam - 1 messages')).toBeDefined();
        expect(screen.getByText('message-2: NotSpam by Person, leaves')).toBeDefined();
    });

    it('says not every message was marked, and draws the unwritten ones as before, where the deployment wrote only some', async () => {
        drawn(
            [invoice, receipt],
            deploymentAnswering((storedEmailIds) => ({
                status: 200,
                body: JSON.stringify({
                    results: storedEmailIds.map((storedEmailId) => ({
                        storedEmailId,
                        outcome: storedEmailId === invoice.storedEmailId ? 'applied' : 'message-not-found',
                        moved: false,
                        changes: [],
                    })),
                }),
                headers: {},
            })),
        );
        await markedSpam(/^Scam/u);

        expect(await screen.findByText('Not every message was marked')).toBeDefined();
        expect(screen.getByText('Marked as spam - 1 messages')).toBeDefined();
        expect(screen.getByText('message-1: Spam by Person')).toBeDefined();
        expect(screen.getByText('message-3: unjudged')).toBeDefined();
    });

    it('sends a mark over more messages than one request carries in batches the deployment accepts', async () => {
        const deployment = recording();
        const many = Array.from({ length: mostMessagesPerMutation + 1 }, (_, at) => ({
            ...invoice,
            storedEmailId: `message-${String(at)}`,
        }));

        drawn(many, deployment);
        await markedSpam(/^Marketing/u);

        expect(await screen.findByText(`Marked as spam - ${String(many.length)} messages`)).toBeDefined();
        expect(
            deployment.requests.map(
                (request) => (JSON.parse(request.body ?? '{}') as { storedEmailIds: string[] }).storedEmailIds,
            ),
        ).toEqual([
            many.slice(0, mostMessagesPerMutation).map((message) => message.storedEmailId),
            [`message-${String(mostMessagesPerMutation)}`],
        ]);
    });

    it.each([
        ['refused', { status: 503, body: '', headers: {} }, 'Nothing was marked'],
        ['written', answeredWith('applied')([invoice.storedEmailId]), 'Marked as spam - 1 messages'],
    ] as const)(
        'keeps the newer mark where an older press is %s after it',
        async (_, olderAnswer: ClientResponse, olderSaid) => {
            let answerOlder: (response: ClientResponse) => void = () => undefined;
            const requests: ClientRequest[] = [];
            const deployment: Deployment = {
                requests,
                transport: (request) => {
                    requests.push(request);

                    // The first press is held so that the second is drawn and answered while it is still in flight.
                    return requests.length === 1
                        ? new Promise((resolve) => {
                              answerOlder = resolve;
                          })
                        : Promise.resolve(answeredWith('applied')([invoice.storedEmailId]));
                },
            };

            drawn([invoice], deployment);
            await markedSpam(/^Scam/u);
            fireEvent.click(await screen.findByRole('button', { name: 'Not spam' }));
            await screen.findByText('Marked not spam - 1 messages');

            answerOlder(olderAnswer);

            await screen.findByText(olderSaid);
            expect(screen.getByText('message-1: NotSpam by Person')).toBeDefined();
            expect(screen.getByRole('button', { name: 'Mark spam' })).toBeDefined();
        },
    );

    describe('says a mark was sent only once it was', () => {
        it('after the chooser confirms a kind', async () => {
            let marked = 0;

            drawn([invoice], recording(), false, () => {
                marked += 1;
            });
            fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));

            const chooser = await screen.findByRole('dialog', { name: 'Mark 1 message as spam' });

            expect(marked).toBe(0);

            fireEvent.click(within(chooser).getByRole('radio', { name: /^Scam/u }));
            fireEvent.click(within(chooser).getByRole('button', { name: 'Mark as spam' }));

            expect(marked).toBe(1);
        });

        it('never where the chooser is cancelled', async () => {
            let marked = 0;
            const deployment = recording();

            drawn([invoice], deployment, false, () => {
                marked += 1;
            });
            fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));
            fireEvent.click(
                within(await screen.findByRole('dialog', { name: 'Mark 1 message as spam' })).getByRole('button', {
                    name: 'Cancel',
                }),
            );

            await waitFor(() => {
                expect(screen.queryByRole('dialog')).toBeNull();
            });
            expect(marked).toBe(0);
            expect(deployment.requests).toEqual([]);
        });

        it('never where a selection mixing spam with other mail is refused', async () => {
            let marked = 0;

            drawn([invoice, lottery], recording(), false, () => {
                marked += 1;
            });
            fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));

            await screen.findByText('Nothing was marked');
            expect(marked).toBe(0);
        });

        it('at once where a not-spam mark moves nothing and is sent without asking', () => {
            let marked = 0;

            drawn([lottery], recording(), false, () => {
                marked += 1;
            });
            fireEvent.click(screen.getByRole('button', { name: 'Not spam' }));

            expect(marked).toBe(1);
        });

        it('after the not-spam confirmation where the mark moves the message', async () => {
            let marked = 0;

            drawn([lottery], recording(), true, () => {
                marked += 1;
            });
            fireEvent.click(screen.getByRole('button', { name: 'Not spam' }));

            const question = await screen.findByRole('dialog', { name: 'Mark this message not spam?' });

            expect(marked).toBe(0);

            fireEvent.click(within(question).getByRole('button', { name: 'Not spam, move to Inbox' }));

            await waitFor(() => {
                expect(marked).toBe(1);
            });
        });
    });
});
