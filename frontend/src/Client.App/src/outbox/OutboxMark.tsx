// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { KeyboardEvent, MouseEvent } from 'react';
import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import type { OutboxCount } from './outboxCounts';

// The mark on an account's Sent row, which is how an account's Outbox is reached at all: it has no row of its own in
// the tree, because it holds no stored mail and is no folder. Drawn only while something waits or was refused.
//
// Two shapes, as the design draws it: a pill at the end of the row where the column carries names, and a glyph of its
// own under Sent with the count as a badge where the column is folded to a rail and a row has no end to put one at.
//
// It is a control inside a row of the tree, so it takes the row's place in the tab order rather than one of its own —
// focusable exactly when the row is — and it answers its own press and key without the row answering them too, which
// would scope the client to Sent underneath the Outbox it just opened. A finger's hold is the row's: the mark's name
// is said again under that row's menu header, so one held finger opens one surface.

export interface OutboxMarking extends OutboxCount {
    /** Whether this account's Outbox is the one open, which the mark draws filled and states as `aria-current`. */
    readonly open: boolean;

    readonly onOpen: () => void;
}

function pillColours({ open, refused }: OutboxMarking): string {
    if (open) {
        return refused ? 'bg-error text-on-accent' : 'bg-accent text-on-accent';
    }

    return refused ? 'bg-error-soft text-error-text' : 'bg-accent-soft text-accent-deep';
}

function useMarkPress(marking: OutboxMarking, focusable: boolean) {
    const { anchor, tip } = useTip({ says: marking.said, tapReveals: false });

    return {
        tip,
        bound: {
            ...anchor,
            type: 'button' as const,
            'aria-label': marking.said,
            'aria-current': marking.open ? ('true' as const) : undefined,
            tabIndex: focusable ? 0 : -1,
            onClick: (event: MouseEvent) => {
                event.stopPropagation();
                marking.onOpen();
            },
            onKeyDown: (event: KeyboardEvent) => {
                anchor.onKeyDown(event);

                // The row's own keys walk and fold the tree; the mark keeps only what a button answers to.
                if (event.key === 'Enter' || event.key === ' ') {
                    event.stopPropagation();
                }
            },
        },
    };
}

/** The pill at the end of a Sent row. */
export function OutboxMark({ marking, focusable }: { readonly marking: OutboxMarking; readonly focusable: boolean }) {
    const { bound, tip } = useMarkPress(marking, focusable);

    return (
        <>
            <button
                {...bound}
                className={`touch-extended inline-flex h-5.5 shrink-0 cursor-pointer items-center justify-center gap-1 rounded-full ps-1.5 pe-2 text-sm font-semibold tabular-nums hover:brightness-95 pointer-coarse:px-2.5 ${pillColours(marking)}`}
            >
                <Icon name="outbox" className="size-icon-sm" />
                <span aria-hidden="true">{marking.count}</span>
            </button>
            {tip}
        </>
    );
}

/** The mark standing under Sent on a folded column, with its count as a badge. */
export function OutboxStrip({ marking, focusable }: { readonly marking: OutboxMarking; readonly focusable: boolean }) {
    const { bound, tip } = useMarkPress(marking, focusable);

    return (
        <>
            <button
                {...bound}
                className={`relative flex h-8.5 w-full cursor-pointer items-center justify-center rounded-md ${
                    marking.open
                        ? marking.refused
                            ? 'bg-error text-on-accent'
                            : 'bg-accent text-on-accent'
                        : `hover:bg-hover ${marking.refused ? 'text-error-text' : 'text-accent-deep'}`
                }`}
            >
                <Icon name="outbox" className="size-icon-2xl" />
                <span
                    aria-hidden="true"
                    className={`absolute top-0.5 right-1.5 h-4 min-w-4 rounded-full px-1 text-center text-xs/4 font-semibold text-on-accent ring-2 ring-sunken ${
                        marking.refused ? 'bg-error' : 'bg-accent'
                    }`}
                >
                    {marking.count}
                </span>
            </button>
            {tip}
        </>
    );
}
