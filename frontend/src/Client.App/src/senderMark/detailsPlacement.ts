// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { menuEdge, type MenuSize } from '../contextMenu/menuPlacement';

// Where the sender details stand at the desktop, anchored under the mark that opened them: just below it and a little
// before its start, kept inside the window by the margin a menu keeps, and opened above the mark instead where the room
// below would cramp them and there is more above. Arithmetic over two rectangles, for the reason a menu's is.

/** How wide the details are drawn at the desktop, which is the design's own. */
export const detailsWidth = 392;

/** The tallest the details grow before their steps scroll. */
const tallestDetails = 560;

/** Below this much room under the mark, the details open above it where there is more room there. */
const crampedBelow = 320;

/** How far the details stand from the mark, and how far before its start they begin. */
const gap = 6;
const inset = 8;

export interface DetailsPlacement {
    readonly left: number;
    readonly top?: number;
    readonly bottom?: number;
    readonly maxHeight: number;
}

/** The place for details opened from a mark occupying `mark` in a window of `within`. */
export function detailsPlacement(
    mark: { readonly left: number; readonly top: number; readonly bottom: number },
    within: MenuSize,
): DetailsPlacement {
    const left = Math.max(menuEdge, Math.min(mark.left - inset, within.width - detailsWidth - menuEdge));
    const below = within.height - mark.bottom - 2 * menuEdge;
    const above = mark.top - 2 * menuEdge;

    return below < crampedBelow && above > below
        ? { left, bottom: within.height - mark.top + gap, maxHeight: Math.min(tallestDetails, above) }
        : { left, top: mark.bottom + gap, maxHeight: Math.min(tallestDetails, below) };
}
