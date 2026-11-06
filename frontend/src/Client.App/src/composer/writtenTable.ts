// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { WrittenMarkup, WrittenNode } from './writtenText';

// A table in the composer's closed tree, and every edit the table's controls make to one, as functions from a table to
// the table that replaces it.
//
// **No edit here touches a document.** ADR 0047 has `execCommand` keep what it does inside the closed set and has
// every structural edit be a function over the tree, whose answer the editor writes back over the old table through
// `insertHTML` — one step on the engine's own undo stack. So each function here takes a table and a cell and answers a
// new table, or `null` where the edit leaves no table at all, and the editor does the rest.
//
// **The grid is laid out the way a browser lays one out.** A cell's column is not its index in its row: a cell above
// it spanning down, or one beside it spanning across, pushes it along. Rows and columns are therefore counted on the
// laid-out grid, and an edit that lands inside a span grows the span rather than cutting a cell in two.

/** The most rows a table the composer writes may have, which ADR 0047 states. */
export const mostTableRows = 200;

/** The most columns a table the composer writes may have, which ADR 0047 states. */
export const mostTableColumns = 20;

/** Where a cell is in its table: its row, and its index among that row's own cells. */
export interface TableCellPosition {
    readonly row: number;
    readonly cell: number;
}

/** What an edit leaves: the table to write in place of the old one, or none, and the cell the caret goes to. */
export interface TableEdit {
    readonly table: WrittenMarkup | null;
    readonly caret: TableCellPosition | null;
}

/** Which side of a row or a column an inserted one lands on. */
export type TableSide = 'before' | 'after';

// One cell placed on the grid: the row it starts in, its index in that row, its first column, and how far it spans
// after the table's own extent has bounded it.
interface PlacedCell {
    readonly row: number;
    readonly cell: number;
    readonly column: number;
    readonly columnSpan: number;
    readonly rowSpan: number;
}

interface TableLayout {
    readonly columns: number;
    readonly placed: readonly (readonly PlacedCell[])[];

    /** Which placed cell covers each slot of each row, `undefined` for a slot no cell reaches. */
    readonly covering: readonly (readonly (PlacedCell | undefined)[])[];
}

/** The rows a table holds, in order. */
export function rowsOf(table: WrittenMarkup): readonly WrittenMarkup[] {
    return table.holds.filter(isMarkup);
}

/** The cells a row holds, in order. */
export function cellsOf(row: WrittenMarkup): readonly WrittenMarkup[] {
    return row.holds.filter(isMarkup);
}

/** How many columns a table's grid has, spans included. */
export function columnsOf(table: WrittenMarkup): number {
    return laidOut(rowsOf(table)).columns;
}

/** A new table of empty cells, the first row a header row when asked. */
export function newTable(rows: number, columns: number, headed: boolean): WrittenMarkup {
    return wrapping(
        'table',
        Array.from({ length: rows }, (_, row) =>
            wrapping(
                'tr',
                Array.from({ length: columns }, () => wrapping(headed && row === 0 ? 'th' : 'td', [])),
            ),
        ),
    );
}

/** Whether a table may take one more row, which is the bound and nothing else. */
export function rowAddable(table: WrittenMarkup): boolean {
    return rowsOf(table).length < mostTableRows;
}

/** Whether a table may take one more column. */
export function columnAddable(table: WrittenMarkup): boolean {
    return columnsOf(table) < mostTableColumns;
}

/**
 * A row inserted above or below the one the caret's cell stands in. Below a cell spanning several rows means below
 * the last of them, and a cell the new row lands inside grows by it rather than being cut.
 */
export function withRowInserted(table: WrittenMarkup, at: TableCellPosition, side: TableSide): TableEdit {
    const rows = rowsOf(table);
    const placed = laidOut(rows).placed[at.row]?.[at.cell];

    if (placed === undefined || !rowAddable(table)) {
        return { table, caret: at };
    }

    const landing = side === 'before' ? placed.row : placed.row + placed.rowSpan;

    return {
        table: rowInserted(table, landing),
        caret: { row: side === 'before' ? at.row + 1 : at.row, cell: at.cell },
    };
}

