// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { cellsOf, mostTableColumns, mostTableRows, rowsOf, spanned, tableWithinBounds } from './writtenTable';

// What somebody has written, as a closed tree rather than as markup, and the two readings a message going out needs
// from it: the HTML part and the plain-text alternative beside it.
//
// **Nothing here parses a string into nodes.** ADR 0024 states that for a message arriving, and the reasoning holds
// just as firmly for one going out: the composer reads its own editable region node by node into the closed set below,
// renders that set back as ordinary elements, and writes the HTML part by escaping every value it puts in one. The one
// markup the editor is ever handed is that same escaped writing of this set, through `editing.ts` under ADR 0047, and
// the one foreign markup ever parsed is a paste's, into an inert document `clipboardReading.ts` reads into this set and
// lets go of. So no sanitizer is pinned, and what a paste or a poisoned session store could put on the screen is
// bounded by what this type can express rather than by what a filter remembered to strip.
//
// **A picture is a name, never an address.** The tree refers to a picture the composer holds by the name it minted for
// it, under ADR 0047 Question 2, and how that picture is written is what a reading of the tree is handed: an object
// URL the composer issued and the picture's name as its alternative text, for the editor, and `cid:` and the name, for
// the HTML part. So a remote address can never become a picture.
//
// The set is DOM-shaped rather than semantic, and deliberately so. What produces it is a browser editing a
// `contenteditable` region, and each engine wraps a line, a list, and an emphasis its own way — a semantic block model
// would have to guess which of those it was looking at, and would lose whatever it guessed wrong. Reading the shape
// that is actually there loses nothing, and closing the *set* is what makes it safe.

import { drawnSizeOf, isPictureName, isPictureSide, widestSentPicture } from './pictures';

/** The elements a written message may be made of, which is what the formatting controls produce and nothing else. */
export type WrittenElement =
    | 'a'
    | 'b'
    | 'blockquote'
    | 'br'
    | 'div'
    | 'em'
    | 'i'
    | 'li'
    | 'ol'
    | 'p'
    | 's'
    | 'span'
    | 'strike'
    | 'strong'
    | 'table'
    | 'td'
    | 'th'
    | 'tr'
    | 'u'
    | 'ul';

/** A marked step of the size scale ADR 0047 states. Normal is the absence of one rather than a fifth value. */
export type WrittenSize = 'small' | 'large' | 'larger' | 'largest';

/** How a block places its lines. Start and end rather than left and right, because that is what the document says. */
export type WrittenAlignment = 'start' | 'center' | 'end' | 'justify';

/** A run of text somebody typed. */
export interface WrittenText {
    readonly text: string;
}

/** One element around what it holds. */
export interface WrittenMarkup {
    readonly element: WrittenElement;

    /** Where a link goes, which only a link carries and which is always one this client would follow. */
    readonly address: string | null;

    /** A run's colour, as an opaque `#rrggbb` in lower case, which only a `span` carries. */
    readonly colour?: string;

    /** A run's step of the size scale, which only a `span` carries. */
    readonly size?: WrittenSize;

    /** How a block places its lines, which only a paragraph, a line, a list item, and a table cell carry. */
    readonly alignment?: WrittenAlignment;

    /** A table cell's fill, as an opaque `#rrggbb` in lower case. */
    readonly background?: string;

    /** How many columns and rows a table cell spans, each carried only where it is more than one. */
    readonly columnSpan?: number;
    readonly rowSpan?: number;

    readonly holds: readonly WrittenNode[];
}

/** Who a written tree is spelled for: the HTML part of a message going out, or the composer's own editable region. */
export type WrittenDestination = 'mail' | 'editor';

/**
 * A picture the composer holds, by the name it minted for it, at the pixel size it is drawn at — the size its octets
 * state, or a smaller one its grip gave it, never a larger one.
 */
export interface WrittenPicture {
    readonly element: 'img';
    readonly picture: string;
    readonly width: number;
    readonly height: number;
}

export type WrittenNode = WrittenText | WrittenMarkup | WrittenPicture;

/**
 * The picture an address in the editor names, at the size its octets state, which only an object URL the composer
 * issued does.
 */
export type PictureAt = (source: string) => Omit<WrittenPicture, 'element'> | null;

/**
 * How a picture is written for the reading at hand, from the name the tree holds: its source, and the alternative text
 * it is named by, which the editor gives it so a screen reader names the picture somebody is writing beside, and the
 * HTML part does not, a file's name being nothing the recipient was meant to read.
 */
export type PictureSource = (picture: string) => { readonly address: string; readonly label: string | null };

const writableElements: readonly string[] = [
    'a',
    'b',
    'blockquote',
    'br',
    'div',
    'em',
    'i',
    'li',
    'ol',
    'p',
    's',
    'span',
    'strike',
    'strong',
    'table',
    'td',
    'th',
    'tr',
    'u',
    'ul',
];

// A table's own parts, which stand only where a table puts them: a row in a table, a cell in a row.
const tableParts: readonly string[] = ['table', 'td', 'th', 'tr'];

