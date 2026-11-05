// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, renderHook, screen } from '@testing-library/react';
import type { ReactNode } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientFailure, ClientSession, MailMutationResult } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { ToastsProvider } from '../toasts/Toasts';
import type { ChangeSubmission } from './changeSubmission';
import { PendingChangesProvider } from './PendingChanges';
import {
    unansweredWriteRetryDelay,
    usePendingChanges,
    type AbandonedChange,
    type PendingChanges,
} from './usePendingChanges';

// The queue is driven the way a producer drives it — one submission handed in with its answer — because what is being
// proven is what somebody is told and what is sent again: which answers are silent, which are said out loud, and which
// are held to go out a second time under the same key. The clock is fake, so the wait before a repeat is asserted
// rather than sat through.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const somebodyElse: ClientSession = { ...session, authorization: 'Basic b3RoZXI=' };

const storedEmailId = '2f7d4f2a-6c1e-4e0a-9a2f-1b0c9d8e7f60';

const askedAgain: string[] = [];
const letGo: string[] = [];
let sentAgain = 0;

function submission(
    results: readonly MailMutationResult[] | null,
    named: readonly string[] = [storedEmailId],
    failure: ClientFailure | null = null,
    resent = false,
): ChangeSubmission {
    return {
        act: 'markRead',
        asked: named,
        results,
        failure,
        resent,
        askAgain: (storedEmailIds) => askedAgain.push(...storedEmailIds),
        sendAgain: () => {
            sentAgain += 1;
        },
        letGo: (storedEmailIds) => letGo.push(...storedEmailIds),
    };
}

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

// Read while the wrapper renders and written only between renders, which is what lets one mounted tree be handed a
// second credential or a lost server the way the frame hands them.
let signedInAs: ClientSession | null = session;
let statusHere: ConnectionStatus = statusAt('online');
let queue: { current: PendingChanges } | null = null;

function holding(
    signedIn: ClientSession | null = session,
    abandoned: readonly AbandonedChange[] = [],
    onDecided?: (recordId: string) => void,
): { signInAsSomebodyElse: () => void; statusBecomes: (level: 'online' | 'disconnected') => void } {
    signedInAs = signedIn;

    const view = renderHook(() => usePendingChanges(), {
        wrapper: ({ children }: { readonly children: ReactNode }) => (
            <LocalizationProvider>
                <ToastsProvider>
                    <ConnectionStatusContext value={statusHere}>
                        <PendingChangesProvider
                            session={signedInAs}
                            abandoned={abandoned}
                            {...(onDecided === undefined ? {} : { onDecided })}
                        >
                            {children}
                        </PendingChangesProvider>
                    </ConnectionStatusContext>
                </ToastsProvider>
            </LocalizationProvider>
        ),
    });

    queue = view.result;

    return {
        signInAsSomebodyElse: () => {
            signedInAs = somebodyElse;
            view.rerender();
        },
        statusBecomes: (level) => {
            statusHere = statusAt(level);
            view.rerender();
        },
    };
}

function hand(submitted: ChangeSubmission): void {
    act(() => {
        queue?.current.follow(submitted);
    });
}

async function pass(milliseconds: number): Promise<void> {
    await act(async () => {
        await vi.advanceTimersByTimeAsync(milliseconds);
    });
}

beforeEach(() => {
    vi.useFakeTimers();
    askedAgain.length = 0;
    letGo.length = 0;
    sentAgain = 0;
    statusHere = statusAt('online');
});

afterEach(() => {
    queue = null;
    vi.useRealTimers();
});

