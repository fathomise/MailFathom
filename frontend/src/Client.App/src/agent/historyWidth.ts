// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// How wide the Agent's history column is drawn, which is `mailSpace/mailboxesWidth.ts` for the Agent space: the bounds,
// the reset, and the keyboard step of the one boundary it has.
//
// The design's column is 250 pixels and is dragged between 200 and 480, each of them its content alone; the widths
// here count its padding and its one-pixel rule in, twenty-one pixels, because they are the width the column is drawn
// at. The design draws that rule on the column and the five-pixel boundary beside it, and so does `AgentSpace`.

/** The narrowest the history column is drawn at. */
export const narrowestHistory = 221;

/** The widest it is drawn at. */
export const widestHistory = 501;

/** What the column opens at, and what a double-click on the grip or `Home` returns it to. */
export const startingHistoryWidth = 271;

/** How far one keyboard step moves the boundary. */
export const historyWidthStep = 16;

/**
 * The width actually drawn, given the one somebody dragged to.
 *
 * @param width The width somebody dragged to, which a drag past either edge puts outside the bounds.
 * @returns A width inside the bounds, rounded to whole pixels.
 */
export function historyWidthWithin(width: number): number {
    return Math.round(Math.min(Math.max(width, narrowestHistory), widestHistory));
}
