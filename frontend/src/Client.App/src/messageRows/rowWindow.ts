// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Which rows of a list of mail are in the document, which is the whole of what keeps a mailbox of two hundred thousand
// messages costing what a screenful costs. It is arithmetic over a few numbers rather than a package, and the
// measurement that decided it is in `frontend/README.md`: a row of the folder's list and the search's results is one of
// two heights, the whole row or the one line a spam row folds to, which both window the same row the same way, and the
// list read by conversation has one per variant of its row. Each height is measured from a drawn row of its kind, and
// `windowOfHeights` walks them; a virtualizer would add nothing for a list whose heights come in so few kinds.
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

    // The overscan the leading end has no rows for is drawn after the screen instead, as `windowOf` draws it, so the
    // window holds as many rows at the top of the list as anywhere further down.
    const first = Math.max(visible - overscanRows, 0);
    const end = Math.min(past + overscanRows * 2 - (visible - first), heights.length);
    const sum = (from: number, to: number): number =>
        heights.slice(from, to).reduce((total, height) => total + height, 0);

    return { first, count: end - first, above: sum(0, first), below: sum(end, heights.length) };
}

/**
 * What a row of mail is drawn as, which is what decides its height: the whole row of three lines, or the one line a spam
 * row folds to outside the Spam folder.
 */
export type MessageRowShape = 'whole' | 'folded';

/**
 * The estimates a list of mail draws its first frame under, one per shape, before a row of that shape has been
 * rendered to measure. Only close, for the reason {@link estimatedRowHeight} gives.
 */
export const estimatedRowHeights: Readonly<Record<MessageRowShape, number>> = {
    whole: estimatedRowHeight,
    folded: 44,
};

/**
 * The heights a list of mail lays its rows out at once these rows have been measured: the first drawn row of each
 * shape stands for every row of it, since every row of a shape draws the same lines.
 *
 * @param current What each shape measured before.
 * @param drawn The shape and the measured height of each drawn row, in pixels; a row with no height yet is passed over.
 * @returns The heights with what was measured in them, and the same object where nothing changed.
 */
export function measuredRowHeights(
    current: Readonly<Record<MessageRowShape, number>>,
    drawn: Iterable<readonly [MessageRowShape, number]>,
): Readonly<Record<MessageRowShape, number>> {
    const measured: Partial<Record<MessageRowShape, number>> = {};

    for (const [shape, height] of drawn) {
        if (height > 0 && measured[shape] === undefined) {
            measured[shape] = height;
        }
    }

    const next = { ...current, ...measured };

    return next.whole === current.whole && next.folded === current.folded ? current : next;
}

/** How far a list whose rows are not all one height has to be scrolled for a row to be the first one under its top. */
export function offsetOfRowIn(heights: readonly number[], row: number): number {
    let offset = 0;

    for (let at = 0; at < Math.min(row, heights.length); at += 1) {
        offset += heights[at] ?? 0;
    }

    return offset;
}

/** The row under the top of a scroller whose rows are not all one height, which {@link leadingRow} is for one height. */
export function leadingRowIn(heights: readonly number[], scrollTop: number): number {
    const reached = Math.max(scrollTop, 0);
    let top = 0;
    let row = 0;

    while (row < heights.length - 1 && top + (heights[row] ?? 0) <= reached) {
        top += heights[row] ?? 0;
        row += 1;
    }

    return row;
}

/**
 * Where a scroller stands once some of its rows have changed height, so the reader is still on the row they were on.
 *
 * What {@link offsetKeptAcross} is for rows of one height, and for the same two causes and one more: a composition that
 * draws rows at other heights, a measurement replacing an estimate, and a row above the reader folding or unfolding in
 * place as it is marked spam or not spam. Rows above the reader move the scroller by what they gained or lost, and the
 * reader's own row keeps the same share of itself above the top of the scroller.
 *
 * @param scrollTop How far the scroller stands, in pixels, measured against the old heights.
 * @param from What each row measured before, in pixels.
 * @param to What each row measures now, in pixels, row for row.
 * @returns The offset that puts the same row at the same place under the top of the scroller.
 */
export function offsetKeptAcrossHeights(scrollTop: number, from: readonly number[], to: readonly number[]): number {
    if (from.length === 0) {
        return scrollTop;
    }

    const row = leadingRowIn(from, scrollTop);
    const within = Math.max(scrollTop, 0) - offsetOfRowIn(from, row);
    const fromHeight = from[row] ?? 0;
    const toHeight = to[row] ?? fromHeight;
    const kept = fromHeight <= 0 ? within : Math.round((within * toHeight) / fromHeight);

    return offsetOfRowIn(to, row) + kept;
}
