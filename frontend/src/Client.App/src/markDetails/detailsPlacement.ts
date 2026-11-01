// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { menuEdge, type MenuSize } from '../contextMenu/menuPlacement';

// Where a mark's details stand at the desktop, anchored under the mark that opened them: just below it, kept inside the
// window by the margin a menu keeps, and opened above the mark instead where the room below would cramp them and there
// is more above. Arithmetic over two rectangles, for the reason a menu's is.
//
// Each mark's details take the design's own measurements for them, which is what a shape is: how wide, how tall
// before they scroll, how little room below sends them above, and which edge of the mark they line up with — the
// sender's begin a little before the mark's start, the receipts' end on the mark's end.

/** The measurements one mark's details are drawn with at the desktop. */
export interface DetailsShape {
    readonly width: number;
    readonly tallest: number;
    readonly crampedBelow: number;
    readonly alignedWith: 'start' | 'end';
}

/** Where the mark that asked stands on the screen. */
export interface MarkAnchor {
    readonly left: number;
    readonly right: number;
    readonly top: number;
    readonly bottom: number;
}

/** The sender details, which begin a little before the mark's start. */
export const senderDetailsShape: DetailsShape = { width: 392, tallest: 560, crampedBelow: 320, alignedWith: 'start' };

/** The receipt details, whose end edge stands on the mark's. */
export const receiptDetailsShape: DetailsShape = { width: 360, tallest: 520, crampedBelow: 300, alignedWith: 'end' };

/** How far the details stand from the mark, and how far before its start the sender's begin. */
const gap = 6;
const inset = 8;

export interface DetailsPlacement {
    readonly left: number;
    readonly top?: number;
    readonly bottom?: number;
    readonly maxHeight: number;
}

/** The place for details of `shape` opened from a mark occupying `mark` in a window of `within`. */
export function detailsPlacement(mark: MarkAnchor, within: MenuSize, shape: DetailsShape): DetailsPlacement {
    const wanted = shape.alignedWith === 'start' ? mark.left - inset : mark.right - shape.width;
    const left = Math.max(menuEdge, Math.min(wanted, within.width - shape.width - menuEdge));
    const below = within.height - mark.bottom - 2 * menuEdge;
    const above = mark.top - 2 * menuEdge;

    return below < shape.crampedBelow && above > below
        ? { left, bottom: within.height - mark.top + gap, maxHeight: Math.min(shape.tallest, above) }
        : { left, top: mark.bottom + gap, maxHeight: Math.min(shape.tallest, below) };
}
