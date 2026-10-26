// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { MoveDestination } from '../mailboxActs/mailboxDestinations';
import { useMailboxActs, type ActedMessage, type MailboxAct } from '../mailboxActs/useMailboxActs';
import type { AbandonedAct } from '../notifications/failures';
import { useToasts } from '../toasts/useToasts';
import type { ChangeAct } from './changeStandings';
import { usePendingChanges, type AbandonedChange, type UndecidedChange } from './usePendingChanges';

// The question one change the mailbox did not take puts in front of the person, and the two answers to it. It is one
// hook rather than a pair of handlers on each surface because the question is asked in two places — on the message the
// change was asked of, and on the notification it raised — and one change answered differently from two places is the
// same failure shown as two.
//
// Two kinds of change reach here. One this tab followed is the pending-changes queue's own, and asking it again is the
// act that produced it, submitted afresh. One this tab knows only from its notification has no producer here, so
// asking it again is the act performed now, on the message as it stands — which is why a move offers the folder choice
// rather than guessing which folder was meant, and why a copy and a change of tags, which no surface here performs, are
// let go and never asked again.

/** One change waiting on the person, whichever of the two ways this tab came to know about it. */
export type ChangeQuestion =
    | { readonly kind: 'followed'; readonly change: UndecidedChange }
    | { readonly kind: 'abandoned'; readonly change: AbandonedChange };

/**
 * What asking a change again does: the act goes out again as it is, a folder has to be chosen first, or nothing here
 * can ask it and the only answer is to let it go.
 */
export type AskingAgain = 'asks' | 'choosesFolder' | null;

const exhaustedTitles: Readonly<Record<ChangeAct, MessageKey>> = {
    markRead: 'change.notMarkedRead',
    flag: 'change.notFlagged',
    unflag: 'change.notUnflagged',
    markUnread: 'change.notMarkedUnread',
    archive: 'change.notArchived',
    delete: 'change.notDeleted',
    move: 'change.notMoved',
    putBack: 'change.notPutBack',
};

const unknownTitles: Readonly<Record<ChangeAct, MessageKey>> = {
    markRead: 'change.markingReadUnknown',
    flag: 'change.flaggingUnknown',
    unflag: 'change.unflaggingUnknown',
    markUnread: 'change.markingUnreadUnknown',
    archive: 'change.archivingUnknown',
    delete: 'change.deletingUnknown',
    move: 'change.movingUnknown',
    putBack: 'change.puttingBackUnknown',
};

const abandonedTitles: Readonly<Record<AbandonedAct, MessageKey>> = {
    markRead: 'change.notMarkedRead',
    markUnread: 'change.notMarkedUnread',
    flag: 'change.notFlagged',
    unflag: 'change.notUnflagged',
    move: 'change.notMoved',
    delete: 'change.notDeleted',
    copy: 'change.notCopied',
    changeTags: 'change.tagsNotChanged',
};

// What asking a given-up change again performs. A move is asked through the folder choice, and the two acts no
// surface here performs are absent, which is what leaves letting go as their only answer.
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

/** How the question about one change is said: its title, the sentence under it, and whether nobody knows the outcome. */
export interface ChangeQuestionWords {
    readonly title: MessageKey;
    readonly text: MessageKey;
    readonly unknown: boolean;
}

export function wordChangeQuestion(question: ChangeQuestion): ChangeQuestionWords {
    if (question.kind === 'abandoned') {
        return { title: abandonedTitles[question.change.act], text: 'change.exhaustedText', unknown: false };
    }

    return question.change.standing === 'unanswered'
        ? { title: unknownTitles[question.change.act], text: 'change.unknownText', unknown: true }
        : { title: exhaustedTitles[question.change.act], text: 'change.exhaustedText', unknown: false };
}

export interface ChangeDecision {
    /** The question standing over one message, or `null` where none does. */
    readonly questionAbout: (storedEmailId: string) => ChangeQuestion | null;

    /** The question about one record, or `null` where it has already been answered. */
    readonly questionOf: (recordId: string) => ChangeQuestion | null;

    readonly askingAgain: (question: ChangeQuestion) => AskingAgain;

    /**
     * Asks the change again, on the message as it stands now. A followed change ignores both, because its producer
     * already knows what it asked of which message; a given-up move needs the folder somebody chose.
     */
    readonly askAgain: (question: ChangeQuestion, message: ActedMessage | null, destination?: MoveDestination) => void;

    readonly letGo: (question: ChangeQuestion) => void;
}

export function useChangeDecision(): ChangeDecision {
    const { translate } = useLocalization();
    const pending = usePendingChanges();
    const acts = useMailboxActs();
    const toasts = useToasts();

    function questionWhere(
        matches: (change: { readonly recordId: string; readonly storedEmailId: string }) => boolean,
    ) {
        const followed = pending.undecided.find(matches);

        if (followed !== undefined) {
            return { kind: 'followed', change: followed } as const;
        }

        const abandoned = pending.abandoned.find(matches);

        return abandoned === undefined ? null : ({ kind: 'abandoned', change: abandoned } as const);
    }

    function askingAgain(question: ChangeQuestion): AskingAgain {
        if (question.kind === 'followed') {
            return 'asks';
        }

        const act = performedAgain[question.change.act];

        return act === null ? null : act === 'move' ? 'choosesFolder' : 'asks';
    }

    function askAgain(question: ChangeQuestion, message: ActedMessage | null, destination?: MoveDestination): void {
        if (question.kind === 'followed') {
            pending.settle(question.change.recordId, 'askAgain');

            return;
        }

        const act = performedAgain[question.change.act];

        if (act === null || message === null || (act === 'move' && destination === undefined)) {
            return;
        }

        pending.settle(question.change.recordId, 'askAgain');
        acts.perform(act, [message], destination);

        // A filing act reports itself with the way back; a flag reports nothing of its own, so it is said here.
        if (act !== 'move' && act !== 'delete') {
            toasts.raise({
                kind: 'neutral',
                title: translate('change.askedAgainTitle'),
                body: translate('change.askedAgain'),
            });
        }
    }

    return {
        questionAbout: (storedEmailId) => questionWhere((change) => change.storedEmailId === storedEmailId),
        questionOf: (recordId) => questionWhere((change) => change.recordId === recordId),
        askingAgain,
        askAgain,
        letGo: (question) => {
            pending.settle(question.change.recordId, 'letGo');
        },
    };
}
