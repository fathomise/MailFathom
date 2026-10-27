// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useState } from 'react';
import {
    listAgentConversations,
    markAgentConversationRead,
    type AgentConversationSummary,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import type { SignalledChanges } from '../signals/signalledChanges';

/** The history as far as it has been read, and the two things a screen asks of it. */
export interface ConversationHistory {
    readonly conversations: readonly AgentConversationSummary[];
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;

    /** Reads the history again, which a screen asks for after each write it makes. */
    readonly reread: () => void;

    /** Says a conversation is in front of the person, which reads it as read here at once and on every machine after. */
    readonly markRead: (conversation: string) => void;
}

/**
 * A run writes many entries and each is signalled, so the history is read again at most this often while one runs —
 * often enough that the rail's dot follows a run starting and ending, and seldom enough that an answer composing does
 * not become a listing per block.
 */
const signalledRereadDelay = 1000;

const nothingRead: Omit<ConversationHistory, 'reread' | 'markRead'> = {
    conversations: [],
    reading: true,
    failure: null,
};

/**
 * Reads this person's conversations: once, again whenever a screen asks after a write, whenever a run is signalled to
 * have moved in one of them, and whenever the connection says an advance may have been missed. It is read for the
 * whole session rather than while the Agent is in front, because the rail reports on it from every space.
 *
 * @param changes What the deployment says changed, handed in because the frame reading the history is the one that
 * provides it to everything below.
 */
export function useConversationHistory(
    session: ClientSession | null,
    transport: MailFathomTransport,
    changes: SignalledChanges,
): ConversationHistory {
    const [asked, setAsked] = useState(0);
    const [history, setHistory] = useState<Omit<ConversationHistory, 'reread' | 'markRead'>>(nothingRead);

    // The frame outlives a session, so what one person's history held is let go of the moment the session changes,
    // rather than standing on the rail of whoever signs in next until their own list arrives.
    const [readFor, setReadFor] = useState(session);

    if (session !== readFor) {
        setReadFor(session);
        setHistory(nothingRead);
    }

    useEffect(() => {
        let waiting: ReturnType<typeof setTimeout> | null = null;

        const unlisten = changes.listen((change) => {
            if (change.kind === 'refresh') {
                setAsked((count) => count + 1);
            }

            if (change.kind === 'run.advanced' && change.conversation !== null && waiting === null) {
                waiting = setTimeout(() => {
                    waiting = null;
                    setAsked((count) => count + 1);
                }, signalledRereadDelay);
            }
        });

        return () => {
            unlisten();

            if (waiting !== null) {
                clearTimeout(waiting);
            }
        };
    }, [changes]);

    useEffect(() => {
        if (session === null) {
            return;
        }

        let listening = true;

        void listAgentConversations(session, transport).then((listed) => {
            if (!listening) {
                return;
            }

            setHistory((before) =>
                listed.outcome === 'read'
                    ? { conversations: listed.value, reading: false, failure: null }
                    : { ...before, reading: false, failure: listed.failure.reason },
            );
        });

        return () => {
            listening = false;
        };
    }, [session, transport, asked]);

    const reread = useCallback(() => {
        setAsked((count) => count + 1);
    }, []);

    const markRead = useCallback(
        (conversation: string) => {
            if (session === null) {
                return;
            }

            setHistory((before) => ({
                ...before,
                conversations: before.conversations.map((listed) =>
                    listed.id === conversation ? { ...listed, unread: false } : listed,
                ),
            }));
            void markAgentConversationRead(session, transport, conversation);
        },
        [session, transport],
    );

    return { ...history, reread, markRead };
}
