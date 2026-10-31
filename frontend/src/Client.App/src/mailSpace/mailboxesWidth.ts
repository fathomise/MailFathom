// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { deviceStore, mailboxesWidthKey } from '../device/deviceStore';

// How wide the mailbox column is drawn, which is `listWidth.ts` one column to the left and is a module of its own for
// the same reason: the bounds, the reset, and where the chosen width is written are arithmetic over one number, and
// each of them is testable without rendering three columns.
//
// The widths are the design's own — a column that opens at 210, or at 240 past the monitor width, and is dragged as
// far as 420 — and they
// are written here rather than in `styles.css` for the reason the list's are: what reads them is the drag arithmetic,
// the keyboard step, and the position the grip reports as a separator, none of which the markup spells.
//
// It carries no counterpart to the list's `leastReadingWidth`, and that absence is the one real difference between
// the two. The list and the reading pane share the room left over after this column, so the list has to leave its
// neighbour something to draw in; this column stands beside both of them and the pair below it does its own
// clamping against whatever room is left — which is why widening the mailboxes narrows the list rather than
// squeezing the message.

/** The narrowest the mailbox column is drawn at, which is the width the design opens it at. */
export const narrowestMailboxes = 210;

/** The widest it is drawn at, past which it stops being a column of names and becomes a second list. */
export const widestMailboxes = 420;

/** What the column opens at below the monitor width, and what a double-click on the grip returns to there. */
export const startingMailboxesWidth = narrowestMailboxes;

/** What the column opens at in this window before anybody drags it, and what a double-click on the grip returns to. */
export function startingMailboxesWidthIn(monitor: boolean): number {
    return monitor ? 240 : startingMailboxesWidth;
}

/** How far one keyboard step moves the boundary, which is a step a reader can see land rather than a pixel. */
export const mailboxesWidthStep = 16;

/**
 * The width actually drawn, given the one somebody chose.
 *
 * @param width The width somebody chose, which a stored value or a drag past the edge can put outside the bounds.
 * @returns A width inside the bounds, rounded to whole pixels.
 */
export function mailboxesWidthWithin(width: number): number {
    return Math.round(Math.min(Math.max(width, narrowestMailboxes), widestMailboxes));
}

/**
 * The width this person last chose on this machine, or `null` where they chose none and the band's own width stands.
 *
 * Read back as untrusted input, because a device store is a place a person can write: anything that is not a number
 * inside the bounds is answered as nothing chosen rather than as a column drawn off the screen.
 */
export function readMailboxesWidth(person: string | null): number | null {
    if (person === null) {
        return null;
    }

    const stored = Number(deviceStore().read(mailboxesWidthKey(person)));

    return Number.isFinite(stored) && stored > 0 ? mailboxesWidthWithin(stored) : null;
}

/**
 * Keeps the width this person settled on, so the next start of the client opens the Mail space at it — or forgets it
 * for `null`, which a column set back to its band's own width is, so the next band's width is the one it opens at.
 */
export function storeMailboxesWidth(person: string | null, width: number | null): void {
    if (person === null) {
        return;
    }

    if (width === null) {
        deviceStore().remove(mailboxesWidthKey(person));
        return;
    }

    deviceStore().write(mailboxesWidthKey(person), String(Math.round(width)));
}
