// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientFailure, MailMutationOutcome, MailMutationResult } from '@mailfathom/client-backend';

// What one submission of a change this client asked for is, and what its answer says. The deployment answers a
// mailbox act with the change already made in what MailFathom holds, so an answer is the whole of what a change is
// owed here: nothing is followed afterwards. What reaches the mail server later is the account freshness line's to say,
// and a refusal the mail server makes afterwards arrives as a notification.

/**
 * What the client asked a mailbox for, named for the person rather than for the record behind it.
 *
 * One member per act a person performs, each with its own sentences beside it rather than a free-form label a screen
 * would have to guess the wording for. Marking read is the act opening a message performs; the rest are the acts of the
 * Mail space, and `putBack` is the reverse move a filing act offers as its way back — a move of its own, named apart
 * because what did not happen to a message somebody tried to put back is not that it was not filed.
 */
export type ChangeAct = 'markRead' | 'flag' | 'unflag' | 'markUnread' | 'archive' | 'delete' | 'move' | 'putBack';

/**
 * What one submission of one act became, which is the whole of what a producer hands the queue.
 *
 * The callbacks are the producer's because only it knows what its own act means: asking again is that act submitted
 * afresh under a new key, sending again is the same request under the key it first went with, and letting go is
 * whatever the screen has to stop claiming.
 */
export interface ChangeSubmission {
    readonly act: ChangeAct;

    /** The messages the submission named. */
    readonly asked: readonly string[];

    /** What the deployment answered per message, or `null` where it answered nothing this client could read as one. */
    readonly results: readonly MailMutationResult[] | null;

    /**
     * Why there are no results: the refusal the server stated, or `null` where nothing answered at all.
     *
     * The two are different sentences. A refusal is an answer, so nothing was written down and the way back is to ask
     * again. Silence — and a gateway's `502`, `503`, or `504`, which may come after the request was handed on — leaves
     * the change's outcome unknown: it most likely landed, so it is sent again under its own key, which lands it once.
     */
    readonly failure: ClientFailure | null;

    /** Whether this is the answer to sending a submission nobody answered again, which says nothing a second time. */
    readonly resent: boolean;

    /** Asks for the same change afresh over the messages named, under a new key: a new act rather than a repeat. */
    readonly askAgain: (storedEmailIds: readonly string[]) => void;

    /**
     * Sends exactly the request nobody answered again, under the key it first went with, and hands its answer back
     * as a submission of its own.
     */
    readonly sendAgain: () => void;

    /** Stops claiming the change over the messages named, which is what a refusal has to leave behind. */
    readonly letGo: (storedEmailIds: readonly string[]) => void;
}

/**
 * The messages the deployment refused, by what it refused them with.
 *
 * Grouped rather than listed, because what a person is owed is one sentence per reason naming how many messages it
 * happened to, rather than one card per message saying the same thing several times. A message answered `recorded` or
 * `applied` is not among them: the deployment holds the change.
 */
export function refusalsIn(
    results: readonly MailMutationResult[],
): ReadonlyMap<MailMutationOutcome, readonly string[]> {
    const refused = new Map<MailMutationOutcome, string[]>();

    for (const result of results) {
        if (result.outcome !== 'recorded' && result.outcome !== 'applied') {
            refused.set(result.outcome, [...(refused.get(result.outcome) ?? []), result.storedEmailId]);
        }
    }

    return refused;
}
