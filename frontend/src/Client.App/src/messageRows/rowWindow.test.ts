// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    estimatedRowHeights,
    heightOfRowIn,
    leadingRow,
    leadingRowIn,
    measuredRowHeights,
    offsetKeptAcross,
    offsetKeptAcrossRuns,
    offsetOfRow,
    offsetOfRowIn,
    runsOf,
    sameRuns,
    windowOfRuns,
    overscanRows,
    rowsDrawnAtOnce,
    windowOf,
    windowOfHeights,
} from './rowWindow';

// Ten rows of a hundred pixels each, so a number in an expectation reads as the row it stands for.
const rowHeight = 100;
const viewport = 1_000;

describe('windowOf', () => {
    it('draws nothing for a list holding nothing', () => {
        expect(windowOf(0, rowHeight, 0, viewport)).toStrictEqual({ first: 0, count: 0, above: 0, below: 0 });
    });

    it('draws the rows a screenful shows and the overscan after them, at the top of a long list', () => {
        const drawn = windowOf(10_000, rowHeight, 0, viewport);

        expect(drawn.first).toBe(0);
        expect(drawn.count).toBe(10 + overscanRows * 2);
        expect(drawn.above).toBe(0);
    });

    it('keeps the number of rows in the document the same however far down the list it is', () => {
        const near = windowOf(214_000, rowHeight, 0, viewport);
        const far = windowOf(214_000, rowHeight, 21_000_000, viewport);

        expect(far.count).toBe(near.count);
    });

    it('starts the overscan before the row the reader is on, so scrolling does not reach the end of the document', () => {
        const drawn = windowOf(10_000, rowHeight, 5_000, viewport);

        expect(drawn.first).toBe(50 - overscanRows);
    });

    it('reserves the space the rows above and below the window would take', () => {
        const drawn = windowOf(10_000, rowHeight, 5_000, viewport);

        expect(drawn.above).toBe(drawn.first * rowHeight);
        expect(drawn.above + drawn.count * rowHeight + drawn.below).toBe(10_000 * rowHeight);
    });

    it('draws to the end of a list rather than past it', () => {
        const drawn = windowOf(12, rowHeight, 0, viewport);

        expect(drawn.first).toBe(0);
        expect(drawn.count).toBe(12);
        expect(drawn.below).toBe(0);
    });

    it('draws the last rows for a scroller left below every row there is', () => {
        const drawn = windowOf(12, rowHeight, 99_000, viewport);

        expect(drawn.first).toBe(11);
        expect(drawn.count).toBe(1);
        expect(drawn.below).toBe(0);
    });

    it('reads a scroller pulled above its own top as being at the top', () => {
        expect(windowOf(100, rowHeight, -400, viewport)).toStrictEqual(windowOf(100, rowHeight, 0, viewport));
    });

    it('draws nothing before a row has been measured', () => {
        expect(windowOf(100, 0, 0, viewport).count).toBe(0);
    });
});

describe('offsetOfRow', () => {
    it('answers where a row stands in a list of rows one height each', () => {
        expect(offsetOfRow(42, rowHeight)).toBe(4_200);
    });

    it('answers the top of the list for a row before the first', () => {
        expect(offsetOfRow(-3, rowHeight)).toBe(0);
    });
});

describe('leadingRow', () => {
    it('answers the row under the top of the scroller rather than the first one drawn', () => {
        expect(leadingRow(4_250, rowHeight)).toBe(42);
    });

    it('answers the first row before anything has been measured', () => {
        expect(leadingRow(4_250, 0)).toBe(0);
    });
});

describe('offsetKeptAcross', () => {
    it('keeps the reader on the row they were on when the rows change height under them', () => {
        const kept = offsetKeptAcross(4_250, rowHeight, 50);

        expect(leadingRow(kept, 50)).toBe(leadingRow(4_250, rowHeight));
    });

    it('keeps the offset where there was no height to measure it against', () => {
        expect(offsetKeptAcross(4_250, 0, 50)).toBe(4_250);
    });
});

describe('rowsDrawnAtOnce', () => {
    it('answers a screenful and the overscan either side of it, wherever the scroller stands', () => {
        expect(rowsDrawnAtOnce(rowHeight, 500)).toBe(windowOf(10_000, rowHeight, 0, 500).count);
        expect(rowsDrawnAtOnce(rowHeight, 500)).toBe(windowOf(10_000, rowHeight, 40_000, 500).count);
    });

    it('answers no rows before one has been measured, there being no height to divide by', () => {
        expect(rowsDrawnAtOnce(0, 500)).toBe(0);
    });
});

describe('windowOfHeights', () => {
    // Forty rows alternating a hundred and eighty pixels, so every pair of them is 180 pixels.
    const heights = Array.from({ length: 40 }, (_, row) => (row % 2 === 0 ? 100 : 80));
    const total = (from: number, to: number): number => heights.slice(from, to).reduce((sum, row) => sum + row, 0);

    it('draws nothing for a list holding nothing', () => {
        expect(windowOfHeights([], 0, viewport)).toStrictEqual({ first: 0, count: 0, above: 0, below: 0 });
    });

    it('draws the rows in view and both sides of the overscan after them, at the top of the list', () => {
        // Six rows fill 540 pixels, which is the viewport, and the overscan the top has no rows for is drawn after them.
        expect(windowOfHeights(heights, 0, 540)).toStrictEqual({
            first: 0,
            count: 6 + overscanRows * 2,
            above: 0,
            below: total(6 + overscanRows * 2, 40),
        });
    });

    it('holds as many rows at the top of the list as further down it', () => {
        expect(windowOfHeights(heights, 0, 360).count).toBe(windowOfHeights(heights, 1_800, 360).count);
    });

    it('starts the window the overscan before the first row in view, and reserves what it does not draw', () => {
        // Row 20 starts at 1 800 pixels, so it is the first row in view.
        const drawn = windowOfHeights(heights, 1_800, 360);

        expect(drawn.first).toBe(20 - overscanRows);
        expect(drawn.above).toBe(total(0, drawn.first));
        expect(drawn.above + total(drawn.first, drawn.first + drawn.count) + drawn.below).toBe(total(0, 40));
    });

    it('keeps the last row drawn when the scroller stands past the end of the list', () => {
        const drawn = windowOfHeights([100, 100, 100], 5_000, 400);

        expect(drawn.first + drawn.count).toBe(3);
    });
});