/** A row added after the last, which is what Tab in the last cell asks for, with the caret in its first cell. */
export function withRowAppended(table: WrittenMarkup): TableEdit {
    const rows = rowsOf(table).length;

    return rowAddable(table)
        ? { table: rowInserted(table, rows), caret: { row: rows, cell: 0 } }
        : { table, caret: null };
}

/** A column inserted before or after the caret's cell, a cell spanning the place it lands growing by it. */
export function withColumnInserted(table: WrittenMarkup, at: TableCellPosition, side: TableSide): TableEdit {
    const rows = rowsOf(table);
    const layout = laidOut(rows);
    const placed = layout.placed[at.row]?.[at.cell];

    if (placed === undefined || !columnAddable(table)) {
        return { table, caret: at };
    }

    const landing = side === 'before' ? placed.column : placed.column + placed.columnSpan;
    const edited = rows.map((row, rowIndex) => {
        const covering = layout.covering[rowIndex]?.[landing];

        // A cell starting left of the new column and reaching past it grows by it, once, in the row it starts in.
        if (covering !== undefined && covering.column < landing) {
            return cellsOf(row).map((cell, cellIndex) =>
                cellIndex === covering.cell && covering.row === rowIndex
                    ? spanned(cell, covering.columnSpan + 1, covering.rowSpan)
                    : cell,
            );
        }

        const cells = [...cellsOf(row)];
        const index = (layout.placed[rowIndex] ?? []).filter((cell) => cell.column < landing).length;

        cells.splice(index, 0, wrapping(headerRow(row) ? 'th' : 'td', []));

        return cells;
    });

    // A column before the caret's cell is a new cell ahead of it in its own row; one after leaves its index alone.
    return {
        table: withRows(table, rows, edited),
        caret: { row: at.row, cell: side === 'before' ? at.cell + 1 : at.cell },
    };
}

/**
 * The caret's row removed. A cell starting in it and spanning further down moves down into the next row, a cell from
 * above reaching into it shrinks, and removing the last row removes the table.
 */
export function withRowRemoved(table: WrittenMarkup, at: TableCellPosition): TableEdit {
    const rows = rowsOf(table);

    if (rows.length <= 1) {
        return { table: null, caret: null };
    }

    return { table: rowRemoved(table, at.row), caret: null };
}

/**
 * The caret's column removed: a cell spanning it shrinks and a cell standing in it alone goes, a row left with no cell
 * goes with it, and removing the last column removes the table.
 */
export function withColumnRemoved(table: WrittenMarkup, at: TableCellPosition): TableEdit {
    const rows = rowsOf(table);
    const layout = laidOut(rows);
    const placed = layout.placed[at.row]?.[at.cell];

    if (placed === undefined) {
        return { table, caret: at };
    }

    if (layout.columns <= 1) {
        return { table: null, caret: null };
    }

    const column = placed.column;
    const edited = rows.map((row, rowIndex) =>
        cellsOf(row).flatMap((cell, cellIndex) => {
            const laid = layout.placed[rowIndex]?.[cellIndex];

            if (laid === undefined || column < laid.column || column >= laid.column + laid.columnSpan) {
                return [cell];
            }

            return laid.columnSpan > 1 ? [spanned(cell, laid.columnSpan - 1, laid.rowSpan)] : [];
        }),
    );

    let remaining: WrittenMarkup = withRows(table, rows, edited);

    // A row left holding no cell of its own is a row nobody can put a caret in, so it goes as a removed row would.
    for (let index = rowsOf(remaining).length - 1; index >= 0; index -= 1) {
        const row = rowsOf(remaining)[index];

        if (row !== undefined && cellsOf(row).length === 0) {
            remaining = rowRemoved(remaining, index);
        }
    }

    const caretRow = Math.min(at.row, rowsOf(remaining).length - 1);
    const caretCells = cellsOf(rowsOf(remaining)[caretRow] ?? wrapping('tr', [])).length;

    return {
        table: remaining,
        caret: caretCells === 0 ? null : { row: caretRow, cell: Math.min(at.cell, caretCells - 1) },
    };
}

