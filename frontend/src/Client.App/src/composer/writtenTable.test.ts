// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    cellAfter,
    cellBefore,
    columnsOf,
    mostTableColumns,
    mostTableRows,
    newTable,
    tableWithinBounds,
    withColumnInserted,
    withColumnRemoved,
    withRowAppended,
    withRowInserted,
    withRowRemoved,
} from './writtenTable';
import type { WrittenMarkup, WrittenNode } from './writtenText';

interface Spans {
    readonly columnSpan?: number;
    readonly rowSpan?: number;
}

function cell(text: string, spans: Spans = {}, element: 'td' | 'th' = 'td'): WrittenMarkup {
    return { element, address: null, ...spans, holds: text === '' ? [] : [{ text }] };
}

function header(text: string): WrittenMarkup {
    return cell(text, {}, 'th');
}

function table(...rows: readonly (readonly WrittenMarkup[])[]): WrittenMarkup {
    return {
        element: 'table',
        address: null,
        holds: rows.map((cells) => ({ element: 'tr', address: null, holds: cells })),
    };
}

// A table read back as the words of its cells, a header cell marked `#` and a span as `:c<n>` or `:r<n>`, which is
// what each edit is about and a deep comparison of nested records would bury.
function grid(written: WrittenMarkup | null): readonly (readonly string[])[] | null {
    return written === null
        ? null
        : written.holds.map((row) =>
              !('holds' in row)
                  ? []
                  : row.holds.map((held) => {
                        if (!('holds' in held)) {
                            return 'text' in held ? held.text : '';
                        }

                        const words = held.holds.map((node: WrittenNode) => ('text' in node ? node.text : '')).join('');

                        return `${held.element === 'th' ? '#' : ''}${words}${
                            held.columnSpan === undefined ? '' : `:c${held.columnSpan.toFixed(0)}`
                        }${held.rowSpan === undefined ? '' : `:r${held.rowSpan.toFixed(0)}`}`;
                    }),
          );
}

function sized(rows: number, columns: number): WrittenMarkup {
    return newTable(rows, columns, false);
}

describe('newTable', () => {
    it('makes empty cells, the first row of them header cells when asked', () => {
        expect(grid(newTable(2, 3, true))).toEqual([
            ['#', '#', '#'],
            ['', '', ''],
        ]);
        expect(grid(newTable(1, 2, false))).toEqual([['', '']]);
    });
});

describe('withRowInserted', () => {
    const plain = table([cell('a'), cell('b')], [cell('c'), cell('d')]);

    it('puts an empty row below the caret, which stays in its cell', () => {
        const edit = withRowInserted(plain, { row: 0, cell: 1 }, 'after');

        expect(grid(edit.table)).toEqual([
            ['a', 'b'],
            ['', ''],
            ['c', 'd'],
        ]);
        expect(edit.caret).toEqual({ row: 0, cell: 1 });
    });

    it('puts an empty row above the caret, which moves down with its cell', () => {
        const edit = withRowInserted(plain, { row: 1, cell: 0 }, 'before');

        expect(grid(edit.table)).toEqual([
            ['a', 'b'],
            ['', ''],
            ['c', 'd'],
        ]);
        expect(edit.caret).toEqual({ row: 2, cell: 0 });
    });

    it('makes a row of header cells only inside the header rows', () => {
        const headed = table([header('Name'), header('Price')], [cell('Tea'), cell('3')]);

        expect(grid(withRowInserted(headed, { row: 0, cell: 0 }, 'before').table)).toEqual([
            ['#', '#'],
            ['#Name', '#Price'],
            ['Tea', '3'],
        ]);
        expect(grid(withRowInserted(headed, { row: 0, cell: 0 }, 'after').table)).toEqual([
            ['#Name', '#Price'],
            ['', ''],
            ['Tea', '3'],
        ]);
    });

    it('grows a cell spanning down past the new row rather than cutting it', () => {
        const spanning = table([cell('a', { rowSpan: 2 }), cell('b')], [cell('c')]);

        expect(grid(withRowInserted(spanning, { row: 0, cell: 1 }, 'after').table)).toEqual([
            ['a:r3', 'b'],
            [''],
            ['c'],
        ]);
    });

    it('lands below every row a spanning caret cell covers', () => {
        const spanning = table([cell('a', { rowSpan: 2 }), cell('b')], [cell('c')]);

        expect(grid(withRowInserted(spanning, { row: 0, cell: 0 }, 'after').table)).toEqual([
            ['a:r2', 'b'],
            ['c'],
            ['', ''],
        ]);
    });

    it('adds nothing to a table already at the most rows', () => {
        const full = sized(mostTableRows, 2);

        expect(withRowInserted(full, { row: 0, cell: 0 }, 'after').table).toBe(full);
    });
});