// What a table cell may not hold, being a block of its own. A cell holds what a paragraph holds — runs, colour, size,
// and line breaks — so in a cell each of these is read as the lines it held rather than kept.
const blocksOutsideACell: readonly string[] = ['blockquote', 'div', 'li', 'ol', 'p', 'ul', ...tableParts];

// A `span` an engine leaves in the region is its own bookkeeping — the rendering it carries a moved line over with —
// rather than an element anybody chose, so it is read only through the region's drawing (`RunDrawing`) and otherwise
// unwrapped. A run's colour and size are read off the `font` element the editing commands write, and the tree's
// `span` is that reading. A table's parts are read by a walk of their own, because where each may stand is part of
// what is read.
const readableElements: readonly string[] = writableElements.filter(
    (element) => element !== 'span' && !tableParts.includes(element),
);

const writtenSizes: readonly string[] = ['small', 'large', 'larger', 'largest'];
const writtenAlignments: readonly string[] = ['start', 'center', 'end', 'justify'];
const alignedElements: readonly string[] = ['div', 'li', 'p', 'td', 'th'];
const tableCells: readonly string[] = ['td', 'th'];

const lineBreak: WrittenMarkup = { element: 'br', address: null, holds: [] };

// The size scale as the HTML part writes it, the one place a step becomes a length.
const sentSizes: Readonly<Record<WrittenSize, string>> = {
    small: '12px',
    large: '18px',
    larger: '24px',
    largest: '32px',
};

// The size scale as `<font size>` spells it, which is how `fontSize` writes a step: ADR 0047's table, read both ways.
const fontSizeSteps: Readonly<Record<string, WrittenSize | null>> = {
    '1': 'small',
    '2': 'small',
    '3': null,
    '4': 'large',
    '5': 'larger',
    '6': 'largest',
    '7': 'largest',
};

/** The step of the size scale a `<font size>` reads as, or `null` for normal and for a size that is none of the seven. */
export function stepOfFontSize(size: string): WrittenSize | null {
    return ownEntry(fontSizeSteps, size.trim());
}

/** The `<font size>` the size control asks `fontSize` for, and the one the region is drawn back with. */
export const fontSizeOf: Readonly<Record<WrittenSize, string>> = {
    small: '2',
    large: '4',
    larger: '5',
    largest: '6',
};

// How an alignment is written into the HTML part and back into the region. Start and end are left and right because
// the composer writes no direction, and the engine Outlook renders with reads neither keyword.
const sentAlignments: Readonly<Record<WrittenAlignment, string>> = {
    start: 'left',
    center: 'center',
    end: 'right',
    justify: 'justify',
};

const readAlignments: Readonly<Record<string, WrittenAlignment>> = {
    left: 'start',
    start: 'start',
    center: 'center',
    right: 'end',
    end: 'end',
    justify: 'justify',
};

/** A colour the tree may carry, in the one spelling it carries it in, or `null` for anything that is not one. */
export function writtenColour(value: string): string | null {
    return /^#[0-9a-f]{6}$/iu.test(value) ? value.toLowerCase() : null;
}

/** The alignment a `text-align` value or an `align` attribute states, or `null` for one that is none of the four. */
export function writtenAlignmentOf(stated: string): WrittenAlignment | null {
    return ownEntry(readAlignments, stated.trim().toLowerCase());
}

/** How an alignment is drawn, which is the same `text-align` the HTML part writes. */
export function drawnAlignment(alignment: WrittenAlignment): string {
    return sentAlignments[alignment];
}

/**
 * How a table cell is drawn, as CSS declarations: the same in the HTML part and in the editor, so the grid somebody
 * fills in is the grid that is sent.
 *
 * Every cell carries one border, its padding, and its words at the top, which is what makes a table read as a grid in
 * every client that drops a style sheet. It always carries an alignment too — its own, or start where it has none —
 * because most clients centre a header cell by default and the composer draws one start-aligned like the cells below.
 */
export function cellDeclarationsOf(cell: WrittenMarkup): readonly (readonly [string, string])[] {
    const background = cell.background === undefined ? null : writtenColour(cell.background);

    return [
        ['border', '1px solid #d0d5dd'],
        ['padding', '4px 8px'],
        ['vertical-align', 'top'],
        ...(background === null ? [] : [['background-color', background] as const]),
        ['text-align', sentAlignments[cell.alignment ?? 'start']],
    ];
}

// The schemes a link written here may name. Everything else — `javascript:`, `data:`, a scheme nobody has heard of —
// is a link the composer refuses to make rather than one a reader has to be careful about, and a relative address is
// refused with them: what a message says is read on somebody else's machine, where this deployment's paths mean
// nothing.
const followableSchemes: readonly string[] = ['http:', 'https:', 'mailto:'];

// What a written message may carry. All three bound both directions rather than only the read, and that is the whole
// point of them: a message the composer wrote past one of them would be kept and then refused on the next reload, and
// `keptComposition.ts` answers nothing for a record it cannot read whole — so the recipients, the subject, and the
// account would go down with the words. `writtenIn` therefore holds to each of them as it reads, and each is set far
// above anything typing produces, so what a person actually meets is a paste of a whole document being cut.
export const mostWrittenNodes = 100_000;
export const longestWrittenRun = 100_000;
export const deepestWriting = 64;

