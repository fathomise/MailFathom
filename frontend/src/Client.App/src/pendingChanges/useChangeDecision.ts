// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { MoveDestination } from '../mailboxActs/mailboxDestinations';
import { useMailboxActs, type ActedMessage, type MailboxAct } from '../mailboxActs/useMailboxActs';
import type { AbandonedAct } from '../notifications/failures';
import { useToasts } from '../toasts/useToasts';
import { usePendingChanges, type AbandonedChange } from './usePendingChanges';

// One answer to a change the deployment undid after the mail server refused it, for every surface that offers one: the
// notice on the message and the row in the notification centre. Both ask the same two questions — what is this, and
// what does asking again mean for it — and a second reading of either is how the notice and the row would come to
// disagree about one refusal.
//
// Asking again is a new act under a new key, performed on the message as it stands now: the deployment already put the
// message back, so the act is made at once, as any act is, and reaches the mail server afterwards like any other.

/** What asking a change again does: the act goes out again as it is, a folder has to be chosen first, or it cannot. */
export type AskingAgain = 'asks' | 'choosesFolder' | null;

const refusedTitles: Readonly<Record<AbandonedAct, MessageKey>> = {
    markRead: 'change.notMarkedRead',
    markUnread: 'change.notMarkedUnread',
    flag: 'change.notFlagged',
    unflag: 'change.notUnflagged',
    move: 'change.notMoved',
    delete: 'change.notDeleted',
    copy: 'change.notCopied',
    changeTags: 'change.tagsNotChanged',
};

/** What undoing each act did, said for a refusal asking again may change and for one it may not. */
const refusedTexts: Readonly<Record<'transient' | 'permanent', Readonly<Record<AbandonedAct, MessageKey>>>> = {
    transient: texts('change.refusedText'),
    permanent: texts('change.refusedForGoodText'),
};

function texts(family: 'change.refusedText' | 'change.refusedForGoodText'): Readonly<Record<AbandonedAct, MessageKey>> {
    return {
        markRead: `${family}.markRead`,
        markUnread: `${family}.markUnread`,
        flag: `${family}.flag`,
        unflag: `${family}.unflag`,
        move: `${family}.move`,
        delete: `${family}.delete`,
        copy: `${family}.copy`,
        changeTags: `${family}.changeTags`,
    };
}

/**
 * The act a refused change is performed again as, or `null` where this client performs none: a copy and a change of
 * tags are acts the deployment records for other askers, and the Mail space offers neither.
 */
const performedAgain: Readonly<Record<AbandonedAct, MailboxAct | null>> = {
    markRead: 'markRead',
    markUnread: 'markUnread',
    flag: 'flag',
    unflag: 'unflag',
    move: 'move',
    delete: 'delete',
    copy: null,
    changeTags: null,
};

/** How the notice about one change is said: its title and the sentence under it. */
export interface ChangeQuestionWords {
    readonly title: MessageKey;
    readonly text: MessageKey;
}

export function wordChangeQuestion(change: AbandonedChange): ChangeQuestionWords {
    return {
        title: refusedTitles[change.act],
        text: refusedTexts[change.permanent ? 'permanent' : 'transient'][change.act],
    };
}

export interface ChangeDecision {
    /** The refused change standing over one message, or `null` where none does. */
    readonly questionAbout: (storedEmailId: string) => AbandonedChange | null;

    /** The refused change one record names, or `null` where it has already been answered. */
    readonly questionOf: (recordId: string) => AbandonedChange | null;

    readonly askingAgain: (change: AbandonedChange) => AskingAgain;

    /** Asks the change again, on the message as it stands now; a move needs the folder somebody chose. */
    readonly askAgain: (change: AbandonedChange, message: ActedMessage | null, destination?: MoveDestination) => void;

    readonly letGo: (change: AbandonedChange) => void;
}

export function useChangeDecision(): ChangeDecision {
    const { translate } = useLocalization();
    const pending = usePendingChanges();
    const acts = useMailboxActs();
    const toasts = useToasts();

    function askingAgain(change: AbandonedChange): AskingAgain {
        const act = performedAgain[change.act];

        if (change.permanent || act === null) {
            return null;
        }

        return act === 'move' ? 'choosesFolder' : 'asks';
    }

    function askAgain(change: AbandonedChange, message: ActedMessage | null, destination?: MoveDestination): void {
        const act = performedAgain[change.act];

        if (askingAgain(change) === null || act === null || message === null) {
            return;
        }

        if (act === 'move' && destination === undefined) {
            return;
        }

        pending.settle(change.recordId, 'askAgain');
        acts.perform(act, [message], destination);

        // A move and a delete say what they did in their own toast, which carries the way back; the flag acts report
        // nothing of their own, so what asking again did is said here.
        if (act !== 'move' && act !== 'delete') {
            toasts.raise({
                kind: 'neutral',
                title: translate('change.askedAgainTitle'),
                body: translate('change.askedAgain'),
            });
        }
    }

    return {
        questionAbout: (storedEmailId) =>
            pending.abandoned.find((change) => change.storedEmailId === storedEmailId) ?? null,
        questionOf: (recordId) => pending.abandoned.find((change) => change.recordId === recordId) ?? null,
        askingAgain,
        askAgain,
        letGo: (change) => {
            pending.settle(change.recordId, 'letGo');
        },
    };
}