describe('withRowAppended', () => {
    it('adds a row after the last and puts the caret in its first cell', () => {
        const edit = withRowAppended(table([cell('a'), cell('b')]));

        expect(grid(edit.table)).toEqual([
            ['a', 'b'],
            ['', ''],
        ]);
        expect(edit.caret).toEqual({ row: 1, cell: 0 });
    });

    it('adds nothing past the most rows and leaves the caret where it was', () => {
        const full = sized(mostTableRows, 1);

        expect(withRowAppended(full)).toEqual({ table: full, caret: null });
    });
});

describe('withColumnInserted', () => {
    const plain = table([cell('a'), cell('b')], [cell('c'), cell('d')]);

    it('puts an empty cell before the caret in every row, and the caret moves along with its cell', () => {
        const edit = withColumnInserted(plain, { row: 1, cell: 1 }, 'before');

        expect(grid(edit.table)).toEqual([
            ['a', '', 'b'],
            ['c', '', 'd'],
        ]);
        expect(edit.caret).toEqual({ row: 1, cell: 2 });
    });

    it('puts an empty cell after the caret, which stays where it is', () => {
        const edit = withColumnInserted(plain, { row: 0, cell: 1 }, 'after');

        expect(grid(edit.table)).toEqual([
            ['a', 'b', ''],
            ['c', 'd', ''],
        ]);
        expect(edit.caret).toEqual({ row: 0, cell: 1 });
    });

    it('makes the new cell of a header row a header cell', () => {
        const headed = table([header('Name')], [cell('Tea')]);

        expect(grid(withColumnInserted(headed, { row: 1, cell: 0 }, 'after').table)).toEqual([
            ['#Name', '#'],
            ['Tea', ''],
        ]);
    });

    it('grows a cell spanning across the new column rather than cutting it', () => {
        const spanning = table([cell('a', { columnSpan: 2 })], [cell('b'), cell('c')]);

        expect(grid(withColumnInserted(spanning, { row: 1, cell: 0 }, 'after').table)).toEqual([
            ['a:c3'],
            ['b', '', 'c'],
        ]);
    });

    it('places the new cell by column where a cell from above takes a row’s first slot', () => {
        const spanning = table([cell('a', { rowSpan: 2 }), cell('b')], [cell('c')]);

        expect(grid(withColumnInserted(spanning, { row: 0, cell: 1 }, 'after').table)).toEqual([
            ['a:r2', 'b', ''],
            ['c', ''],
        ]);
    });

    it('adds nothing to a table already at the most columns', () => {
        const full = sized(1, mostTableColumns);

        expect(withColumnInserted(full, { row: 0, cell: 0 }, 'after').table).toBe(full);
    });
});

describe('withRowRemoved', () => {
    it('removes the caret’s row and lets the caret leave the table', () => {
        const edit = withRowRemoved(table([cell('a')], [cell('b')]), { row: 0, cell: 0 });

        expect(grid(edit.table)).toEqual([['b']]);
        expect(edit.caret).toBeNull();
    });

    it('removes the table with its last row', () => {
        expect(withRowRemoved(table([cell('a'), cell('b')]), { row: 0, cell: 1 })).toEqual({
            table: null,
            caret: null,
        });
    });

    it('carries a cell spanning further down into the next row, in its own column', () => {
        const spanning = table([cell('a', { rowSpan: 2 }), cell('b')], [cell('c')]);

        expect(grid(withRowRemoved(spanning, { row: 0, cell: 1 }).table)).toEqual([['a', 'c']]);
    });

    it('shortens a cell from above that reached into the removed row', () => {
        const spanning = table([cell('a', { rowSpan: 2 }), cell('b')], [cell('c')]);

        expect(grid(withRowRemoved(spanning, { row: 1, cell: 0 }).table)).toEqual([['a', 'b']]);
    });
});

