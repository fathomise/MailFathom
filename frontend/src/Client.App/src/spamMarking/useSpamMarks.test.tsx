// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import {
    isSpam,
    type ClientRequest,
    type ClientResponse,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderDirectory,
    type MailFolderRole,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import type { ActedMessage } from '../mailboxActs/useMailboxActs';
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

function SpamToggle({
    messages,
    deployment,
    filesJunk,
}: {
    readonly messages: readonly ActedMessage[];
    readonly deployment: Deployment;
    readonly filesJunk: boolean;
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
                    spam.toggle(messages);
                }}
            >
                {allSpam ? 'Not spam' : 'Mark spam'}
            </button>
            {spam.questions}
        </>
    );
}

function drawn(messages: readonly ActedMessage[], deployment: Deployment, filesJunk = false): void {
    render(
        <LocalizationProvider>
            <ToastsProvider>
                <SpamToggle messages={messages} deployment={deployment} filesJunk={filesJunk} />
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
});