/** Whether a link written here is one this client would make, which is a scheme question rather than a spelling one. */
export function followable(address: string): boolean {
    try {
        return followableSchemes.includes(new URL(address).protocol);
    } catch {
        // An address that is not one at all is not one to link to either.
        return false;
    }
}

/**
 * The address a paste makes a link to, which is pasted text that is exactly one address `followable` accepts, or
 * `null` for anything else — words around it, two addresses, or one this client would not link to.
 */
export function pastedAddress(pasted: string): string | null {
    const address = pasted.trim();

    // A URL parser takes a space inside a path as part of it, so an address with words after it would otherwise pass.
    return /\s/u.test(address) || !followable(address) ? null : address;
}

/**
 * What is written inside an element, read into the closed set above.
 *
 * An element outside the set is unwrapped rather than dropped, so what somebody typed inside one survives a wrapper
 * this client has no name for — which is what an engine leaves behind after an editing command it composed its own way.
 *
 * It holds to every bound `writtenTextIn` reads by, so what this composes is always what that accepts. A region past
 * one of them is cut here rather than kept whole, because the alternative is not a longer message: it is a message
 * kept, refused on the next reload, and taken down with the recipients and the subject it was written beside.
 *
 * @param parent What is read, which is the editable region or a part of it.
 * @param pictureAt Which picture an image's source names. An image whose source names none — a remote address, an
 *   object URL another page issued — is dropped, which is what keeps every picture in the tree one the composer holds.
 * @param drawing How the region draws a run, where the region is one that does — without it, every `span` an engine
 *   left is unwrapped.
 */
export function writtenIn(
    parent: Node,
    pictureAt: PictureAt,
    drawing: RunDrawing | null = null,
): readonly WrittenNode[] {
    return readWritten(parent, 0, { nodes: mostWrittenNodes, pictureAt }, 'flow', drawing);
}

/**
 * One table in the editable region, read exactly as `writtenIn` reads it, or `null` where what stands there is not a
 * table the composer holds — one past a bound, which that reading takes apart into its cells' words.
 */
export function writtenTableIn(table: Element, drawing: RunDrawing | null = null): WrittenMarkup | null {
    // A table's cells hold no picture, so nothing in one is asked which picture it names.
    const [read, ...more] = readTable(table, 0, { nodes: mostWrittenNodes, pictureAt: () => null }, 'flow', drawing);

    return read !== undefined && more.length === 0 && 'holds' in read && read.element === 'table' ? read : null;
}

/**
 * How a region draws a run's colour and size, read backwards.
 *
 * An engine moving a line into a block of its own — aligning, listing, or quoting a line that stood in the region
 * outside any — writes the `<font>` it moved as a `span` carrying the colour and the size the run was *drawn* at: the
 * colour as `rgb()`, and a length the step resolved to. Those are the region's own rendering of a
 * closed value rather than a value of their own, so the region says which closed value each one draws, and anything it
 * does not recognise — the colour of the text around a run, a length off the scale — is dropped as before. A `<font>`
 * colour is asked too, so a colour an engine wrote that is only the region's own is no colour of the run's.
 */
export interface RunDrawing {
    /** The colour a run drawn in this CSS colour was written in, or `null` where no run was written in it. */
    readonly colourDrawnAs: (drawn: string) => string | null;

    /** The step of the size scale a run drawn at this CSS length is, or `null` for normal and for no step at all. */
    readonly sizeDrawnAs: (drawn: string) => WrittenSize | null;
}

/** A colour as the CSS object model serializes an opaque one, `rgb(r, g, b)`, in the tree's own spelling. */
export function colourOfRgb(drawn: string): string | null {
    const channels = /^rgb\(\s*(\d{1,3}),\s*(\d{1,3}),\s*(\d{1,3})\s*\)$/u.exec(drawn.trim());

    if (channels === null) {
        return writtenColour(drawn.trim());
    }

    const hex = channels
        .slice(1)
        .map((channel) => Number(channel))
        .map((channel) => (channel > 255 ? null : channel.toString(16).padStart(2, '0')));

    return hex.includes(null) ? null : `#${hex.join('')}`;
}

// What is left of the one bound that is spent across a whole reading rather than measured node by node, and what the
// reading asks about a picture.
interface WritingBudget {
    nodes: number;
    readonly pictureAt: PictureAt;
}

// Where what is being read stands: in the message's own flow of blocks, or inside a table cell, which holds what a
// paragraph holds and nothing more.
type Placement = 'flow' | 'cell';

