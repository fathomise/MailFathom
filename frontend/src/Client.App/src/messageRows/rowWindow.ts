// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Which rows of a list of mail are in the document, which is the whole of what keeps a mailbox of two hundred thousand
// messages costing what a screenful costs. It is arithmetic over a few numbers rather than a package, and the
// measurement that decided it is in `frontend/README.md`: every row of the folder's list and the search's results is one
// height, which both window the same row the same way, and the list read by conversation has three, one per variant of
// its row, each measured from a drawn row of it. `windowOfHeights` walks those heights; a virtualizer would add nothing
// for a list whose heights come in so few kinds.
//
// It sits beside the message row rather than inside a screen because more than one list windows its rows here.
//
// The height itself is measured from a rendered row rather than written here, because the row's height is a token
// decision and a number repeated in JavaScript is a second copy of it that drifts on the first change of the type
// scale, of the reader's font size, or of the browser's zoom.

/** How many rows outside the viewport are drawn either side of it, so scrolling does not reach the end of the document. */
export const overscanRows = 6;

/**
 * The estimate a list draws its first frame under, before a row has been rendered to measure.
 *
 * It only has to be close: a wrong estimate draws too many or too few rows for one frame, and the measurement that
 * arrives with that frame replaces it. It is deliberately not the row's real height, so nothing here can quietly become
 * the value the list works to.
 */
export const estimatedRowHeight = 72;

/** Which rows are drawn, and the space standing in for the ones that are not. */
export interface RowWindow {
    /** The index of the first row in the document. */
    readonly first: number;

    /** How many rows are in the document. */
    readonly count: number;

    /** The height, in pixels, reserved above them for the rows before. */
    readonly above: number;

    /** The height, in pixels, reserved below them for the rows after. */
    readonly below: number;
}

/**
 * How many rows a scroller of this size draws at once, wherever in the list it happens to stand.
 *
 * The count without the offset, which is what a caller asking *could a reader have watched this* needs: the window
 * below answers where the rows are, and that answer moves with every scroll, while how many of them there are does
 * not.
 *
 * @param rowHeight What one row measures, in pixels.
 * @param viewportHeight How tall the scroller is, in pixels.
 * @returns How many rows are in the document at once, or none where a row has no height to measure against.
 */
export function rowsDrawnAtOnce(rowHeight: number, viewportHeight: number): number {
    return rowHeight <= 0 ? 0 : Math.ceil(viewportHeight / rowHeight) + overscanRows * 2;
}

/**
 * The rows a scroller of this size shows at this offset, with the overscan either side of them.
 *
 * @param rowCount How many rows the list holds.
 * @param rowHeight What one row measures, in pixels.
 * @param scrollTop How far the scroller has been scrolled, in pixels.
 * @param viewportHeight How tall the scroller is, in pixels.
 * @returns The window of rows to draw and the space standing in for the rest.
 */
export function windowOf(rowCount: number, rowHeight: number, scrollTop: number, viewportHeight: number): RowWindow {
    if (rowCount <= 0 || rowHeight <= 0) {
        return { first: 0, count: 0, above: 0, below: 0 };
    }

    const drawn = rowsDrawnAtOnce(rowHeight, viewportHeight);
    const reached = Math.floor(Math.max(scrollTop, 0) / rowHeight);
    const first = Math.min(Math.max(reached - overscanRows, 0), Math.max(rowCount - 1, 0));
    const count = Math.min(drawn, rowCount - first);

    return {
        first,
        count,
        above: first * rowHeight,
        below: (rowCount - first - count) * rowHeight,
    };
}

/** How far a list has to be scrolled for a row to be the first one under the top of the scroller. */
export function offsetOfRow(row: number, rowHeight: number): number {
    return Math.max(row, 0) * rowHeight;
}

/**
 * The row the reader is looking at, which is what a returning visit is put back to.
 *
 * The first row under the top of the scroller rather than the first one drawn: the overscan is not on the screen, and
 * returning somebody to it would put them a screenful above where they left off.
 */
export function leadingRow(scrollTop: number, rowHeight: number): number {
    return rowHeight <= 0 ? 0 : Math.floor(Math.max(scrollTop, 0) / rowHeight);
}

/**
 * Where a scroller stands once its rows have changed height, so the reader is still on the row they were on.
 *
 * A row's height follows the composition — the phone draws a taller row than the strip a foldable's list narrows to —
 * so a window that changes composition under a reader keeps its place in rows rather than in pixels: the same offset
 * over rows half as tall is twice as far down the folder.
 *
 * @param scrollTop How far the scroller stands, in pixels, measured against the old height.
 * @param fromRowHeight What one row measured before, in pixels.
 * @param toRowHeight What one row measures now, in pixels.
 * @returns The offset that puts the same row under the top of the scroller at the new height.
 */
export function offsetKeptAcross(scrollTop: number, fromRowHeight: number, toRowHeight: number): number {
    return fromRowHeight <= 0 ? scrollTop : Math.round((Math.max(scrollTop, 0) * toRowHeight) / fromRowHeight);
}

/**
 * The rows a scroller shows at this offset where the rows are not all one height, with the overscan either side.
 *
 * The conversation list is the case: a row is as tall as what its title says, and each variant is measured rather than
 * assumed. A walk rather than a search, because the heights arrive a page at a time and a list somebody has scrolled
 * through holds hundreds of rows rather than a mailbox — so the arithmetic costs the scroll handler, not the render.
 *
 * @param heights What each row measures, in pixels, in the order they are drawn.
 * @param scrollTop How far the scroller has been scrolled, in pixels.
 * @param viewportHeight How tall the scroller is, in pixels.
 * @returns The window of rows to draw and the space standing in for the rest.
 */
export function windowOfHeights(heights: readonly number[], scrollTop: number, viewportHeight: number): RowWindow {
    const reached = Math.max(scrollTop, 0);
    let top = 0;
    let visible = 0;

    while (visible < heights.length - 1 && top + (heights[visible] ?? 0) <= reached) {
        top += heights[visible] ?? 0;
        visible += 1;
    }

    let bottom = top;
    let past = visible;

    while (past < heights.length && bottom < reached + viewportHeight) {
        bottom += heights[past] ?? 0;
        past += 1;
    }

    const first = Math.max(visible - overscanRows, 0);
    const end = Math.min(past + overscanRows, heights.length);
    const sum = (from: number, to: number): number =>
        heights.slice(from, to).reduce((total, height) => total + height, 0);

    return { first, count: end - first, above: sum(0, first), below: sum(end, heights.length) };
}
