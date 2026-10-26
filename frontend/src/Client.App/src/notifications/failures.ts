// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientNotification } from '@mailfathom/client-backend';

// The three failures that land after the request asking for them had already answered: a send the mail server
// refused, a change to a mailbox MailFathom stopped trying to make, and a draft whose copy on the mail server stopped
// following it. Each is a notification with a cause and a target, and this is the one place the two are read together
// into the thing somebody has to decide about — so the row, its toast, where it leads, and what it offers cannot come
// to disagree about which failure a row is.

/**
 * What a change the deployment gave up on had asked for, by the deployment's own names for it.
 *
 * The deployment counts the act as the statement's number, in this order, and never renumbers it. It says *move* for
 * an archive as well, because archiving is a move into the folder an account files into rather than an act of its own
 * on the server — which is why nothing here can say *not archived* about a change it only knows from a notification.
 */
export type AbandonedAct = 'markRead' | 'markUnread' | 'flag' | 'unflag' | 'move' | 'delete' | 'copy' | 'changeTags';

const actsByCount: readonly AbandonedAct[] = [
    'markRead',
    'markUnread',
    'flag',
    'unflag',
    'move',
    'delete',
    'copy',
    'changeTags',
];

/** One failure, as the thing a person decides about rather than as the row it arrived on. */
export type Failure =
    | { readonly kind: 'send'; readonly outgoingEmailId: string }
    | {
          readonly kind: 'change';
          readonly recordId: string;
          readonly storedEmailId: string;
          readonly act: AbandonedAct;
      }
    | { readonly kind: 'draft'; readonly draftId: string };

/**
 * What a row reporting a failure offers to decide: a refused send is sent again or taken back to edit, and a change the
 * mailbox did not take is asked again — moved again, for a move, which asks for the folder once more — or let go.
 */
export type FailureDecision = 'sendAgain' | 'edit' | 'askAgain' | 'moveAgain' | 'letGo';

/** The act a given-up change's statement counts, or `null` for a count this build does not name. */
export function abandonedActOf(counted: number | null): AbandonedAct | null {
    return counted === null ? null : (actsByCount[counted] ?? null);
}

/**
 * Which failure a notification is about, or `null` for every notification that reports none.
 *
 * Both halves have to agree: a cause naming a refused send over a target that is not a send is a row this client
 * draws in the service's own words and offers nothing on, rather than one it guesses a decision for.
 */
export function failureOf(notification: ClientNotification): Failure | null {
    const { statement, target } = notification;

    if (statement === null) {
        return null;
    }

    if (statement.cause === 'SendRefused' && target.kind === 'OutgoingEmail') {
        return { kind: 'send', outgoingEmailId: target.outgoingEmailId };
    }

    if (statement.cause === 'DraftCopyNotKept' && target.kind === 'MailDraft') {
        return { kind: 'draft', draftId: target.draftId };
    }

    const act = abandonedActOf(statement.counted);

    return statement.cause === 'MailboxChangeAbandoned' && target.kind === 'MailboxChange' && act !== null
        ? { kind: 'change', recordId: target.changeRecordId, storedEmailId: target.storedEmailId, act }
        : null;
}