describe('withColumnRemoved', () => {
    it('removes the caret’s column and keeps the caret in its row', () => {
        const edit = withColumnRemoved(table([cell('a'), cell('b')], [cell('c'), cell('d')]), { row: 1, cell: 1 });

        expect(grid(edit.table)).toEqual([['a'], ['c']]);
        expect(edit.caret).toEqual({ row: 1, cell: 0 });
    });

    it('removes the table with its last column', () => {
        expect(withColumnRemoved(table([cell('a')], [cell('b')]), { row: 0, cell: 0 })).toEqual({
            table: null,
            caret: null,
        });
    });

    it('narrows a cell spanning the removed column rather than removing it', () => {
        const spanning = table([cell('a', { columnSpan: 2 })], [cell('b'), cell('c')]);

        expect(grid(withColumnRemoved(spanning, { row: 1, cell: 1 }).table)).toEqual([['a'], ['b']]);
    });

    it('removes a row the removed column leaves with no cell', () => {
        const ragged = table([cell('a')], [cell('b'), cell('c')]);

        expect(grid(withColumnRemoved(ragged, { row: 1, cell: 0 }).table)).toEqual([['c']]);
    });
});

describe('cellAfter and cellBefore', () => {
    const plain = table([cell('a'), cell('b')], [cell('c')]);

    it('moves along a row and on to the next', () => {
        expect(cellAfter(plain, { row: 0, cell: 0 })).toEqual({ row: 0, cell: 1 });
        expect(cellAfter(plain, { row: 0, cell: 1 })).toEqual({ row: 1, cell: 0 });
        expect(cellBefore(plain, { row: 1, cell: 0 })).toEqual({ row: 0, cell: 1 });
    });

    it('answers nothing past the last cell or before the first', () => {
        expect(cellAfter(plain, { row: 1, cell: 0 })).toBeNull();
        expect(cellBefore(plain, { row: 0, cell: 0 })).toBeNull();
    });
});

describe('tableWithinBounds', () => {
    it('takes a table at the most rows and the most columns', () => {
        expect(tableWithinBounds(sized(mostTableRows, mostTableColumns))).toBe(true);
    });

    it('refuses one row or one column past a bound', () => {
        expect(tableWithinBounds(sized(mostTableRows + 1, 1))).toBe(false);
        expect(tableWithinBounds(sized(1, mostTableColumns + 1))).toBe(false);
    });

    it('counts a column span toward the columns', () => {
        const wide = table([cell('a', { columnSpan: mostTableColumns }), cell('b')]);

        expect(columnsOf(wide)).toBe(mostTableColumns + 1);
        expect(tableWithinBounds(wide)).toBe(false);
    });

    // A pasted span may say a thousand, and laying every one of those columns out before refusing the table is what
    // would freeze the composer.
    it('lays a span out no further than one column past the bound, and refuses the table it widens', () => {
        const pasted = table(
            ...Array.from({ length: mostTableRows }, () => [cell('a', { columnSpan: 1000, rowSpan: mostTableRows })]),
        );

        expect(columnsOf(pasted)).toBe(mostTableColumns + 1);
        expect(tableWithinBounds(pasted)).toBe(false);
    });

    it('refuses a row holding more cells than the table may have columns', () => {
        expect(tableWithinBounds(table(Array.from({ length: mostTableColumns + 1 }, () => cell('a'))))).toBe(false);
    });

    it('refuses a span reaching past the last row, and a table with no row', () => {
        expect(tableWithinBounds(table([cell('a', { rowSpan: 2 })]))).toBe(false);
        expect(tableWithinBounds(table())).toBe(false);
    });
});