function readWritten(
    parent: Node,
    depth: number,
    budget: WritingBudget,
    within: Placement,
    drawing: RunDrawing | null,
): readonly WrittenNode[] {
    // Past the depth a kept message may be read back at, an element is unwrapped rather than kept.
    if (depth >= deepestWriting) {
        if (budget.nodes <= 0) {
            return [];
        }

        budget.nodes -= 1;

        return [{ text: (parent.textContent ?? '').slice(0, longestWrittenRun) }];
    }

    const written: WrittenNode[] = [];

    for (const node of [...parent.childNodes]) {
        if (budget.nodes <= 0) {
            break;
        }

        if (node.nodeType === Node.TEXT_NODE) {
            // A run past the bound is cut rather than dropped: what somebody pasted is mostly still there, and the
            // whole of it would have been lost on the reload that refused it.
            const text = (node.nodeValue ?? '').slice(0, longestWrittenRun);

            if (text !== '') {
                budget.nodes -= 1;
                written.push({ text });
            }

            continue;
        }

        if (!(node instanceof Element)) {
            continue;
        }

        const named = node.tagName.toLowerCase();

        // A cell holds no picture, so one an engine left in a cell is not read as one.
        if (named === 'img') {
            const source = node.getAttribute('src');
            const picture = source === null || within === 'cell' ? null : budget.pictureAt(source);

            if (picture !== null) {
                budget.nodes -= 1;
                written.push({ element: 'img', picture: picture.picture, ...drawnIn(node, picture) });
            }

            continue;
        }

        // A table needs three levels below it for its rows, its cells, and their words, so one too deep for them is
        // read as the words it holds, as any element past the depth is.
        if (named === 'table' && depth + 3 <= deepestWriting) {
            written.push(...readTable(node, depth, budget, within, drawing));

            continue;
        }

        // A block inside a cell is read as the lines it held: a break before it where words stand ahead of it, and one
        // after it unless it already ended on one.
        if (within === 'cell' && blocksOutsideACell.includes(named)) {
            const lines = readWritten(node, depth, budget, 'cell', drawing);
            const ahead = written[written.length - 1];

            if (lines.length > 0) {
                if (ahead !== undefined && !isBreak(ahead)) {
                    budget.nodes -= 1;
                    written.push(lineBreak);
                }

                written.push(...lines);

                if (!isBreak(lines[lines.length - 1] ?? lineBreak)) {
                    budget.nodes -= 1;
                    written.push(lineBreak);
                }
            }

            continue;
        }

        const run = runOf(node, named, drawing);
        const writable = readableElements.includes(named) || run !== null;
        // An engine merging a pasted line into the one at the caret carries that line's rendering over onto the
        // emphasis it keeps rather than onto a `span`, so what it carried is read as the run around that emphasis.
        const carried =
            run === null && emphasisElements.includes(named as WrittenElement) ? drawnRunOf(node, drawing) : null;

        // An element that is kept is one of the nodes the budget counts, and it is counted before what it holds is
        // read, so a wide tree cannot spend the whole budget on children and then add a parent past the bound. The
        // run carried around an emphasis is one more.
        if (writable) {
            budget.nodes -= carried === null ? 1 : 2;
        }

        const holds = readWritten(node, depth + 1, budget, within, drawing);

        if (!writable) {
            written.push(...holds);

            continue;
        }

        if (run !== null) {
            written.push({ element: 'span', address: null, ...run, holds });

            continue;
        }

        const element = named as WrittenElement;
        const address = element === 'a' ? node.getAttribute('href') : null;

        // A link to nowhere is a link this client did not make, so what it held is kept and the link itself is not.
        if (element === 'a' && (address === null || !followable(address))) {
            budget.nodes += 1;
            written.push(...holds);

            continue;
        }

        const alignment = alignedElements.includes(element) ? alignmentOf(node) : null;
        const read: WrittenMarkup =
            alignment === null ? { element, address, holds } : { element, address, alignment, holds };

        written.push(carried === null ? read : { element: 'span', address: null, ...carried, holds: [read] });
    }

    return written;
}

// The emphasis an engine may carry a run's rendering over onto, as it does a `span`.
const emphasisElements: readonly WrittenElement[] = ['b', 'em', 'i', 's', 'strike', 'strong', 'u'];

// The colour and the size an element's own style draws it in, read back through the region's drawing.
function drawnRunOf(
    element: Element,
    drawing: RunDrawing | null,
): { readonly colour?: string; readonly size?: WrittenSize } | null {
    if (drawing === null || !(element instanceof HTMLElement)) {
        return null;
    }

    const colour = element.style.color === '' ? null : drawing.colourDrawnAs(element.style.color);
    const size = element.style.fontSize === '' ? null : drawing.sizeDrawnAs(element.style.fontSize);

    return colour === null && size === null
        ? null
        : { ...(colour === null ? {} : { colour }), ...(size === null ? {} : { size }) };
}

// The colour and the size an element states as a run, each only where it is on the closed set: a `font` by the
// attributes the editing commands write, and a `span` by the rendering an engine copied onto it, read through the
// region's own drawing. A colour in some other notation and a size off the scale are dropped rather than approximated,
// and an element left with neither is unwrapped like any other this client has no name for.
function runOf(
    element: Element,
    named: string,
    drawing: RunDrawing | null,
): { readonly colour?: string; readonly size?: WrittenSize } | null {
    if (named === 'span') {
        return drawnRunOf(element, drawing);
    }

    if (named !== 'font') {
        return null;
    }

    const attributed = writtenColour(element.getAttribute('color') ?? '');
    const colour = attributed === null || drawing === null ? attributed : drawing.colourDrawnAs(attributed);
    const size = stepOfFontSize(element.getAttribute('size') ?? '');

    return colour === null && size === null
        ? null
        : { ...(colour === null ? {} : { colour }), ...(size === null ? {} : { size }) };
}

