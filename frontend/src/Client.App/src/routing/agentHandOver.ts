// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type { AgentMessageScope } from '@mailfathom/client-backend';

// Carrying what is in front of somebody into a conversation with the agent. Every space that draws an entry to the
// agent — a thread, an event, a Discover answer, a merged arrival notification — hands over the same shape, and the
// Agent space is the one reader of it: it opens a new conversation and draws what was handed over as the context chip, which the next question is asked
// under until somebody clears it. So a space names what it is handing over and never carries a conversation of its own.
//
// **A hand-over opens a conversation and asks nothing, unless the press that made it was the question.** What is
// typed next is the question, and whatever the agent answers with is proposed rather than done, exactly as for a
// question asked from the Agent space itself. *Summarize* on a merged arrival notification is the one press that asks
// as it hands over, because summarizing is the whole of what it was pressed for; it carries its question in words the
// reader sees asked, rather than as an instruction behind the conversation.

/** What a space hands to the agent: the thing the next question is about, and what it is called on the chip. */
export interface AgentHandOver {
    readonly scope: AgentMessageScope & {
        readonly kind: 'thread' | 'calendarEvent' | 'discoveryRun' | 'notification';
    };

    /**
     * The thread's subject, the event's title, the question a Discover answer answered, or what a notification counts.
     */
    readonly title: string;

    /** What the new conversation asks at once, where the press that handed over was itself the question. */
    readonly question?: string;
}

/**
 * Opens the Agent space on a new conversation carrying what was handed over, or `null` where this credential has no
 * agent to open — which is what an entry reads to know whether it is drawn at all.
 */
export const AgentHandOverContext = createContext<((handOver: AgentHandOver) => void) | null>(null);

export function useAgentHandOver(): ((handOver: AgentHandOver) => void) | null {
    return useContext(AgentHandOverContext);
}