/** The cell Tab moves to, or `null` past the last one, which is where Tab adds a row instead. */
export function cellAfter(table: WrittenMarkup, at: TableCellPosition): TableCellPosition | null {
    const rows = rowsOf(table);

    for (let row = at.row; row < rows.length; row += 1) {
        const cell = row === at.row ? at.cell + 1 : 0;

        if (cell < cellsOf(rows[row] ?? wrapping('tr', [])).length) {
            return { row, cell };
        }
    }

    return null;
}

/** The cell Shift+Tab moves to, or `null` before the first one. */
export function cellBefore(table: WrittenMarkup, at: TableCellPosition): TableCellPosition | null {
    const rows = rowsOf(table);

    for (let row = at.row; row >= 0; row -= 1) {
        const cells = cellsOf(rows[row] ?? wrapping('tr', [])).length;
        const cell = row === at.row ? at.cell - 1 : cells - 1;

        if (cell >= 0 && cell < cells) {
            return { row, cell };
        }
    }

    return null;
}

/**
 * Whether a table is one the composer may write: within both bounds, every row holding a cell, and no span reaching
 * past its last row.
 * A table that is not is unwrapped to its cells' contents when it is read, and refused when a stored one holds it.
 */
export function tableWithinBounds(table: WrittenMarkup): boolean {
    const rows = rowsOf(table);

    // A row's cells stand in columns of their own, so a row holding more of them than the bound is refused before
    // anything is laid out.
    if (
        rows.length === 0 ||
        rows.length > mostTableRows ||
        rows.some((row) => cellsOf(row).length > mostTableColumns)
    ) {
        return false;
    }

    return (
        laidOut(rows).columns <= mostTableColumns &&
        rows.every(
            (row, rowIndex) =>
                cellsOf(row).length > 0 && cellsOf(row).every((cell) => rowIndex + (cell.rowSpan ?? 1) <= rows.length),
        )
    );
}

/** Where each cell of each row stands on the table's grid: the column it starts in and how many it spans there. */
export function placementsOf(
    table: WrittenMarkup,
): readonly (readonly { readonly column: number; readonly columnSpan: number }[])[] {
    return laidOut(rowsOf(table)).placed;
}

/**
 * The table with each cell's column span narrowed to the grid its cells start in: columns past the last one any cell
 * begins at hold nothing, so a span written into them — a whole-row `colspan="100%"`, say — reaches that last column.
 */
export function withSpansWithinTheGrid(table: WrittenMarkup): WrittenMarkup {
    const rows = rowsOf(table);
    const { placed } = laidOut(rows);
    const width = Math.max(0, ...placed.flat().map((laid) => laid.column + 1));

    return withRows(
        table,
        rows,
        rows.map((row, rowIndex) =>
            cellsOf(row).map((cell, cellIndex) => {
                const column = placed[rowIndex]?.[cellIndex]?.column ?? 0;

                return spanned(cell, Math.min(cell.columnSpan ?? 1, width - column), cell.rowSpan ?? 1);
            }),
        ),
    );
}

/** A cell with the spans given, each written only where it is more than one. */
export function spanned(cell: WrittenMarkup, columnSpan: number, rowSpan: number): WrittenMarkup {
    const unspanned: { -readonly [Key in keyof WrittenMarkup]: WrittenMarkup[Key] } = { ...cell };

    delete unspanned.columnSpan;
    delete unspanned.rowSpan;

    return {
        ...unspanned,
        ...(columnSpan > 1 ? { columnSpan } : {}),
        ...(rowSpan > 1 ? { rowSpan } : {}),
    };
}

function rowInserted(table: WrittenMarkup, landing: number): WrittenMarkup {
    const rows = rowsOf(table);
    const layout = laidOut(rows);
    const following = rows[landing];
    const element = following !== undefined && headerRow(following) ? 'th' : 'td';
    const added: WrittenMarkup[] = [];
    const grown = new Set<PlacedCell>();

    for (let column = 0; column < layout.columns; column += 1) {
        const covering = layout.covering[landing]?.[column];

        // A cell from above reaching down past the new row grows into it, so its columns take no new cell.
        if (covering !== undefined && covering.row < landing) {
            grown.add(covering);
        } else {
            added.push(wrapping(element, []));
        }
    }

    const edited = rows.map((row, rowIndex) =>
        cellsOf(row).map((cell, cellIndex) => {
            const laid = layout.placed[rowIndex]?.[cellIndex];

            return laid !== undefined && grown.has(laid) ? spanned(cell, laid.columnSpan, laid.rowSpan + 1) : cell;
        }),
    );

    edited.splice(landing, 0, added);

    return withRows(table, [...rows.slice(0, landing), wrapping('tr', []), ...rows.slice(landing)], edited);
}