// The alignment a block states, written by the engine as `text-align` or, in an engine that still spells it the old
// way, as an `align` attribute. Anything else is no alignment at all.
function alignmentOf(block: Element): WrittenAlignment | null {
    const stated = block instanceof HTMLElement && block.style.textAlign !== '' ? block.style.textAlign : null;

    return writtenAlignmentOf(stated ?? block.getAttribute('align') ?? '');
}

// A table read with a value somebody else wrote answers for its own keys only, so `constructor` reads as nothing
// rather than as what every object inherits.
function ownEntry<Value>(table: Readonly<Record<string, Value>>, key: string): Value | null {
    return Object.hasOwn(table, key) ? (table[key] ?? null) : null;
}

// A table read row by row and cell by cell. A table inside a cell, and one past a bound, is not one the composer holds,
// so either is read as what its cells said instead — the grid is what is lost, never the words.
function readTable(
    table: Element,
    depth: number,
    budget: WritingBudget,
    within: Placement,
    drawing: RunDrawing | null,
): readonly WrittenNode[] {
    const rows: WrittenMarkup[] = [];

    budget.nodes -= 1;

    for (const row of table instanceof HTMLTableElement ? table.rows : []) {
        const cells: WrittenMarkup[] = [];

        for (const cell of row.cells) {
            if (budget.nodes <= 0) {
                break;
            }

            budget.nodes -= 1;
            cells.push(cellOf(cell, withoutTheLastBreak(readWritten(cell, depth + 3, budget, 'cell', drawing))));
        }

        if (cells.length > 0) {
            budget.nodes -= 1;
            rows.push({ element: 'tr', address: null, holds: cells });
        }
    }

    // A span reaching past the last row is one an engine left behind when it took rows away, and it is drawn to the
    // last row anyway, so it is read as reaching there rather than costing the table.
    const read: WrittenMarkup = {
        element: 'table',
        address: null,
        holds: rows.map((row, index) => ({
            ...row,
            holds: cellsOf(row).map((cell) =>
                spanned(cell, cell.columnSpan ?? 1, Math.min(cell.rowSpan ?? 1, rows.length - index)),
            ),
        })),
    };

    if (within === 'flow' && tableWithinBounds(read)) {
        return [read];
    }

    const contents = rowsOf(read)
        .flatMap(cellsOf)
        .map((cell) => cell.holds)
        .filter((holds) => holds.length > 0);

    return within === 'cell'
        ? contents.flatMap((holds) => [...holds, lineBreak])
        : contents.map((holds) => ({ element: 'div' as const, address: null, holds }));
}

// One cell with what it carries: its alignment where it is not the start every cell is drawn at anyway, its fill, and
// its spans where they are more than one.
function cellOf(cell: HTMLTableCellElement, holds: readonly WrittenNode[]): WrittenMarkup {
    const alignment = alignmentOf(cell);
    const background = backgroundOf(cell);

    return spanned(
        {
            element: cell.tagName.toLowerCase() === 'th' ? 'th' : 'td',
            address: null,
            ...(alignment === null || alignment === 'start' ? {} : { alignment }),
            ...(background === null ? {} : { background }),
            holds,
        },
        cell.colSpan,
        cell.rowSpan,
    );
}

// A cell's fill. The style an engine reports gives a colour back as `rgb()` whatever it was written as, and a fill with
// any transparency comes back as `rgba()`, which is no fill the tree carries.
function backgroundOf(cell: HTMLTableCellElement): string | null {
    return colourOfRgb(cell.style.backgroundColor) ?? writtenColour(cell.getAttribute('bgcolor') ?? '');
}

// An engine keeps a break at the end of a block so the block has a line to put the caret on, and draws no line for it,
// so the last break in a cell is that placeholder rather than one somebody typed.
function withoutTheLastBreak(holds: readonly WrittenNode[]): readonly WrittenNode[] {
    const last = holds[holds.length - 1];

    return last !== undefined && isBreak(last) ? holds.slice(0, -1) : holds;
}

function isBreak(node: WrittenNode): boolean {
    return !('text' in node) && node.element === 'br';
}

/**
 * The HTML part of a message going out, with every value in it escaped rather than trusted — or, for the editor, the
 * same tree in the spelling the region is drawn in, which is what `insertHTML` is handed under ADR 0047.
 *
 * The two differ only where the editing commands spell something their own way: a run's colour and size, which the
 * region holds as the `<font>` `foreColor` and `fontSize` write, and an empty cell, which the editor gives a break to
 * put the caret on — and a picture's source and name, which `pictureSource` writes. Everything else is one
 * serialization, so what somebody edits is what is sent.
 *
 * @param written The tree.
 * @param pictureSource How a picture is written: `cid:` and its name and no alternative text for the HTML part, and the
 *   object URL the composer issued for it and its name as alternative text in the editor.
 * @param destination Who the tree is spelled for.
 */
