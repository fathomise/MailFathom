// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MessageRowShape } from '../messageRows/rowWindow';
import type { HeldTimeline, TimelineSlot } from './heldTimeline';
import { foldedRowsOf, noFoldedRows, shapesOf } from './rowHeights';

// Rows named for what they are drawn as, so an expectation reads as the shapes it stands for.
type Row = 'whole' | 'folded';

function shapeOf(row: Row): MessageRowShape {
    return row;
}

function page(askedWith: string | null, rows: readonly Row[] | null, rowCount = rows?.length ?? 0): TimelineSlot<Row> {
    return { askedWith, readAs: 'forward', rowCount, rows, stale: false, nextCursor: null, previousCursor: null };
}

describe('shapesOf', () => {
    it('answers a held row by the shape it is drawn as', () => {
        const shown: HeldTimeline<Row> = { slots: [page(null, ['whole', 'folded', 'whole'])] };

        expect(shapesOf(shown, noFoldedRows, shapeOf)).toStrictEqual(['whole', 'folded', 'whole']);
    });

    it('answers a page let go of by the shapes its rows were drawn as, so letting it go moves nothing', () => {
        const held: HeldTimeline<Row> = { slots: [page(null, ['folded', 'whole']), page('b', ['whole', 'folded'])] };
        const folded = foldedRowsOf(noFoldedRows, held, shapeOf);
        const letGo: HeldTimeline<Row> = { slots: [page(null, null, 2), page('b', ['whole', 'folded'])] };

        expect(shapesOf(letGo, folded, shapeOf)).toStrictEqual(shapesOf(held, folded, shapeOf));
    });

    it('answers a page let go of before it drew a folded row as whole rows', () => {
        const shown: HeldTimeline<Row> = { slots: [page(null, null, 2)] };

        expect(shapesOf(shown, noFoldedRows, shapeOf)).toStrictEqual(['whole', 'whole']);
    });
});

describe('foldedRowsOf', () => {
    it('writes down where each held page draws its folded rows', () => {
        const shown: HeldTimeline<Row> = { slots: [page(null, ['folded', 'whole', 'folded']), page('b', null, 4)] };

        expect(foldedRowsOf(noFoldedRows, shown, shapeOf)).toStrictEqual(new Map([['forward\n', new Set([0, 2])]]));
    });

    it('forgets a page once none of its rows is folded, as when the one spam row on it is marked not spam', () => {
        const before = foldedRowsOf(noFoldedRows, { slots: [page(null, ['folded'])] }, shapeOf);

        expect(foldedRowsOf(before, { slots: [page(null, ['whole'])] }, shapeOf)).toStrictEqual(new Map());
    });

    it('answers what it was given where no held page changed, so an ordinary scroll changes nothing', () => {
        const shown: HeldTimeline<Row> = { slots: [page(null, ['folded', 'whole']), page('b', ['whole'])] };
        const before = foldedRowsOf(noFoldedRows, shown, shapeOf);

        expect(foldedRowsOf(before, shown, shapeOf)).toBe(before);
        expect(foldedRowsOf(noFoldedRows, { slots: [page(null, ['whole'])] }, shapeOf)).toBe(noFoldedRows);
    });
});
