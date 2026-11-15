// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MessageRowShape } from '../messageRows/rowWindow';
import { pageKey, type HeldTimeline } from './heldTimeline';

// What each row of the message list is drawn as, which is what its window is laid out from. A row is whole or folded,
// and the two are different heights, so a page the list has let go of still has to say which of its rows were folded:
// laid out as whole rows instead, letting a page go would move every row under it by what its folded rows saved.
//
// What a page keeps is where its folded rows stood when it was last drawn, by the page. Only a page that drew a folded
// row keeps anything, so what this holds grows with the spam a reader scrolled past rather than with the folder.

/** Where each page drew a folded row, by the page, as each was last drawn while the list held its rows. */
export type FoldedRows = ReadonlyMap<string, ReadonlySet<number>>;

/** A list that has drawn no folded row yet. */
export const noFoldedRows: FoldedRows = new Map();

/**
 * What each row of the list is drawn as, held or not: a held row by its own shape, and a row of a page the list has let
 * go of by the shape it was drawn as when that page last held its rows. The list lays each row out at the height its
 * shape measures.
 *
 * @param shown The list as it is drawn.
 * @param folded Where each page drew its folded rows.
 * @param shapeOf What a held row is drawn as.
 * @returns One shape per row, in the order the rows are drawn.
 */
export function shapesOf<Row>(
    shown: HeldTimeline<Row>,
    folded: FoldedRows,
    shapeOf: (row: Row) => MessageRowShape,
): readonly MessageRowShape[] {
    return shown.slots.flatMap((slot) => {
        if (slot.rows !== null) {
            return slot.rows.map(shapeOf);
        }

        const foldedHere = folded.get(pageKey(slot.askedWith, slot.readAs));

        return Array.from({ length: slot.rowCount }, (_, at): MessageRowShape =>
            foldedHere?.has(at) === true ? 'folded' : 'whole',
        );
    });
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