export function htmlOf(
    written: readonly WrittenNode[],
    pictureSource: PictureSource,
    destination: WrittenDestination = 'mail',
): string {
    return written.map((node) => markupOf(node, pictureSource, destination)).join('');
}

/** The names of the pictures a tree refers to, each once, in the order it first refers to them. */
export function picturesNamedIn(written: readonly WrittenNode[]): readonly string[] {
    const named = new Set<string>();

    const walk = (nodes: readonly WrittenNode[]): void => {
        for (const node of nodes) {
            if ('text' in node) {
                continue;
            }

            if (node.element === 'img') {
                named.add(node.picture);
            } else {
                walk(node.holds);
            }
        }
    };

    walk(written);

    return [...named];
}

/**
 * The plain-text alternative every message goes out with beside its HTML part.
 *
 * It is a reading of the same tree rather than a second thing to keep in step with it, which is why the composer holds
 * one message and not two: a pair that can disagree is a message whose two halves say different things to two readers.
 */
export function plainTextOf(written: readonly WrittenNode[]): string {
    return collapsed(textOf(written, null));
}

/**
 * A written message read back out of somewhere a person can write, which is what a session store is.
 *
 * Answers `null` for anything this client did not write, bounds included, because a message with a hole in it is worse
 * than an empty composer: it reaches the confirmation as words nobody typed.
 *
 * **A picture the kept pictures do not hold is dropped rather than refused**, as long as its name is one the composer
 * mints: that is exactly what a store that would not take the pictures leaves behind, and refusing the tree for it
 * would take the words, the recipients, and the subject down with the picture.
 *
 * @param value What was kept.
 * @param holdsPicture Whether a picture of this name was kept beside the tree.
 */
export function writtenTextIn(
    value: unknown,
    holdsPicture: (picture: string) => boolean,
): readonly WrittenNode[] | null {
    let remaining = mostWrittenNodes;

    function nodesIn(held: unknown, depth: number, within: StoredPlacement): readonly WrittenNode[] | null {
        if (!Array.isArray(held) || depth > deepestWriting) {
            return null;
        }

        const written: WrittenNode[] = [];

        for (const node of held) {
            remaining -= 1;

            if (remaining < 0) {
                return null;
            }

            const read = nodeIn(node, depth, within);

            if (read === null) {
                return null;
            }

            if (read !== dropped) {
                written.push(read);
            }
        }

        return written;
    }

    function nodeIn(node: unknown, depth: number, within: StoredPlacement): WrittenNode | typeof dropped | null {
        if (typeof node !== 'object' || node === null || Array.isArray(node)) {
            return null;
        }

        const record = node as Record<string, unknown>;
        const text = record['text'];

        if (text !== undefined) {
            // Words stand in the flow and in a cell, and never loose in a table or a row, where nothing draws them.
            return typeof text === 'string' &&
                text.length <= longestWrittenRun &&
                (within === 'flow' || within === 'cell')
                ? { text }
                : null;
        }

        const element = record['element'];

        // A picture stands only in the flow: a cell holds none, and nothing stands loose in a table or a row.
        if (element === 'img') {
            return within === 'flow' ? pictureIn(record) : null;
        }

        const address = record['address'] ?? null;

        if (typeof element !== 'string' || !writableElements.includes(element) || !standsIn(element, within)) {
            return null;
        }

        // Only a link carries one, which is what `writtenIn` writes and therefore the whole of what this may read
        // back: an address on anything else is a stored tree this client did not compose, and it would otherwise
        // leave as an `href` on an element that has no business holding one.
        if (address !== null && (element !== 'a' || typeof address !== 'string' || !followable(address))) {
            return null;
        }

        const presented = presentationIn(element, record);
        const celled = cellIn(element, record);

        if (presented === null || celled === null) {
            return null;
        }

        const holds = nodesIn(record['holds'], depth + 1, placementInside(element, within));

        if (holds === null) {
            return null;
        }

        const read: WrittenMarkup = { element: element as WrittenElement, address, ...presented, ...celled, holds };

        // A table past a bound is one `writtenIn` would have taken apart, so a stored one is not this client's.
        return element === 'table' && !tableWithinBounds(read) ? null : read;
    }

    function pictureIn(record: Record<string, unknown>): WrittenPicture | typeof dropped | null {
        const { picture, width, height } = record;

        if (typeof picture !== 'string' || !isPictureName(picture) || !isPictureSide(width) || !isPictureSide(height)) {
            return null;
        }

        return holdsPicture(picture) ? { element: 'img', picture, width, height } : dropped;
    }

    return nodesIn(value, 0, 'flow');
}

// Where a stored node stands: the two places words do, and the two parts of a table between them.
type StoredPlacement = Placement | 'table' | 'row';

// Whether an element may stand where a stored tree put it: a row only in a table, a cell only in a row, nothing but a
// cell's own runs inside a cell — so no table inside another — and none of a table's parts loose in the flow.
function standsIn(element: string, within: StoredPlacement): boolean {
    switch (within) {
        case 'table':
            return element === 'tr';
        case 'row':
            return tableCells.includes(element);
        case 'cell':
            return !blocksOutsideACell.includes(element);
        case 'flow':
            return element === 'table' || !tableParts.includes(element);
    }
}