function rowRemoved(table: WrittenMarkup, removed: number): WrittenMarkup {
    const rows = rowsOf(table);
    const layout = laidOut(rows);
    const edited = rows.map((row, rowIndex) =>
        cellsOf(row).map((cell, cellIndex) => {
            const laid = layout.placed[rowIndex]?.[cellIndex];

            // A cell from above reaching into the removed row is one row shorter.
            return laid !== undefined && laid.row < removed && laid.row + laid.rowSpan > removed
                ? spanned(cell, laid.columnSpan, laid.rowSpan - 1)
                : cell;
        }),
    );

    // A cell starting in the removed row and reaching further down carries on in the next row, where its column puts it.
    const next = removed + 1;
    const carried = (layout.placed[removed] ?? []).filter((laid) => laid.rowSpan > 1);
    const nextCells = [...(edited[next] ?? [])];

    for (const laid of [...carried].reverse()) {
        const cell = cellsOf(rows[removed] ?? wrapping('tr', []))[laid.cell];

        if (cell === undefined) {
            continue;
        }

        const index = (layout.placed[next] ?? []).filter((placed) => placed.column < laid.column).length;

        nextCells.splice(index, 0, spanned(cell, laid.columnSpan, laid.rowSpan - 1));
    }

    if (next < edited.length) {
        edited[next] = nextCells;
    }

    edited.splice(removed, 1);

    return withRows(
        table,
        rows.filter((_, rowIndex) => rowIndex !== removed),
        edited,
    );
}

// A grid is laid out no wider than one column past the bound: that is enough to tell a table past it, and a span a
// paste wrote as a thousand then costs the walk no more than that.
const widestLaidOut = mostTableColumns + 1;

function laidOut(rows: readonly WrittenMarkup[]): TableLayout {
    const covering: (PlacedCell | undefined)[][] = rows.map(() => []);
    const placed = rows.map((row, rowIndex) => {
        let column = 0;

        return cellsOf(row).map((cell, cellIndex) => {
            while (covering[rowIndex]?.[column] !== undefined) {
                column += 1;
            }

            const laid: PlacedCell = {
                row: rowIndex,
                cell: cellIndex,
                column,
                columnSpan: Math.min(widestLaidOut, Math.max(1, cell.columnSpan ?? 1)),
                rowSpan: Math.max(1, Math.min(cell.rowSpan ?? 1, rows.length - rowIndex)),
            };
            const spanEnd = Math.min(column + laid.columnSpan, widestLaidOut);

            for (let spannedRow = rowIndex; spannedRow < rowIndex + laid.rowSpan; spannedRow += 1) {
                for (let spannedColumn = column; spannedColumn < spanEnd; spannedColumn += 1) {
                    const slots = covering[spannedRow];

                    if (slots !== undefined) {
                        slots[spannedColumn] = laid;
                    }
                }
            }

            column += laid.columnSpan;

            return laid;
        });
    });

    return { columns: Math.max(0, ...covering.map((slots) => slots.length)), placed, covering };
}

// Rows written back with new cells, each keeping everything else the row carried.
function withRows(
    table: WrittenMarkup,
    rows: readonly WrittenMarkup[],
    cells: readonly (readonly WrittenMarkup[])[],
): WrittenMarkup {
    return { ...table, holds: rows.map((row, index) => ({ ...row, holds: cells[index] ?? [] })) };
}

// A header row is a row of header cells, so a row with any other cell in it is not one.
function headerRow(row: WrittenMarkup): boolean {
    const cells = cellsOf(row);

    return cells.length > 0 && cells.every((cell) => cell.element === 'th');
}

function isMarkup(node: WrittenNode): node is WrittenMarkup {
    return 'holds' in node;
}

function wrapping(element: 'table' | 'tr' | 'th' | 'td', holds: readonly WrittenNode[]): WrittenMarkup {
    return { element, address: null, holds };
}
