// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// Which rows of a list of mail are in the document, which is the whole of what keeps a mailbox of two hundred thousand
// messages costing what a screenful costs. It is arithmetic over a few numbers rather than a package, and the
// measurement that decided it is in `frontend/README.md`: a row of the folder's list and the search's results is one of
// two heights, the whole row or the one line a spam row folds to, which both window the same row the same way, and the
// list read by conversation has one per variant of its row. Each height is measured from a drawn row of its kind, and
// `windowOfRuns` walks them a run of one height at a time; a virtualizer would add nothing for a list whose heights come
// in so few kinds.
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
 * Consecutive rows of one height, which is how a list whose rows are not all one height is laid out without a number per
 * row: a page of whole rows is one run, and a folded row splits the run it stands in. The arithmetic below costs one
 * step per run rather than per row, so a list a reader has scrolled forty thousand messages into costs what its pages
 * and its folded rows cost — the same reason `windowOf` costs nothing at any depth.
 */
export interface RowRun {
    /** How many rows stand in the run. */
    readonly count: number;

    /** What each of them measures, in pixels. */
    readonly height: number;
}

/**
 * The runs a list of row heights makes, consecutive rows of one height joined into one. Runs are always joined this way,
 * so two layouts of the same heights are the same runs, which is what comparing two of them relies on.
 */
export function runsOf(heights: Iterable<number>): readonly RowRun[] {
    return joined([...heights].map((height) => ({ count: 1, height })));
}

/** The runs joined wherever two neighbours are one height, and with every empty run left out. */
export function joined(runs: readonly RowRun[]): readonly RowRun[] {
    const out: RowRun[] = [];

    for (const run of runs) {
        const last = out.at(-1);

        if (run.count <= 0) {
            continue;
        }

        if (last?.height === run.height) {
            out[out.length - 1] = { count: last.count + run.count, height: run.height };
        } else {
            out.push(run);
        }
    }

    return out;
}

/** Whether two layouts lay every row out at the same height. */
export function sameRuns(one: readonly RowRun[], other: readonly RowRun[]): boolean {
    return (
        one.length === other.length &&
        one.every((run, at) => run.count === other[at]?.count && run.height === other[at].height)
    );
}

/** How many rows the runs lay out. */
export function rowCountOfRuns(runs: readonly RowRun[]): number {
    return runs.reduce((rows, run) => rows + run.count, 0);
}

/** How far a list laid out in these runs has to be scrolled for a row to be the first one under its top. */
export function offsetOfRowIn(runs: readonly RowRun[], row: number): number {
    let offset = 0;
    let passed = 0;

    for (const run of runs) {
        if (row < passed + run.count) {
            return offset + (Math.max(row, passed) - passed) * run.height;
        }

        offset += run.count * run.height;
        passed += run.count;
    }

    return offset;
}

/** What one row of a list laid out in these runs measures, or nothing for a row past its end. */
export function heightOfRowIn(runs: readonly RowRun[], row: number): number {
    let passed = 0;

    for (const run of runs) {
        if (row < passed + run.count) {
            return run.height;
        }

        passed += run.count;
    }

    return 0;
}

/**
 * The row standing at an offset of a list laid out in these runs, which is the row under the top of a scroller at that
 * offset — what {@link leadingRow} is for one height. A scroller standing past the end answers the last row.
 */
export function leadingRowIn(runs: readonly RowRun[], scrollTop: number): number {
    const reached = Math.max(scrollTop, 0);
    let top = 0;
    let passed = 0;

    for (const run of runs) {
        const bottom = top + run.count * run.height;

        if (run.height > 0 && bottom > reached) {
            return passed + Math.floor((reached - top) / run.height);
        }

        top = bottom;
        passed += run.count;
    }

    return Math.max(passed - 1, 0);
}

/**
 * The rows a scroller shows at this offset where the rows are not all one height, with the overscan either side.
 *
 * The overscan the leading end has no rows for is drawn after the screen instead, as `windowOf` draws it, so the window
 * holds as many rows at the top of the list as anywhere further down.
 *
 * @param runs The list, laid out in runs of one height.
 * @param scrollTop How far the scroller has been scrolled, in pixels.
 * @param viewportHeight How tall the scroller is, in pixels.
 * @returns The window of rows to draw and the space standing in for the rest.
 */
export function windowOfRuns(runs: readonly RowRun[], scrollTop: number, viewportHeight: number): RowWindow {
    const rowCount = rowCountOfRuns(runs);

    if (rowCount === 0) {
        return { first: 0, count: 0, above: 0, below: 0 };
    }

    const reached = Math.max(scrollTop, 0);
    const visible = leadingRowIn(runs, reached);
    const bottomEdge = reached + viewportHeight;
    const underBottom = leadingRowIn(runs, bottomEdge);

    // The rows starting above the bottom edge of the screen: the one standing at that edge counts only where it starts
    // above it, and none past the last row does.
    const past = Math.min(
        Math.max(visible, offsetOfRowIn(runs, underBottom) < bottomEdge ? underBottom + 1 : underBottom),
        rowCount,
    );
    const first = Math.max(visible - overscanRows, 0);
    const end = Math.min(past + overscanRows * 2 - (visible - first), rowCount);
    const total = offsetOfRowIn(runs, rowCount);

    return {
        first,
        count: end - first,
        above: offsetOfRowIn(runs, first),
        below: total - offsetOfRowIn(runs, end),
    };
}

/**
 * The rows a scroller shows at this offset, given what each row measures.
 *
 * The conversation list is the case: a row is as tall as what its title says, and each variant is measured rather than
 * assumed. It is laid out in runs like the message list, so both windows are one arithmetic, and it is handed a number
 * per row because that list holds the conversations a reader has scrolled through rather than a mailbox.
 */
export function windowOfHeights(heights: readonly number[], scrollTop: number, viewportHeight: number): RowWindow {
    return windowOfRuns(runsOf(heights), scrollTop, viewportHeight);
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

/**
 * Where a scroller stands once some of its rows have changed height, so the reader is still on the row they were on.
 *
 * What {@link offsetKeptAcross} is for rows of one height, and for the same two causes and one more: a composition that
 * draws rows at other heights, a measurement replacing an estimate, and a row above the reader folding or unfolding in
 * place as it is marked spam or not spam. Rows above the reader move the scroller by what they gained or lost, and the
 * reader's own row keeps the same share of itself above the top of the scroller.
 *
 * @param scrollTop How far the scroller stands, in pixels, measured against the old layout.
 * @param from The list as it was laid out before.
 * @param to The list as it is laid out now, row for row.
 * @returns The offset that puts the same row at the same place under the top of the scroller.
 */
export function offsetKeptAcrossRuns(scrollTop: number, from: readonly RowRun[], to: readonly RowRun[]): number {
    if (rowCountOfRuns(from) === 0) {
        return scrollTop;
    }

    const row = leadingRowIn(from, scrollTop);
    const within = Math.max(scrollTop, 0) - offsetOfRowIn(from, row);
    const fromHeight = heightOfRowIn(from, row);
    const toHeight = heightOfRowIn(to, row);
    const kept = fromHeight <= 0 ? within : Math.round((within * toHeight) / fromHeight);

    return offsetOfRowIn(to, row) + kept;
}