describe('PendingChangesProvider', () => {
    // The deployment answers with the change made, so nothing is left to say and nothing to follow.
    it.each(['recorded', 'applied'] as const)('says nothing about a change answered %s', (outcome) => {
        holding();

        hand(submission([{ storedEmailId, outcome, changes: [] }]));

        expect(screen.queryByRole('listitem')).toBeNull();
        expect(letGo).toStrictEqual([]);
        expect(queue?.current.unanswered).toBe(0);
    });

    it('says nothing about a message whose mailbox already agreed with the change', () => {
        holding();

        hand(submission([{ storedEmailId, outcome: 'already-in-destination', changes: [] }]));

        expect(screen.queryByText('One message was not marked read.')).toBeNull();
        expect(letGo).toStrictEqual([]);
    });

    it('says a message the deployment could not change was not changed, and stops the screen claiming it', () => {
        holding();

        hand(submission([{ storedEmailId, outcome: 'message-not-found', changes: [] }]));

        expect(screen.getByText('One message was not marked read.')).toBeTruthy();
        expect(screen.getByText('That mail is no longer in the mailbox this server reads.')).toBeTruthy();
        expect(letGo).toStrictEqual([storedEmailId]);
    });

    it('counts the messages one reason happened to rather than saying it once each', () => {
        holding();

        hand(
            submission([
                { storedEmailId: 'gone', outcome: 'message-not-found', changes: [] },
                { storedEmailId: 'also-gone', outcome: 'message-not-found', changes: [] },
            ]),
        );

        expect(screen.getByText('2 messages were not marked read.')).toBeTruthy();
    });

    it('says a refusal the server stated changed nothing, and offers it again as a new change', () => {
        holding();

        hand(submission(null, [storedEmailId], { reason: 'unauthorized', status: 403 }));

        expect(screen.getByText('The server refused this change.')).toBeTruthy();
        expect(letGo).toStrictEqual([storedEmailId]);

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(askedAgain).toStrictEqual([storedEmailId]);
        expect(sentAgain).toBe(0);
    });

    // Nothing answered, so the client cannot know the change did not land — and since an act commits on arrival it
    // most likely did. So it stays drawn, nothing offers to ask for it afresh, and it is held to be sent again.
    it('says a write nobody answered may have landed, keeps drawing it, offers nothing, and holds it', () => {
        holding();

        hand(submission(null));

        expect(screen.getByText('Not known whether this reached the server')).toBeTruthy();
        expect(
            screen.getByText(
                'The connection went as this change was made. It stays shown as you made it and is confirmed once the connection is back. Closing this tab before then drops it.',
            ),
        ).toBeTruthy();
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
        expect(letGo).toStrictEqual([]);
        expect(queue?.current.unanswered).toBe(1);
    });

    // A gateway may have handed the write on before it gave up, so its 504 settles nothing either.
    it('treats a gateway answering a write for the server as a write nobody answered', () => {
        holding();

        hand(submission(null, [storedEmailId], { reason: 'unavailable', status: 504 }));

        expect(screen.getByText('Not known whether this reached the server')).toBeTruthy();
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
        expect(queue?.current.unanswered).toBe(1);
    });

    it('sends a write nobody answered again once the wait has passed, and holds it no longer', async () => {
        holding();

        hand(submission(null));
        await pass(unansweredWriteRetryDelay - 1);

        expect(sentAgain).toBe(0);

        await pass(1);

        expect(sentAgain).toBe(1);
        expect(queue?.current.unanswered).toBe(0);
    });

    it('waits for a server out of reach to answer before sending anything again', async () => {
        const { statusBecomes } = holding();

        statusBecomes('disconnected');
        hand(submission(null));
        await pass(unansweredWriteRetryDelay * 3);

        expect(sentAgain).toBe(0);

        statusBecomes('online');
        await pass(unansweredWriteRetryDelay);

        expect(sentAgain).toBe(1);
    });

    // Every repeat that meets the same silence is held again, and only the first said anything.
    it('says nothing again when a write sent again meets the same silence', () => {
        holding();

        hand(submission(null, [storedEmailId], null, true));

        expect(screen.queryByText('Not known whether this reached the server')).toBeNull();
        expect(queue?.current.unanswered).toBe(1);
    });

    it('sends nothing of the last person’s under the next person’s credential', async () => {
        const { signInAsSomebodyElse } = holding();

        hand(submission(null));
        signInAsSomebodyElse();
        await pass(unansweredWriteRetryDelay);

        expect(queue?.current.unanswered).toBe(0);
        expect(sentAgain).toBe(0);
    });

    it('holds nothing at all where nobody is signed in', async () => {
        holding(null);

        hand(submission(null));
        await pass(unansweredWriteRetryDelay);

        expect(sentAgain).toBe(0);
    });

    it('carries the changes the deployment undid, and says when the person settles one', () => {
        const decided: string[] = [];
        const undone: AbandonedChange = {
            recordId: 'record-1',
            storedEmailId,
            act: 'move',
            permanent: false,
        };

        holding(session, [undone], (recordId) => decided.push(recordId));

        expect(queue?.current.abandoned).toStrictEqual([undone]);

        act(() => {
            queue?.current.settle('record-1', 'letGo');
        });

        expect(decided).toStrictEqual(['record-1']);
    });
});
