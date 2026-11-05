// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientFailureReason, MailMutationOutcome } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { ChangeAct } from './changeSubmission';

// What a refused change is called on a screen. It sits apart from the provider that raises the sentences because
// naming is a rendering decision rather than part of reading an answer, which `changeSubmission.ts` does.
//
// The tables are split the way the sentences are: what did not happen is the act's own, and why is the deployment's
// answer and therefore one table for every act. That is what keeps a second act from costing a sentence per outcome,
// and it is why each reason below is written about mail rather than about a count — none of them has to agree with a
// number, so none of them needs a plural form.

/** What a message this act was refused for is called, selected by plural form for the count it happened to. */
export const refusalTitles: Readonly<Record<ChangeAct, Readonly<Record<Intl.LDMLPluralRule, MessageKey>>>> = {
    markRead: forms('notMarkedRead'),
    flag: forms('notFlagged'),
    unflag: forms('notUnflagged'),
    markUnread: forms('notMarkedUnread'),
    archive: forms('notArchived'),
    delete: forms('notDeleted'),
    move: forms('notFiled'),
    putBack: forms('notPutBack'),
};

/** The four forms one act's refusal is counted in, which every act declares the same way. */
function forms(
    refusal:
        | 'notMarkedRead'
        | 'notFlagged'
        | 'notUnflagged'
        | 'notMarkedUnread'
        | 'notArchived'
        | 'notDeleted'
        | 'notFiled'
        | 'notPutBack',
): Readonly<Record<Intl.LDMLPluralRule, MessageKey>> {
    return {
        zero: `pendingChange.${refusal}.other`,
        one: `pendingChange.${refusal}.one`,
        two: `pendingChange.${refusal}.other`,
        few: `pendingChange.${refusal}.few`,
        many: `pendingChange.${refusal}.many`,
        other: `pendingChange.${refusal}.other`,
    };
}

/**
 * Why the deployment wrote nothing down, or `null` for the three outcomes nobody is told about.
 *
 * `recorded` wrote something down and `applied` already made the change, so neither is a refusal at all.
 * `already-in-destination` is the collision that
 * converges without a decision: the mailbox already says what was asked for, so nothing failed, the screen was right,
 * and reporting it would be the client apologising for having been correct.
 */
export const refusalReasons: Readonly<Record<MailMutationOutcome, MessageKey | null>> = {
    recorded: null,
    applied: null,
    'already-in-destination': null,
    'message-not-found': 'pendingChange.messageNotFound',
    'destination-not-found': 'pendingChange.destinationNotFound',
    'account-no-longer-configured': 'pendingChange.accountNoLongerConfigured',
    'change-not-usable': 'pendingChange.changeNotUsable',
    'destination-not-allowed': 'pendingChange.destinationNotAllowed',
};

/** What each way a request can fail is called, which is what a refusal the server stated is said with. */
export const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};
