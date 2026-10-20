// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { KeyboardEvent } from 'react';
import type { AgentConversationSummary } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { ConversationRow } from './ConversationRow';

/** Conversations listed under one heading, or under none where the list carries a single group. */
export interface ConversationGroup {
    readonly label: string | null;
    readonly conversations: readonly AgentConversationSummary[];
}

/**
 * One list of the history — the conversations being worked in, grouped by the day each last moved, or the ones put
 * away — drawn as rows that can be picked out. Which row the keyboard is on is the history's to decide, because the
 * arrow keys walk from one list into the next and across the groups inside one.
 */
export function ConversationList({
    label,
    groups,
    open,
    selected,
    focusable,
    onWalk,
    attach,
    onReached,
    onOpen,
    onToggle,
    onPress,
}: {
    readonly label: string;
    readonly groups: readonly ConversationGroup[];

    /** The conversation in front, and `null` where a new one is. */
    readonly open: string | null;

    readonly selected: readonly string[];

    /** The row that is the history's one tab stop, which may stand in the other list. */
    readonly focusable: string | null;

    readonly onWalk: (event: KeyboardEvent<HTMLUListElement>) => void;
    readonly attach: (conversation: string, row: HTMLLIElement | null) => void;
    readonly onReached: (conversation: string) => void;
    readonly onOpen: (conversation: string) => void;
    readonly onToggle: (conversation: string) => void;
    readonly onPress: (conversation: AgentConversationSummary, at: MenuPoint) => void;
}) {
    const rowOf = (conversation: AgentConversationSummary) => (
        <ConversationRow
            key={conversation.id}
            conversation={conversation}
            open={conversation.id === open}
            selected={selected.includes(conversation.id)}
            selecting={selected.length > 0}
            focusable={conversation.id === focusable}
            attach={(row) => {
                attach(conversation.id, row);
            }}
            onReached={() => {
                onReached(conversation.id);
            }}
            onOpen={() => {
                onOpen(conversation.id);
            }}
            onToggle={() => {
                onToggle(conversation.id);
            }}
            onPress={(at) => {
                onPress(conversation, at);
            }}
        />
    );

    return (
        <ul
            role="listbox"
            aria-label={label}
            aria-multiselectable={true}
            className="flex flex-col gap-px"
            onKeyDown={onWalk}
        >
            {groups.map((group) =>
                group.label === null ? (
                    group.conversations.map(rowOf)
                ) : (
                    // A group of options inside the one listbox, so the day is said to whoever walks into it and the
                    // keyboard still walks the whole history as one list.
                    <li key={group.label} role="group" aria-label={group.label} className="flex flex-col gap-px">
                        <span
                            aria-hidden="true"
                            className="px-3 pt-2.5 pb-1.5 text-xs font-semibold text-faint workspace:px-2.5 workspace:pt-2 workspace:pb-1"
                        >
                            {group.label}
                        </span>

                        <ul role="presentation" className="flex flex-col gap-px">
                            {group.conversations.map(rowOf)}
                        </ul>
                    </li>
                ),
            )}
        </ul>
    );
}