function placementInside(element: string, within: StoredPlacement): StoredPlacement {
    if (element === 'table') {
        return 'table';
    }

    if (element === 'tr') {
        return 'row';
    }

    return tableCells.includes(element) ? 'cell' : within;
}

// The fill and the spans a stored cell carries, only on a cell and only as `writtenIn` writes them: an opaque colour in
// its one spelling, and a span that is a whole number above one and inside the table's bounds.
function cellIn(
    element: string,
    record: Record<string, unknown>,
): { readonly background?: string; readonly columnSpan?: number; readonly rowSpan?: number } | null {
    const background = record['background'];
    const columnSpan = record['columnSpan'];
    const rowSpan = record['rowSpan'];

    if (background === undefined && columnSpan === undefined && rowSpan === undefined) {
        return {};
    }

    const backgroundRead =
        background === undefined || (typeof background === 'string' && writtenColour(background) === background);
    const columnSpanRead = columnSpan === undefined || spanWithin(columnSpan, mostTableColumns);
    const rowSpanRead = rowSpan === undefined || spanWithin(rowSpan, mostTableRows);

    if (!tableCells.includes(element) || !backgroundRead || !columnSpanRead || !rowSpanRead) {
        return null;
    }

    return {
        ...(typeof background === 'string' ? { background } : {}),
        ...(typeof columnSpan === 'number' ? { columnSpan } : {}),
        ...(typeof rowSpan === 'number' ? { rowSpan } : {}),
    };
}

function spanWithin(span: unknown, most: number): boolean {
    return typeof span === 'number' && Number.isInteger(span) && span > 1 && span <= most;
}

// The colour, the size, and the alignment a stored element carries, each on the element `writtenIn` writes it on and
// only in the spelling it writes. A `span` carrying neither is one `writtenIn` never writes, so it is refused with the
// rest.
function presentationIn(
    element: string,
    record: Record<string, unknown>,
): { readonly colour?: string; readonly size?: WrittenSize; readonly alignment?: WrittenAlignment } | null {
    const colour = record['colour'];
    const size = record['size'];
    const alignment = record['alignment'];

    const colourRead = colour === undefined || (typeof colour === 'string' && writtenColour(colour) === colour);
    const sizeRead = size === undefined || (typeof size === 'string' && writtenSizes.includes(size));
    const alignmentRead =
        alignment === undefined || (typeof alignment === 'string' && writtenAlignments.includes(alignment));

    if (!colourRead || !sizeRead || !alignmentRead) {
        return null;
    }

    const spanned = colour !== undefined || size !== undefined;

    if (spanned !== (element === 'span') || (alignment !== undefined && !alignedElements.includes(element))) {
        return null;
    }

    return {
        ...(colour === undefined ? {} : { colour }),
        ...(size === undefined ? {} : { size: size as WrittenSize }),
        ...(alignment === undefined ? {} : { alignment: alignment as WrittenAlignment }),
    };
}

// What a kept picture the kept pictures do not hold reads as: nothing in its place, and the rest of the tree read.
const dropped = Symbol('dropped');

function markupOf(node: WrittenNode, pictureSource: PictureSource, destination: WrittenDestination): string {
    if ('text' in node) {
        return escaped(node.text);
    }

    if (node.element === 'img') {
        return pictureMarkupOf(node, pictureSource);
    }

    const holds = htmlOf(node.holds, pictureSource, destination);

    switch (node.element) {
        case 'br':
            return '<br />';
        case 'span':
            if (destination === 'editor') {
                return `<font${fontAttributesOf(node)}>${holds}</font>`;
            }

            break;
        case 'table':
            return `<table style="border-collapse:collapse">${holds}</table>`;
        case 'td':
        case 'th':
            return cellMarkupOf(node, holds, destination);
        default:
            break;
    }

    const address = node.address === null ? '' : ` href="${attribute(node.address)}"`;
    const style = sentStyleOf(node);

    return `<${node.element}${address}${style === '' ? '' : ` style="${style}"`}>${holds}</${node.element}>`;
}

// A run's colour and size as `<font>` spells them, every value of which comes from the closed set.
function fontAttributesOf(run: WrittenMarkup): string {
    const colour = run.colour === undefined ? null : writtenColour(run.colour);

    return `${colour === null ? '' : ` color="${colour}"`}${run.size === undefined ? '' : ` size="${fontSizeOf[run.size]}"`}`;
}

// A cell with its spans and its declarations. A break ending a cell draws no line of its own, so it is given a second
// to stand on, which is also what `writtenIn` reads back as one; and in the editor an empty cell is given a break so
// an engine has a line to put the caret on.
function cellMarkupOf(cell: WrittenMarkup, holds: string, destination: WrittenDestination): string {
    const last = cell.holds[cell.holds.length - 1];
    const standing = last === undefined ? destination === 'editor' : isBreak(last);
    const spans = `${cell.columnSpan === undefined ? '' : ` colspan="${cell.columnSpan.toFixed(0)}"`}${
        cell.rowSpan === undefined ? '' : ` rowspan="${cell.rowSpan.toFixed(0)}"`
    }`;
    const style = cellDeclarationsOf(cell)
        .map(([property, value]) => `${property}:${value}`)
        .join(';');

    return `<${cell.element}${spans} style="${style}">${holds}${standing ? '<br />' : ''}</${cell.element}>`;
}

