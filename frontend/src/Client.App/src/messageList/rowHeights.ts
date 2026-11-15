// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { joined, type MessageRowShape, type RowRun } from '../messageRows/rowWindow';
import { pageKey, type HeldTimeline } from './heldTimeline';

// What each row of the message list is drawn as, which is what its window is laid out from. A row is whole or folded,
// and the two are different heights, so a page the list has let go of still has to say which of its rows were folded:
// laid out as whole rows instead, letting a page go would move every row under it by what its folded rows saved.
//
// What a page keeps is where its folded rows stood when it was last drawn, by the page. Only a page that drew a folded
// row keeps anything, so what this holds grows with the spam a reader scrolled past rather than with the folder.
//
// The list is laid out in runs of one shape rather than a shape per row: a page let go of with no folded row is one run
// however many rows it stood for, so laying out a list a reader has scrolled forty thousand messages into costs its
// pages and its folded rows, on every render and every scroll, rather than forty thousand steps.

/** Where each page drew a folded row, by the page, as each was last drawn while the list held its rows. */
export type FoldedRows = ReadonlyMap<string, ReadonlySet<number>>;

/** A list that has drawn no folded row yet. */
export const noFoldedRows: FoldedRows = new Map();

/** Consecutive rows drawn as one shape. */
export interface ShapeRun {
    readonly count: number;
    readonly shape: MessageRowShape;
}

function joinedShapes(runs: readonly ShapeRun[]): readonly ShapeRun[] {
    const out: ShapeRun[] = [];

    for (const run of runs) {
        const last = out.at(-1);

        if (run.count <= 0) {
            continue;
        }

        if (last?.shape === run.shape) {
            out[out.length - 1] = { count: last.count + run.count, shape: run.shape };
        } else {
            out.push(run);
        }
    }

    return out;
}

/**
 * What each row of the list is drawn as, held or not: a held row by its own shape, and a row of a page the list has let
 * go of by the shape it was drawn as when that page last held its rows.
 *
 * @param shown The list as it is drawn.
 * @param folded Where each page drew its folded rows.
 * @param shapeOf What a held row is drawn as.
 * @returns The rows in runs of one shape, in the order they are drawn.
 */
export function shapeRunsOf<Row>(
    shown: HeldTimeline<Row>,
    folded: FoldedRows,
    shapeOf: (row: Row) => MessageRowShape,
): readonly ShapeRun[] {
    const runs: ShapeRun[] = [];

    for (const slot of shown.slots) {
        if (slot.rows !== null) {
            for (const row of slot.rows) {
                runs.push({ count: 1, shape: shapeOf(row) });
            }

            continue;
        }

        let from = 0;

        for (const at of [...(folded.get(pageKey(slot.askedWith, slot.readAs)) ?? [])].sort(
            (one, other) => one - other,
        )) {
            if (at >= slot.rowCount) {
                break;
            }

            runs.push({ count: at - from, shape: 'whole' }, { count: 1, shape: 'folded' });
            from = at + 1;
        }

        runs.push({ count: slot.rowCount - from, shape: 'whole' });
    }

    return joinedShapes(runs);
}

/** The list laid out at what each shape measures. */
export function heightRunsOf(
    shapes: readonly ShapeRun[],
    heightOf: Readonly<Record<MessageRowShape, number>>,
): readonly RowRun[] {
    return joined(shapes.map(({ count, shape }) => ({ count, height: heightOf[shape] })));
}

/** What each of the rows from `first` is drawn as, for as many rows as `count` names — the rows a window draws. */
export function shapesBetween(shapes: readonly ShapeRun[], first: number, count: number): readonly MessageRowShape[] {
    const between: MessageRowShape[] = [];
    let passed = 0;

    for (const run of shapes) {
        const from = Math.max(first, passed);
        const to = Math.min(first + count, passed + run.count);

        for (let row = from; row < to; row += 1) {
            between.push(run.shape);
        }

        passed += run.count;

        if (passed >= first + count) {
            break;
        }
    }

    return between;
}

/**
 * Where each page draws its folded rows now, written down for the pages the list may let go of next.
 *
 * Read from the list as it is drawn, because a page let go of keeps the count it was drawn at — what is leaving already
 * out of it — so its folded rows are counted in that same numbering.
 *
 * @param current Where each page drew its folded rows before.
 * @param shown The list as it is drawn.
 * @param shapeOf What a held row is drawn as.
 * @returns Where each page draws its folded rows, and the same object where no held page changed.
 */
export function foldedRowsOf<Row>(
    current: FoldedRows,
    shown: HeldTimeline<Row>,
    shapeOf: (row: Row) => MessageRowShape,
): FoldedRows {
    let next: Map<string, ReadonlySet<number>> | null = null;

    for (const slot of shown.slots) {
        if (slot.rows === null) {
            continue;
        }

        const key = pageKey(slot.askedWith, slot.readAs);
        const before = current.get(key);
        const now = new Set(slot.rows.flatMap((row, at) => (shapeOf(row) === 'folded' ? [at] : [])));
        const same =
            before === undefined ? now.size === 0 : before.size === now.size && [...now].every((at) => before.has(at));

        if (same) {
            continue;
        }

        next ??= new Map(current);

        if (now.size === 0) {
            next.delete(key);
        } else {
            next.set(key, now);
        }
    }

    return next ?? current;
}
