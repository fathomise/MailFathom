// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MessageRowShape } from '../messageRows/rowWindow';
import type { HeldTimeline, TimelineSlot } from './heldTimeline';
import { foldedRowsOf, heightRunsOf, noFoldedRows, shapeRunsOf, shapesBetween } from './rowHeights';

// Rows named for what they are drawn as, so an expectation reads as the shapes it stands for.
type Row = 'whole' | 'folded';

function shapeOf(row: Row): MessageRowShape {
    return row;
}

function page(askedWith: string | null, rows: readonly Row[] | null, rowCount = rows?.length ?? 0): TimelineSlot<Row> {
    return { askedWith, readAs: 'forward', rowCount, rows, stale: false, nextCursor: null, previousCursor: null };
}

describe('shapeRunsOf', () => {
    it('answers held rows by the shape each is drawn as, consecutive rows of one shape joined', () => {
        const shown: HeldTimeline<Row> = { slots: [page(null, ['whole', 'whole', 'folded']), page('b', ['folded'])] };

        expect(shapeRunsOf(shown, noFoldedRows, shapeOf)).toStrictEqual([
            { count: 2, shape: 'whole' },
            { count: 2, shape: 'folded' },
        ]);
    });

    it('answers a page let go of by the shapes its rows were drawn as, so letting it go moves nothing', () => {
        const held: HeldTimeline<Row> = { slots: [page(null, ['folded', 'whole', 'whole']), page('b', ['whole'])] };
        const folded = foldedRowsOf(noFoldedRows, held, shapeOf);
        const letGo: HeldTimeline<Row> = { slots: [page(null, null, 3), page('b', ['whole'])] };

        expect(shapeRunsOf(letGo, folded, shapeOf)).toStrictEqual(shapeRunsOf(held, folded, shapeOf));
    });

    it('answers a page let go of before it drew a folded row as one run of whole rows, however many it stood for', () => {
        const shown: HeldTimeline<Row> = { slots: [page(null, null, 100), page('b', null, 100)] };

        expect(shapeRunsOf(shown, noFoldedRows, shapeOf)).toStrictEqual([{ count: 200, shape: 'whole' }]);
    });
});

describe('heightRunsOf', () => {
    it('lays the shapes out at what each measures, joining shapes measured at one height', () => {
        const shapes = [
            { count: 2, shape: 'whole' as const },
            { count: 1, shape: 'folded' as const },
        ];

        expect(heightRunsOf(shapes, { whole: 95, folded: 40 })).toStrictEqual([
            { count: 2, height: 95 },
            { count: 1, height: 40 },
        ]);
        expect(heightRunsOf(shapes, { whole: 95, folded: 95 })).toStrictEqual([{ count: 3, height: 95 }]);
    });
});

describe('shapesBetween', () => {
    it('answers the shape of each row a window draws', () => {
        const shapes = [
            { count: 3, shape: 'whole' as const },
            { count: 1, shape: 'folded' as const },
            { count: 5, shape: 'whole' as const },
        ];

        expect(shapesBetween(shapes, 2, 3)).toStrictEqual(['whole', 'folded', 'whole']);
        expect(shapesBetween(shapes, 8, 4)).toStrictEqual(['whole']);
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