// Mail clients drop style sheets, so what the composer presents goes out as an inline style — and every value in it is
// one of the closed set's rather than text anybody wrote, which is why nothing here is escaped. The colour is checked
// once more on the way out because it is the one value whose type is a string.
function sentStyleOf(node: WrittenMarkup): string {
    const colour = node.colour === undefined ? null : writtenColour(node.colour);

    return [
        colour === null ? null : `color:${colour}`,
        node.size === undefined ? null : `font-size:${sentSizes[node.size]}`,
        node.alignment === undefined ? null : `text-align:${sentAlignments[node.alignment]}`,
    ]
        .filter((declaration) => declaration !== null)
        .join(';');
}

/**
 * The size a picture is written at: the size it is drawn at, at most the widest a message carries and its height
 * scaled to that. Outlook honours the two attributes and ignores the style, and every other client honours both.
 */
export function sentSizeOf(node: WrittenPicture): { readonly width: number; readonly height: number } {
    const width = Math.min(node.width, widestSentPicture);

    return { width, height: Math.max(1, Math.round((node.height * width) / node.width)) };
}

/**
 * The size a picture element is drawn at, read from its `width` attribute — which is how the editor, and the HTML a
 * copy carries, write the size its grip gave it — against the size the picture's octets state.
 */
export function drawnIn(
    image: Element,
    own: { readonly width: number; readonly height: number },
): { readonly width: number; readonly height: number } {
    return drawnSizeOf(own, Number(image.getAttribute('width') ?? Number.NaN));
}

function pictureMarkupOf(node: WrittenPicture, pictureSource: PictureSource): string {
    const { width, height } = sentSizeOf(node);
    const { address, label } = pictureSource(node.picture);
    const named = label === null ? '' : ` alt="${attribute(label)}"`;

    return `<img src="${attribute(address)}"${named} width="${width.toFixed(0)}" height="${height.toFixed(0)}" style="max-width:100%;height:auto" />`;
}

function escaped(text: string): string {
    return text.replace(/&/gu, '&amp;').replace(/</gu, '&lt;').replace(/>/gu, '&gt;');
}

function attribute(text: string): string {
    return escaped(text).replace(/"/gu, '&quot;');
}

// The text a tree reads as, before the whitespace it accumulated is collapsed. A list is walked knowing which kind it
// is, because that is the one thing an item cannot see from where it stands.
function textOf(written: readonly WrittenNode[], list: 'ol' | 'ul' | null): string {
    let items = 0;
    let text = '';

    for (const node of written) {
        if ('text' in node) {
            // The whitespace collapsing a browser does when it draws the same text, so a tree that arrived across
            // several source lines reads as the one line somebody was looking at.
            text += node.text.replace(/\s+/gu, ' ');

            continue;
        }

        switch (node.element) {
            // A picture says nothing to a reader of the plain text, so it is left out of it rather than described.
            case 'img':
                break;
            case 'br':
                text += '\n';
                break;
            case 'li':
                items += 1;
                text += `${list === 'ol' ? `${items.toFixed(0)}. ` : '- '}${textOf(node.holds, null).trim()}\n`;
                break;
            case 'blockquote':
                text += `${quoted(textOf(node.holds, null))}\n`;
                break;
            case 'ol':
            case 'ul':
                text += `${textOf(node.holds, node.element)}\n`;
                break;
            case 'p':
                // A paragraph is a break in the reading rather than the next line, which is what the blank line
                // between two of them says. A `div` is how an engine writes one line after another and gets one.
                text += `${textOf(node.holds, null)}\n\n`;
                break;
            case 'div':
                text += `${textOf(node.holds, null)}\n`;
                break;
            case 'table':
                // A row to a line and its cells side by side, so a reader whose client shows the plain-text part
                // still reads the figures in their rows. A break inside a cell would end the row, so it is a space.
                text += `${rowsOf(node)
                    .map((row) =>
                        cellsOf(row)
                            .map((cell) => textOf(cell.holds, null).replace(/\s+/gu, ' ').trim())
                            .join(' | '),
                    )
                    .join('\n')}\n\n`;
                break;
            default:
                text += textOf(node.holds, null);
                break;
        }
    }

    return text;
}

// A quotation as mail has always written one, so that a reader whose client shows the plain-text part still sees which
// half of the message is somebody else's.
function quoted(text: string): string {
    return text
        .trim()
        .split('\n')
        .map((line) => (line === '' ? '>' : `> ${line}`))
        .join('\n');
}

function collapsed(text: string): string {
    return text
        .replace(/[^\S\n]+$/gmu, '')
        .replace(/\n{3,}/gu, '\n\n')
        .trim();
}
