// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { KeyboardEvent } from 'react';
import type { AgentConversationSummary } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import { ReceivedAt } from '../controls/ReceivedAt';
import { useLocalization } from '../localization/useLocalization';

/**
 * One conversation in the history: its title and when it last moved, tinted with an accent title where it is the one
 * open, and tinted and outlined where it is picked out — a readable row rather than a card, with no edge marking it.
 *
 * Its quiet `more_horiz` control opens the same menu a right-click or a long press does. It is the pointer's way in
 * and hidden from the accessibility tree, because an option holds no focusable descendant; the menu key and Shift+F10
 * are the keyboard's.
 *
 * A row answers a press the way every list in this client does: a modifier-held click picks it out, a plain click picks
 * it out while a selection is held and opens it otherwise, and a right-click, a long press, the menu key or Shift+F10
 * opens its menu.
 */
export function ConversationRow({
    conversation,
    open,
    selected,
    selecting,
    focusable,
    attach,
    onReached,
    onOpen,
    onToggle,
    onPress,
}: {
    readonly conversation: AgentConversationSummary;
    readonly open: boolean;
    readonly selected: boolean;

    /** Whether a selection is being held, which turns a plain press into picking the row out. */
    readonly selecting: boolean;

    /** Whether this row is the one the keyboard is on, which is the list's roving tab stop. */
    readonly focusable: boolean;

    readonly attach: (row: HTMLLIElement | null) => void;
    readonly onReached: () => void;

    readonly onOpen: () => void;
    readonly onToggle: () => void;
    readonly onPress: (at: MenuPoint) => void;
}) {
    const { translate } = useLocalization();
    const press = useRowPress(onPress);

    function pressed(event: KeyboardEvent<HTMLLIElement>): void {
        if (event.key === 'ContextMenu' || (event.key === 'F10' && event.shiftKey)) {
            const box = event.currentTarget.getBoundingClientRect();

            event.preventDefault();
            onPress({ x: box.left + box.width / 2, y: box.top + box.height / 2 });

            return;
        }

        if (event.key !== 'Enter' && event.key !== ' ') {
            return;
        }

        event.preventDefault();

        if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
            onToggle();
        } else {
            onOpen();
        }
    }

    return (
        <li
            role="option"
            aria-selected={selected}
            aria-current={open ? 'true' : undefined}
            ref={attach}
            tabIndex={focusable ? 0 : -1}
            className={`group flex cursor-pointer flex-col gap-0.75 rounded-field px-3 py-2.75 workspace:px-2.5 workspace:py-2 ${
                open || selected ? 'bg-accent-soft' : 'hover:bg-hover'
            } ${selected ? 'ring-1 ring-accent-line ring-inset' : ''}`}
            onKeyDown={pressed}
            onFocus={onReached}
            onContextMenu={press.onContextMenu}
            onPointerDown={press.onPointerDown}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
            onClick={(event) => {
                if (press.tapSuppressed()) {
                    return;
                }

                if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
                    onToggle();
                } else {
                    onOpen();
                }
            }}
        >
            <span className="flex items-baseline gap-2">
                {selected ? <Icon name="check" className="size-icon-xs shrink-0 self-center text-accent-deep" /> : null}

                <span
                    className={`min-w-0 flex-1 truncate text-lg/snug workspace:text-base/snug ${
                        open ? 'font-semibold text-accent-deep' : 'font-medium text-text'
                    }`}
                >
                    {conversation.title ?? translate('agent.newConversation')}
                </span>

                <ReceivedAt at={conversation.lastActivityAt} />

                <span
                    aria-hidden="true"
                    className="-my-1.5 -me-1.5 flex size-9 shrink-0 cursor-pointer items-center justify-center self-center rounded-sm text-faint opacity-0 transition group-hover:opacity-100 hover:bg-hover hover:text-text pointer-coarse:opacity-100 workspace:-my-0.75 workspace:-me-1 workspace:size-6"
                    onPointerDown={(event) => {
                        event.stopPropagation();
                    }}
                    onPointerUp={(event) => {
                        event.stopPropagation();
                    }}
                    onClick={(event) => {
                        const box = event.currentTarget.getBoundingClientRect();

                        event.stopPropagation();
                        onPress({ x: box.left + box.width / 2, y: box.bottom });
                    }}
                >
                    <Icon name="more_horiz" className="size-icon-md" />
                </span>
            </span>
        </li>
    );
}