describe('runsOf', () => {
    it('joins consecutive rows of one height into one run', () => {
        expect(runsOf([100, 100, 40, 100])).toStrictEqual([
            { count: 2, height: 100 },
            { count: 1, height: 40 },
            { count: 1, height: 100 },
        ]);
    });

    it('lays two lists of the same heights out as the same runs', () => {
        expect(sameRuns(runsOf([100, 40, 40]), runsOf([100, 40, 40]))).toBe(true);
        expect(sameRuns(runsOf([100, 40, 40]), runsOf([100, 100, 40]))).toBe(false);
    });
});

describe('offsetOfRowIn', () => {
    it('puts a row below every row above it, whatever height each of them is', () => {
        expect(offsetOfRowIn(runsOf([100, 40, 100]), 0)).toBe(0);
        expect(offsetOfRowIn(runsOf([100, 40, 100]), 2)).toBe(140);
        expect(offsetOfRowIn([{ count: 40_000, height: 95 }], 30_000)).toBe(30_000 * 95);
    });

    it('stops at the end of the list for a row past it', () => {
        expect(offsetOfRowIn(runsOf([100, 40]), 5)).toBe(140);
    });
});

describe('heightOfRowIn', () => {
    it('answers what the row measures, and nothing past the end of the list', () => {
        expect(heightOfRowIn(runsOf([100, 40, 100]), 1)).toBe(40);
        expect(heightOfRowIn(runsOf([100, 40, 100]), 3)).toBe(0);
    });
});

describe('leadingRowIn', () => {
    it('answers the row under the top of the scroller, counting each row at its own height', () => {
        expect(leadingRowIn(runsOf([100, 40, 100]), 0)).toBe(0);
        expect(leadingRowIn(runsOf([100, 40, 100]), 120)).toBe(1);
        expect(leadingRowIn(runsOf([100, 40, 100]), 140)).toBe(2);
    });

    it('answers the last row for a scroller standing past the end of the list', () => {
        expect(leadingRowIn(runsOf([100, 40, 100]), 5_000)).toBe(2);
    });
});

describe('windowOfRuns', () => {
    it('draws the window a number per row would, from a page of rows laid out as one run', () => {
        const heights = [...Array.from({ length: 300 }, () => 95), 40, ...Array.from({ length: 300 }, () => 95)];

        expect(windowOfRuns(runsOf(heights), 28_600, 800)).toStrictEqual(
            windowOfRuns(
                heights.map((height) => ({ count: 1, height })),
                28_600,
                800,
            ),
        );
    });
});

describe('offsetKeptAcrossRuns', () => {
    it('keeps the reader where they were when a row above them folds', () => {
        // Row 1 folds from a hundred pixels to forty while the reader stands thirty pixels into row 3.
        const kept = offsetKeptAcrossRuns(330, runsOf([100, 100, 100, 100]), runsOf([100, 40, 100, 100]));

        expect(kept).toBe(270);
        expect(leadingRowIn(runsOf([100, 40, 100, 100]), kept)).toBe(3);
    });

    it('keeps the reader where they were when a row above them unfolds', () => {
        expect(offsetKeptAcrossRuns(270, runsOf([100, 40, 100, 100]), runsOf([100, 100, 100, 100]))).toBe(330);
    });

    it('keeps the share of the reader’s own row above the top of the scroller when that row changes height', () => {
        expect(offsetKeptAcrossRuns(150, runsOf([100, 100, 100]), runsOf([100, 40, 100]))).toBe(120);
    });

    it('moves nothing where only rows below the reader changed height', () => {
        expect(offsetKeptAcrossRuns(30, runsOf([100, 100, 100]), runsOf([100, 40, 40]))).toBe(30);
    });

    it('keeps the offset where there were no rows to measure it against', () => {
        expect(offsetKeptAcrossRuns(250, [], runsOf([100]))).toBe(250);
    });
});

describe('measuredRowHeights', () => {
    it('takes each shape from the first drawn row of it', () => {
        expect(
            measuredRowHeights(estimatedRowHeights, [
                ['whole', 95],
                ['folded', 40],
                ['whole', 96],
            ]),
        ).toStrictEqual({ whole: 95, folded: 40 });
    });

    it('keeps the estimate of a shape no drawn row has, and passes over a row with no height yet', () => {
        expect(
            measuredRowHeights(estimatedRowHeights, [
                ['folded', 0],
                ['whole', 95],
            ]),
        ).toStrictEqual({ whole: 95, folded: estimatedRowHeights.folded });
    });

    it('answers the heights it was given where nothing measured differently', () => {
        const current = { whole: 95, folded: 40 };

        expect(measuredRowHeights(current, [['whole', 95]])).toBe(current);
    });
});
