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
import { failureLabels } from '../pendingChanges/changeWording';
import { useToasts } from '../toasts/useToasts';
import {
    messagesTaken,
    readConversationMembers,
    readsFolders,
    type ConversationAct,
    type FolderPlace,
} from './conversationMessages';

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

    /**
     * The conversations whose messages are being read for an act, which is the one stretch it is under way with nothing
     * yet changed: their rows say so, and a second act waits for the first.
     */
    readonly reading: ReadonlySet<string>;

    /**
     * Whether a delete asked of that conversation here still stands, which is what takes its row out of the list at
     * the press. It stops being true the moment the delete is taken back or refused.
     */
    readonly deleted: (threadId: string) => boolean;

    /** The two questions an act may stand behind, drawn once by the surface that holds these acts. */
    readonly questions: ReactNode;
}

const nothingRead: ReadonlySet<string> = new Set();

export function useConversationActs(session: ClientSession, transport: MailFathomTransport): ConversationActs {
    const { translate } = useLocalization();
    const acts = useMailboxActs();
    const toasts = useToasts();
    const deleting = useRef<HTMLDialogElement>(null);
    const filing = useRef<HTMLDialogElement>(null);
    const [reading, setReading] = useState(nothingRead);
    const [questioned, setQuestioned] = useState<{
        readonly messages: readonly ActedMessage[];
        readonly onActed?: (() => void) | undefined;
    }>({ messages: [] });

    // One message a delete took from each conversation deleted here, by the conversation. The delete standing for that
    // message is the delete standing for the conversation, so this names which act to read rather than holding a
    // second copy of whether it stands.
    const [deletedThrough, setDeletedThrough] = useState<ReadonlyMap<string, string>>(() => new Map());
    const deleteStands = (storedEmailId: string): boolean => acts.asked.get(storedEmailId)?.act === 'delete';

    // A delete taken back or refused is no longer this conversation's, so it is let go of as it stops standing — else
    // deleting that one message later, from inside the conversation, would take the whole row with it.
    if ([...deletedThrough.values()].some((storedEmailId) => !deleteStands(storedEmailId))) {
        setDeletedThrough(new Map([...deletedThrough].filter(([, storedEmailId]) => deleteStands(storedEmailId))));
    }

    // The acts as the newest render holds them, which is what a read that has just answered asks: the folders may have
    // been read while it was under way, and the render that started it would still say they were not.
    const held = useRef(acts);

    useEffect(() => {
        held.current = acts;
    });

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
        setReading(new Set(threads.map((thread) => thread.threadId)));

        const answered = await readConversationMembers(
            session,
            transport,
            threads.map((thread) => thread.threadId),
        );

        if (!listening.current) {
            return;
        }

        setReading(nothingRead);

        const mailbox = held.current;

        const control = translate(act === 'spam' ? 'spam.spam' : actsDrawn[act].label);

        if (answered.outcome === 'failed') {
            const { reason } = answered.failure;

            // Only a service that did not answer is worth asking again: a credential it refused, a grant it does not
            // hold, and an answer nobody can read fail the same way twice.
            toasts.raise({
                kind: 'error',
                title: translate('threadAct.notRead', { control, reason: translate(failureLabels[reason]) }),
                ...(reason === 'unavailable'
                    ? {
                          action: {
                              label: translate('threadAct.readAgain'),
                              take: () => {
                                  void carryOut(act, threads, onActed);
                              },
                          },
                      }
                    : {}),
            });

            return;
        }

        const placeOf = (message: ActedMessage): FolderPlace => {
            const folder = mailbox.folderOf(message);

            return folder === null ? { known: false } : { known: true, role: folder.role };
        };

        // An act that reads where a message is kept is not carried out over part of a conversation because the rest
        // sits in folders this client has not read: it says the folders are not known, as any filing act does.
        if (readsFolders(act) && answered.value.messages.some((message) => !placeOf(message).known)) {
            toasts.raise({ kind: 'warning', title: translate(refusalSaid.foldersUnknown, { control }) });

            return;
        }

        const taken = messagesTaken(act, answered.value.messages, placeOf, (message) =>
            isSpam(drawnSpam(mailbox, { id: message.storedEmailId, spam: message.spam })),
        );

        if (taken.length === 0) {
            toasts.raise({ kind: 'warning', title: translate('threadAct.nothingTaken', { control }) });

            return;
        }

        const refusal = act === 'spam' ? null : mailbox.refusalOf(act, taken);

        if (refusal !== null) {
            toasts.raise({ kind: 'warning', title: translate(refusalSaid[refusal], { control }) });

            return;
        }

        if (answered.value.cut) {
            toasts.raise({ kind: 'warning', title: translate('threadAct.cut') });
        }

        if (act === 'spam') {
            mailbox.toggleSpam(taken, onActed);
        } else if (act === 'delete' || act === 'move') {
            const { conversationOf } = answered.value;

            // The question is drawn over the messages before it is opened, so it never opens counting the ones an
            // earlier act was about.
            flushSync(() => {
                setQuestioned({
                    messages: taken,
                    onActed: () => {
                        if (act === 'delete') {
                            // Written down as the delete is asked, which is the commit it starts standing in.
                            setDeletedThrough((current) => {
                                const next = new Map(current);

                                for (const message of taken) {
                                    const threadId = conversationOf.get(message.storedEmailId);

                                    if (threadId !== undefined) {
                                        next.set(threadId, message.storedEmailId);
                                    }
                                }

                                return next;
                            });
                        }

                        onActed?.();
                    },
                });
            });
            (act === 'delete' ? deleting : filing).current?.showModal();
        } else {
            mailbox.perform(act, taken);
            onActed?.();
        }
    }

    return {
        ask: (act, threads, onActed) => {
            // One act at a time: a second press while the first is still reading would be the same act asked twice.
            if (reading.size === 0 && threads.length > 0) {
                void carryOut(act, threads, onActed);
            }
        },
        reading,
        deleted: (threadId) => {
            const through = deletedThrough.get(threadId);

            return through !== undefined && deleteStands(through);
        },
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
