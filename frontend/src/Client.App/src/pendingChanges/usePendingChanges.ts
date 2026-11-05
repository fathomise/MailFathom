// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type { AbandonedAct } from '../notifications/failures';
import type { ChangeSubmission } from './changeSubmission';

// What becomes of the changes this client asks its mailbox for, and the changes the deployment undid that wait on the
// person. It belongs to the whole application rather than to a screen for the reason `readMarking/useReadMarking.ts`
// gives about its own: what asks for a change and what says what became of it are different parts of the frame, and
// neither is above the other.
//
// The context and its hook sit apart from the provider that fills them for the reason `workspace/useWorkspace.ts`
// gives: a module Vite hot-reloads may export components alone.

/**
 * How long a write nobody answered waits, once the server answers, before it is sent again under its own key.
 *
 * A pause rather than at once, because a write can go unanswered while the server answers everything else — a gateway
 * that dropped one request — and sending it again in the same instant would only meet the same gateway. The key makes
 * every repeat land once, so what the pause bounds is the load, never the outcome.
 */
export const unansweredWriteRetryDelay = 5_000;

/**
 * A change the deployment undid after the mail server refused it, known from the notification it raised.
 *
 * It is what every refusal comes back as, whichever tab or device asked for it: the record has nothing left to follow,
 * so it is a question for the person and nothing else, and it stands until they answer it.
 */
export interface AbandonedChange {
    readonly recordId: string;
    readonly storedEmailId: string;
    readonly act: AbandonedAct;

    /** Whether the mail server refused it for good, so asking again would be refused the same way and is not offered. */
    readonly permanent: boolean;
}

/** What a person may do about a change the deployment undid. Both are acts; neither is a default. */
export type ChangeResolution = 'askAgain' | 'letGo';

export interface PendingChanges {
    /** How many messages a write nobody answered is held for, waiting to be sent again once the server answers. */
    readonly unanswered: number;

    /** The changes the deployment undid that still wait on the person. */
    readonly abandoned: readonly AbandonedChange[];

    /** Reads what one submission's answer says: what was refused is said, and what nobody answered is sent again. */
    readonly follow: (submission: ChangeSubmission) => void;

    /**
     * Records the person's answer to one change the deployment undid. Whoever asks it again performs the act itself,
     * which is `useChangeDecision.ts`'s; this marks it answered.
     */
    readonly settle: (recordId: string, resolution: ChangeResolution) => void;
}

/**
 * What a tree with no provider above it reads, which is a client that holds nothing.
 *
 * A default rather than a refusal, for the reason `readMarking/useReadMarking.ts` gives about the same shape: a client
 * with no session, or one whose credential may change no flag, really does hold nothing, and every screen below is
 * drawn the same way under either.
 */
export const nothingPending: PendingChanges = {
    unanswered: 0,
    abandoned: [],
    follow: () => undefined,
    settle: () => undefined,
};

export const PendingChangesContext = createContext<PendingChanges>(nothingPending);

export function usePendingChanges(): PendingChanges {
    return useContext(PendingChangesContext);
}
