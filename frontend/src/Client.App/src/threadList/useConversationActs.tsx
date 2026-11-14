// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { flushSync } from 'react-dom';
import {
    isSpam,
    type ClientSession,
    type ListedMailThread,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { ActQuestions } from '../mailboxActs/ActQuestions';
import { actsDrawn, refusalSaid } from '../mailboxActs/drawnActs';
import { drawnSpam, useMailboxActs, type ActedMessage } from '../mailboxActs/useMailboxActs';
import { useToasts } from '../toasts/useToasts';
import { messagesTaken, readConversationMembers, type ConversationAct } from './conversationMessages';

// An act asked of whole conversations, carried out as the same act on the messages it takes. A conversation's row holds
// its newest message alone, so the act first reads the conversations' messages and only then knows what it is about:
// which is why the direction of each control is read off the row, and everything that depends on the messages
// themselves — a refusal, the question in front of deleting, the folders a move offers — is answered once they arrive.
//
// **Nothing here performs an act of its own.** Every one routes through `mailboxActs/useMailboxActs.ts`, with the
// questions the message list asks and the toasts it reports through, so filing a conversation and filing its messages
// one by one are the same act asked two ways.

export interface ConversationActs {
    /**
     * Asks one act of those conversations.
     *
     * @param onActed Runs once the act has been asked for — after its question is answered, where it stands behind one
     * — which is where a selection is let go.
     */
    readonly ask: (act: ConversationAct, threads: readonly ListedMailThread[], onActed?: () => void) => void;

    /** Whether the conversations' messages are being read for an act, which is the one stretch it is under way unseen. */
    readonly reading: boolean;

    /** The two questions an act may stand behind, drawn once by the surface that holds these acts. */
    readonly questions: ReactNode;
}

export function useConversationActs(session: ClientSession, transport: MailFathomTransport): ConversationActs {
    const { translate } = useLocalization();
    const acts = useMailboxActs();
    const toasts = useToasts();
    const deleting = useRef<HTMLDialogElement>(null);
    const filing = useRef<HTMLDialogElement>(null);
    const [reading, setReading] = useState(false);
    const [questioned, setQuestioned] = useState<{
        readonly messages: readonly ActedMessage[];
        readonly onActed?: (() => void) | undefined;
    }>({ messages: [] });

    // Whether the list is still on the screen when a read answers, since an answer is not something to act on for a
    // surface that has gone.
    const listening = useRef(true);

    useEffect(() => {
        listening.current = true;

        return () => {
            listening.current = false;
        };
    }, []);

    async function carryOut(
        act: ConversationAct,
        threads: readonly ListedMailThread[],
        onActed: (() => void) | undefined,
    ): Promise<void> {
        setReading(true);

        const answered = await readConversationMembers(
            session,
            transport,
            threads.map((thread) => thread.threadId),
        );

        if (!listening.current) {
            return;
        }

        setReading(false);

        if (answered.outcome === 'failed') {
            toasts.raise({ kind: 'error', title: translate('threadAct.notRead') });

            return;
        }

        const taken = messagesTaken(act, answered.value.messages, acts.folderRoleOf, (message) =>
            isSpam(drawnSpam(acts, { id: message.storedEmailId, spam: message.spam })),
        );
        const control = translate(act === 'spam' ? 'spam.spam' : actsDrawn[act].label);

        if (taken.length === 0) {
            toasts.raise({ kind: 'warning', title: translate('threadAct.nothingTaken', { control }) });

            return;
        }

        const refusal = act === 'spam' ? null : acts.refusalOf(act, taken);

        if (refusal !== null) {
            toasts.raise({ kind: 'warning', title: translate(refusalSaid[refusal], { control }) });

            return;
        }

        if (answered.value.cut) {
            toasts.raise({ kind: 'warning', title: translate('threadAct.cut') });
        }

        if (act === 'spam') {
            acts.toggleSpam(taken, onActed);
        } else if (act === 'delete' || act === 'move') {
            // The question is drawn over the messages before it is opened, so it never opens counting the ones an
            // earlier act was about.
            flushSync(() => {
                setQuestioned({ messages: taken, onActed });
            });
            (act === 'delete' ? deleting : filing).current?.showModal();
        } else {
            acts.perform(act, taken);
            onActed?.();
        }
    }

    return {
        ask: (act, threads, onActed) => {
            // One act at a time: a second press while the first is still reading would be the same act asked twice.
            if (!reading && threads.length > 0) {
                void carryOut(act, threads, onActed);
            }
        },
        reading,
        questions: (
            <ActQuestions
                messages={questioned.messages}
                deleting={deleting}
                filing={filing}
                onActed={questioned.onActed}
            />
        ),
    };
}
