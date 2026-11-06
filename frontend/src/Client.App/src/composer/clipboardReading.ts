// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { newIdentifier } from '../agent/newIdentifier';
import { writesBodyText } from '../messageBody/senderColour';
import { octetsOf, pictureIn, type HeldPicture } from './pictures';
import {
    columnsOf,
    mostTableColumns,
    mostTableRows,
    placementsOf,
    spanned,
    tableWithinBounds,
    withSpansWithinTheGrid,
} from './writtenTable';
import {
    colourOfRgb,
    deepestWriting,
    followable,
    longestWrittenRun,
    mostWrittenNodes,
    stepOfFontSize,
    writtenAlignmentOf,
    writtenColour,
    writtenTextIn,
    type PictureAt,
    type WrittenAlignment,
    type WrittenMarkup,
    type WrittenNode,
    type WrittenPicture,
    type WrittenSize,
} from './writtenText';

// What a paste carries as `text/html` — a passage of a web page, a document, or a spreadsheet's range — read into the
// composer's closed tree, as ADR 0047 Question 1 decides it.
//
// **This is the one module that parses foreign markup, and what it parses is never adopted.** `DOMParser` makes a
// document with no browsing context: the standard disables its scripts, and it has nothing to fetch a resource for. No
// node of it is adopted, imported, inserted, or handed to a `Range` or to `execCommand`; it is walked node by node into
// `WrittenNode`s and let go of, and the lint rule admits `parseFromString` in this file alone. What reaches the editor
// is that tree, through `editing.ts`.
//
// **Presentation is read through the engine's own parsers.** An inline declaration through `element.style`; a
// `<style>` element's rules through a constructed `CSSStyleSheet`, which is never adopted by any document and whose
// `replaceSync` refuses `@import`; a colour in any notation through a detached canvas's `fillStyle`. Only seven
// properties are read, and only into the closed set's values — anything else a page wrote is not read at all.
//
// **What the tree carries is checked as a kept tree is**, by `writtenTextIn`, so a reading this module got wrong is a
// paste of nothing rather than a value outside the set.

/** What a paste's markup read as. */
export type ClipboardReading =
    /** A tree with something in it, and the pictures it names that the clipboard itself carried. */
    | {
          readonly kind: 'written';
          readonly written: readonly WrittenNode[];
          readonly pictures: readonly { readonly name: string; readonly picture: HeldPicture }[];
      }
    /** A table past a bound, which is refused whole rather than cut, with how large it was. */
    | { readonly kind: 'tableTooLarge'; readonly rows: number; readonly columns: number }
    /** Nothing the closed set can hold, which leaves the paste to what else the clipboard carries. */
    | { readonly kind: 'nothing' };

const nothing: ClipboardReading = { kind: 'nothing' };

// Elements whose content is no words of anybody's — code, style, metadata, embedded documents and media — and which
// are therefore left out whole rather than unwrapped.
const unread: ReadonlySet<string> = new Set([
    'audio',
    'base',
    'button',
    'canvas',
    'embed',
    'head',
    'hr',
    'iframe',
    'input',
    'link',
    'map',
    'math',
    'meta',
    'noscript',
    'object',
    'script',
    'select',
    'style',
    'svg',
    'template',
    'textarea',
    'title',
    'video',
]);

type BlockElement = 'blockquote' | 'div' | 'li' | 'ol' | 'p' | 'ul';

// The block each block element is read as: the blocks the closed set has as they are, a heading as a paragraph, and a
// block the set has no name for as a line of its own, so its words are not run together with what stands beside them.
const blockElements: Readonly<Record<string, BlockElement>> = {
    blockquote: 'blockquote',
    div: 'div',
    li: 'li',
    ol: 'ol',
    p: 'p',
    ul: 'ul',
    h1: 'p',
    h2: 'p',
    h3: 'p',
    h4: 'p',
    h5: 'p',
    h6: 'p',
    address: 'div',
    article: 'div',
    aside: 'div',
    caption: 'div',
    center: 'div',
    dd: 'div',
    details: 'div',
    dl: 'div',
    dt: 'div',
    fieldset: 'div',
    figcaption: 'div',
    figure: 'div',
    footer: 'div',
    form: 'div',
    header: 'div',
    hgroup: 'div',
    main: 'div',
    nav: 'div',
    pre: 'div',
    section: 'div',
    summary: 'div',
};

// The blocks that carry an alignment of their own, which a list and a quote do not.
const alignedBlocks: readonly BlockElement[] = ['div', 'li', 'p'];

// A heading is bold at the step its level maps to, ADR 0047's size table read for a heading.
const headingSteps: Readonly<Record<string, WrittenSize | null>> = {
    h1: 'largest',
    h2: 'larger',
    h3: 'large',
    h4: null,
    h5: null,
    h6: null,
};

// The emphasis an element states by its name alone, which a declaration on it may still take away.
const namedEmphases: Readonly<Record<string, 'bold' | 'italic' | 'underline' | 'strike'>> = {
    b: 'bold',
    strong: 'bold',
    i: 'italic',
    em: 'italic',
    u: 'underline',
    s: 'strike',
    strike: 'strike',
    del: 'strike',
};

// The blocks an alignment may stand on, which an `align` attribute is read on too.
const alignable: ReadonlySet<string> = new Set(['div', 'li', 'p', 'td', 'th', ...Object.keys(headingSteps)]);

// The CSS keywords of the size scale, ADR 0047's table.
const keywordSteps: Readonly<Record<string, WrittenSize | null>> = {
    'xx-small': 'small',
    'x-small': 'small',
    small: 'small',
    medium: null,
    large: 'large',
    'x-large': 'larger',
    'xx-large': 'largest',
    'xxx-large': 'largest',
};

const lineBreak: WrittenMarkup = { element: 'br', address: null, holds: [] };

/** A `<style>` rule this reader applies: one whose selector is an element, a class, or an element with a class. */
interface SheetRule {
    readonly element: string | null;
    readonly className: string | null;
    readonly style: CSSStyleDeclaration;
}

/** What one element states about how its words are drawn, each on the closed set or not stated. */
interface Presentation {
    readonly bold: boolean;
    readonly italic: boolean;
    readonly underline: boolean;
    readonly strike: boolean;
    readonly colour: string | null;
    readonly size: WrittenSize | null;
    readonly alignment: WrittenAlignment | null;
    readonly background: string | null;
}

/** What one reading spends and collects as it walks. */
interface Reading {
    nodes: number;
    tooLarge: { readonly rows: number; readonly columns: number } | null;
    readonly sheet: readonly SheetRule[];
    readonly pictureAt: PictureAt;
    readonly mint: () => string;
    readonly pictures: { readonly name: string; readonly picture: HeldPicture }[];
    readonly named: Set<string>;
}

type Placement = 'flow' | 'cell';

/**
 * A paste's `text/html` read into the closed tree.
 *
 * @param html What the clipboard carried as markup.
 * @param pictureAt Which picture the composer holds at an object URL, which is the one `blob:` source a picture is read
 *   from — what copying a picture inside the message puts on the clipboard. Any other is dropped.
 * @param mint Names a picture the clipboard carried as a `data:` source.
 */
export function readClipboardHtml(
    html: string,
    pictureAt: PictureAt,
    mint: () => string = newIdentifier,
): ClipboardReading {
    const parsed = new DOMParser().parseFromString(html, 'text/html');
    const reading: Reading = {
        nodes: mostWrittenNodes,
        tooLarge: null,
        sheet: sheetOf(parsed),
        pictureAt,
        mint,
        pictures: [],
        named: new Set(),
    };
    const read = readPasted(parsed.body, 0, reading, 'flow');

    if (reading.tooLarge !== null) {
        return { kind: 'tableTooLarge', ...reading.tooLarge };
    }

    const written = writtenTextIn(read, (name) => reading.named.has(name));

    return written === null || !saysAnything(written)
        ? nothing
        : { kind: 'written', written, pictures: reading.pictures };
}

/**
 * Plain text whose every line that holds anything splits on tabs into the same two or more cells, read as a table of
 * them — what a spreadsheet's range is where the clipboard carries nothing else — or `nothing` for any other text.
 */
export function readTabbedText(plain: string): ClipboardReading {
    const rows = plain
        .replace(/\r\n?/gu, '\n')
        .split('\n')
        .filter((line) => line.trim() !== '')
        .map((line) => line.split('\t'));
    const split = rows[0]?.length ?? 0;
    const columns = Math.max(0, ...rows.map((row) => row.findLastIndex((text) => text !== '') + 1));

    if (columns < 2 || rows.some((row) => row.length !== split)) {
        return nothing;
    }

    if (rows.length > mostTableRows || columns > mostTableColumns) {
        return { kind: 'tableTooLarge', rows: rows.length, columns };
    }

    const table: WrittenMarkup = {
        element: 'table',
        address: null,
        holds: rows.map((cells) => ({
            element: 'tr',
            address: null,
            holds: cells.slice(0, columns).map((text) => ({
                element: 'td',
                address: null,
                holds: text === '' ? [] : [{ text: text.slice(0, longestWrittenRun) }],
            })),
        })),
    };

    return { kind: 'written', written: [table], pictures: [] };
}

// The rules of every `<style>` element, read by a constructed sheet no document adopts, which applies nothing to the
// page and fetches nothing. A selector this reader does not apply is not one an engine needed to match a pasted cell by.
function sheetOf(parsed: Document): readonly SheetRule[] {
    const written = [...parsed.querySelectorAll('style')].map((style) => style.textContent).join('\n');

    if (written.trim() === '') {
        return [];
    }

    const sheet = new CSSStyleSheet();

    try {
        sheet.replaceSync(written);
    } catch {
        return [];
    }

    return [...sheet.cssRules].flatMap((rule) =>
        rule instanceof CSSStyleRule
            ? rule.selectorText.split(',').flatMap((selector) => {
                  const simple = /^([a-z][a-z0-9]*)?(?:\.([_a-z0-9-]+))?$/iu.exec(selector.trim());

                  return simple === null || (simple[1] === undefined && simple[2] === undefined)
                      ? []
                      : [
                            {
                                element: simple[1]?.toLowerCase() ?? null,
                                className: simple[2] ?? null,
                                style: rule.style,
                            },
                        ];
              })
            : [],
    );
}

// What breaks a line or stands in a grid of its own, beside which a break in the source is never a space between words.
const breaksTheRun: ReadonlySet<string> = new Set([
    'br',
    'caption',
    'col',
    'colgroup',
    'table',
    'tbody',
    'td',
    'tfoot',
    'th',
    'thead',
    'tr',
]);

// Whether a text node stands between two runs of words, rather than at the edge of what holds it, beside a block, or
// beside an element that draws no words of its own.
function betweenRuns(text: Node): boolean {
    return runsOn(text.previousSibling, 'previousSibling') && runsOn(text.nextSibling, 'nextSibling');
}

function runsOn(sibling: ChildNode | null, onward: 'previousSibling' | 'nextSibling'): boolean {
    let node = sibling;

    while (node !== null && node.nodeType === Node.COMMENT_NODE) {
        node = node[onward];
    }

    if (node === null) {
        return false;
    }

    if (node.nodeType === Node.TEXT_NODE) {
        return (node.nodeValue ?? '').trim() !== '';
    }

    const named = node instanceof Element ? node.tagName.toLowerCase() : null;

    return (
        named !== null &&
        !Object.hasOwn(blockElements, named) &&
        !unread.has(named) &&
        !breaksTheRun.has(named) &&
        (node.textContent ?? '').trim() !== ''
    );
}

// Whether a text node stands where its line breaks are drawn: inside a `pre`, or under a declared `white-space` that
// keeps them, the nearest declaration deciding.
function keepsItsLines(text: Node): boolean {
    for (let holder = text.parentElement; holder !== null; holder = holder.parentElement) {
        if (holder.tagName.toLowerCase() === 'pre') {
            return true;
        }

        const declared = holder instanceof HTMLElement ? holder.style.whiteSpace : '';

        if (declared !== '') {
            return /^(?:pre|pre-wrap|pre-line|break-spaces)$/u.test(declared);
        }
    }

    return false;
}

// An element's words as one run, for an element too deep to read as a tree: its text, leaving out whole what `unread`
// leaves out on the shallow path, and stopping once the run is as long as one may be.
function wordsOf(element: Element): string {
    const walker = element.ownerDocument.createTreeWalker(element, NodeFilter.SHOW_ELEMENT | NodeFilter.SHOW_TEXT, {
        acceptNode: (node) =>
            node instanceof Element && unread.has(node.tagName.toLowerCase())
                ? NodeFilter.FILTER_REJECT
                : NodeFilter.FILTER_ACCEPT,
    });
    let words = '';

    for (let node = walker.nextNode(); node !== null && words.length < longestWrittenRun; node = walker.nextNode()) {
        words += node.nodeType === Node.TEXT_NODE ? (node.nodeValue ?? '') : '';
    }

    return words.slice(0, longestWrittenRun);
}

function readPasted(parent: Node, depth: number, reading: Reading, within: Placement): WrittenNode[] {
    const written: WrittenNode[] = [];

    for (const node of [...parent.childNodes]) {
        if (reading.nodes <= 0) {
            break;
        }

        if (node.nodeType === Node.TEXT_NODE) {
            const raw = node.nodeValue ?? '';

            // Preformatted text draws the lines it was written in, so each of its line breaks is one the reader saw.
            if (keepsItsLines(node)) {
                for (const [index, line] of raw.split(/\r\n?|\n/u).entries()) {
                    if (index > 0) {
                        reading.nodes -= 1;
                        written.push(lineBreak);
                    }

                    if (line !== '') {
                        reading.nodes -= 1;
                        written.push({ text: line.slice(0, longestWrittenRun) });
                    }
                }

                continue;
            }

            // The line breaks and indentation a page's source was laid out in between its blocks, which no reader of it
            // ever saw. Between two runs the same break is the space the source drew between their words.
            if (raw.trim() === '' && /[\n\r]/u.test(raw) && !betweenRuns(node)) {
                continue;
            }

            reading.nodes -= 1;
            written.push({ text: raw.replace(/[ \t\n\r\f]+/gu, ' ').slice(0, longestWrittenRun) });

            continue;
        }

        if (!(node instanceof Element)) {
            continue;
        }

        const named = node.tagName.toLowerCase();

        if (unread.has(named)) {
            continue;
        }

        if (depth >= deepestWriting) {
            reading.nodes -= 1;
            written.push({ text: wordsOf(node) });

            continue;
        }

        if (named === 'br') {
            reading.nodes -= 1;
            written.push(lineBreak);

            continue;
        }

        if (named === 'img') {
            const picture = within === 'cell' ? null : pictureOf(node, reading);

            if (picture !== null) {
                reading.nodes -= 1;
                written.push(picture);
            }

            continue;
        }

        if (named === 'table' && node instanceof HTMLTableElement) {
            written.push(
                // A table's title has no place in the table the composer holds, so it is the line standing before it.
                ...(node.caption === null ? [] : elementOf(node.caption, 'caption', depth, reading, within)),
                ...(within === 'cell' ? cellLinesOf(node, depth, reading) : tableOf(node, depth, reading)),
            );

            continue;
        }

        written.push(...elementOf(node, named, depth, reading, within));
    }

    return written;
}

// One element read with what it holds: its emphasis, colour, and size around its words, and itself where the set has
// it — a block, a list, a link this client would follow — or a line where it is a block the set has no name for.
function elementOf(
    element: Element,
    named: string,
    depth: number,
    reading: Reading,
    within: Placement,
): readonly WrittenNode[] {
    const stated = presentationOf(element, named, reading.sheet);
    const presentation = Object.hasOwn(headingSteps, named)
        ? { ...stated, bold: true, size: headingSteps[named] ?? null }
        : stated;
    const runs = dressed(readPasted(element, depth + 1, reading, within), presentation, reading);

    if (named === 'a') {
        const address = element.getAttribute('href');

        if (address === null || !followable(address) || runs.length === 0) {
            return runs;
        }

        reading.nodes -= 1;

        return [{ element: 'a', address, holds: runs }];
    }

    const block = Object.hasOwn(blockElements, named) ? blockElements[named] : undefined;

    if (block === undefined || runs.length === 0) {
        return runs;
    }

    if (within === 'cell') {
        return asLines(runs);
    }

    reading.nodes -= 1;

    const alignment = alignedBlocks.includes(block) ? presentation.alignment : null;

    return [
        alignment === null
            ? { element: block, address: null, holds: runs }
            : { element: block, address: null, alignment, holds: runs },
    ];
}

// A block inside a cell read as the lines it held, as `writtenIn` reads one: what it held, then a break.
function asLines(runs: readonly WrittenNode[]): readonly WrittenNode[] {
    const last = runs[runs.length - 1];

    return last !== undefined && isBreak(last) ? runs : [...runs, lineBreak];
}

// Words wrapped in what the element stated about them, innermost first, each wrapper one the closed set has.
function dressed(holds: readonly WrittenNode[], presentation: Presentation, reading: Reading): readonly WrittenNode[] {
    if (holds.length === 0) {
        return holds;
    }

    let runs = holds;

    const wrap = (element: 'b' | 'i' | 'u' | 's') => {
        reading.nodes -= 1;
        runs = [{ element, address: null, holds: runs }];
    };

    if (presentation.strike) {
        wrap('s');
    }

    if (presentation.underline) {
        wrap('u');
    }

    if (presentation.italic) {
        wrap('i');
    }

    if (presentation.bold) {
        wrap('b');
    }

    if (presentation.colour !== null || presentation.size !== null) {
        reading.nodes -= 1;
        runs = [
            {
                element: 'span',
                address: null,
                ...(presentation.colour === null ? {} : { colour: presentation.colour }),
                ...(presentation.size === null ? {} : { size: presentation.size }),
                holds: runs,
            },
        ];
    }

    return runs;
}

// What an element states, from its inline declarations over the sheet's rules over its presentational attributes —
// the order an engine applies them in.
function presentationOf(element: Element, named: string, sheet: readonly SheetRule[]): Presentation {
    const rules = sheet.filter(
        (rule) =>
            (rule.element === null || rule.element === named) &&
            (rule.className === null || element.classList.contains(rule.className)),
    );
    const declared = (property: string): string => {
        const inline = element instanceof HTMLElement ? element.style.getPropertyValue(property) : '';

        if (inline !== '') {
            return inline;
        }

        let fromTheSheet = '';

        for (const rule of rules) {
            fromTheSheet = rule.style.getPropertyValue(property) || fromTheSheet;
        }

        return fromTheSheet;
    };
    const attribute = (name: string): string => element.getAttribute(name) ?? '';
    const emphasis = namedEmphases[named];
    const decoration = `${declared('text-decoration-line')} ${declared('text-decoration')}`.toLowerCase();
    const fontSize = declared('font-size');

    return {
        bold: weightIsBold(declared('font-weight')) ?? emphasis === 'bold',
        italic: styleIsItalic(declared('font-style')) ?? emphasis === 'italic',
        underline: decoration.trim() === '' ? emphasis === 'underline' : decoration.includes('underline'),
        strike: decoration.trim() === '' ? emphasis === 'strike' : decoration.includes('line-through'),
        colour: foregroundOf(declared('color') || (named === 'font' ? attribute('color') : '')),
        size: fontSize === '' && named === 'font' ? stepOfFontSize(attribute('size')) : stepOfLength(fontSize),
        // Excel's `text-align: general` is no alignment an engine draws, so the `align` its figures carry still holds.
        alignment:
            writtenAlignmentOf(declared('text-align')) ??
            (alignable.has(named) ? writtenAlignmentOf(attribute('align')) : null),
        background: backgroundOf(declared('background-color') || attribute('bgcolor')),
    };
}

function weightIsBold(weight: string): boolean | null {
    const stated = weight.trim().toLowerCase();

    if (stated === '') {
        return null;
    }

    if (stated === 'bold' || stated === 'bolder') {
        return true;
    }

    // `normal`, `lighter`, and a number under the semibold weights.
    return Number(stated) >= 600;
}

function styleIsItalic(style: string): boolean | null {
    const stated = style.trim().toLowerCase();

    return stated === '' ? null : stated.startsWith('italic') || stated.startsWith('oblique');
}

// A length or a keyword on ADR 0047's scale. A point is four thirds of a pixel and a relative length is read against
// 16px, because the parent the source meant is not something a paste knows.
function stepOfLength(stated: string): WrittenSize | null {
    const value = stated.trim().toLowerCase();

    if (Object.hasOwn(keywordSteps, value)) {
        return keywordSteps[value] ?? null;
    }

    const length = /^(\d*\.?\d+)(px|pt|em|rem|%)$/u.exec(value);

    if (length === null) {
        return null;
    }

    const amount = Number(length[1]);
    const pixels = { px: amount, pt: (amount * 4) / 3, em: amount * 16, rem: amount * 16, '%': (amount * 16) / 100 }[
        length[2] as 'px' | 'pt' | 'em' | 'rem' | '%'
    ];

    if (pixels < 12.5) {
        return 'small';
    }

    if (pixels < 17) {
        return null;
    }

    if (pixels < 21) {
        return 'large';
    }

    return pixels < 28 ? 'larger' : 'largest';
}

// The colour a run's words were given, except the near-grey dark one every spreadsheet cell and document paragraph
// states as its default rather than as anybody's choice.
function foregroundOf(stated: string): string | null {
    const colour = paintedColour(stated);

    return colour === null || writesBodyText(colour) ? null : colour;
}

// A cell's fill, except one as light as the page a message is read on, which is a sheet's default rather than a fill.
function backgroundOf(stated: string): string | null {
    const colour = paintedColour(stated);

    return colour === null || /^#(?:[f][89a-f]){3}$/u.test(colour) ? null : colour;
}

let painter: CanvasRenderingContext2D | null | undefined;

// A colour in any notation resolved to `#rrggbb` by the engine's own colour parser: a detached canvas's `fillStyle`,
// which draws nothing and fetches nothing. A value it refuses leaves the style as it was, so the value is set over two
// different colours and kept only where both readings agree; one with any transparency comes back as `rgba()`, which
// is no colour the tree carries. An engine with no canvas — the unit suite's — is read by the CSS parser's own
// serialization instead, which answers `rgb()` for what it understood.
function paintedColour(stated: string): string | null {
    const value = stated.trim();

    if (value === '' || /currentcolor|inherit|initial|unset|revert/iu.test(value)) {
        return null;
    }

    painter ??= document.createElement('canvas').getContext('2d');

    if (painter === null) {
        return colourOfRgb(value);
    }

    painter.fillStyle = '#000000';
    painter.fillStyle = value;
    const overBlack = painter.fillStyle;
    painter.fillStyle = '#ffffff';
    painter.fillStyle = value;
    const overWhite = painter.fillStyle;

    return typeof overBlack === 'string' && overBlack === overWhite ? writtenColour(overBlack) : null;
}

// A picture the clipboard itself carries — a `data:` source of an accepted kind, decoded by hand with no request — or
// one the composer holds at an object URL it issued. Any other source is dropped and never fetched.
function pictureOf(image: Element, reading: Reading): WrittenPicture | null {
    const source = image.getAttribute('src') ?? '';
    const held = reading.pictureAt(source);

    if (held !== null) {
        reading.named.add(held.picture);

        return { element: 'img', ...held };
    }

    // Decoded whatever its size, because one past a bound refuses the paste whole rather than going missing from it,
    // and the composer judges the bounds against every picture the paste carries together.
    const encoded = /^data:image\/(?:png|jpeg|gif);base64,([a-z0-9+/=\s]+)$/iu.exec(source)?.[1];
    const octets = encoded === undefined ? null : octetsOf(encoded.replace(/\s+/gu, ''));
    const read = octets === null ? null : pictureIn(octets);

    if (octets === null || read === null) {
        return null;
    }

    const name = reading.mint();

    reading.named.add(name);
    reading.pictures.push({ name, picture: { octets, ...read, label: null } });

    return { element: 'img', picture: name, width: read.width, height: read.height };
}

/** One cell as it was read, and whether all of its words were bold, which is how a sheet marks a header row. */
interface ReadCell {
    readonly cell: WrittenMarkup;
    readonly filled: boolean;
    readonly bold: boolean;
}

// A table read as a spreadsheet's range: its grid trimmed of the empty rows and columns at its end, its header row
// where the sheet marks one, and each cell's alignment, fill, emphasis, colour, and size. One past a bound is refused
// whole, and the reading says how large it was.
function tableOf(table: HTMLTableElement, depth: number, reading: Reading): readonly WrittenNode[] {
    if (depth + 3 > deepestWriting) {
        reading.nodes -= 1;

        return [{ text: wordsOf(table) }];
    }

    const headRows = new Set(table.tHead === null ? [] : [...table.tHead.rows]);
    const read = [...table.rows]
        .map((row) => ({
            inTheHead: headRows.has(row),
            cells: [...row.cells].map((cell) => cellOf(cell, depth, reading)),
        }))
        .filter((row) => row.cells.length > 0);

    while (read.length > 0 && read[read.length - 1]?.cells.every((cell) => !cell.filled) === true) {
        read.pop();
    }

    // The empty columns at the range's end are found on its grid rather than by counting cells, since a merged cell
    // stands for several columns: what is dropped from a row's end is the empty cells that start past the last column
    // any cell with something in it reaches.
    const placements = placementsOf({
        element: 'table',
        address: null,
        holds: read.map((row) => ({ element: 'tr', address: null, holds: row.cells.map(({ cell }) => cell) })),
    });
    const width = Math.max(
        0,
        ...read.flatMap((row, rowIndex) =>
            row.cells.map((cell, cellIndex) => {
                const placed = placements[rowIndex]?.[cellIndex];

                return cell.filled && placed !== undefined ? placed.column + placed.columnSpan : 0;
            }),
        ),
    );
    const rows = read
        .map((row, rowIndex) => {
            let kept = row.cells.length;

            while (
                kept > 0 &&
                row.cells[kept - 1]?.filled === false &&
                (placements[rowIndex]?.[kept - 1]?.column ?? 0) >= width
            ) {
                kept -= 1;
            }

            return { ...row, cells: row.cells.slice(0, kept) };
        })
        .filter((row) => row.cells.length > 0);
    const [first, second] = rows;

    if (first === undefined || width === 0) {
        return [];
    }

    const spanless = rows.every((row) =>
        row.cells.every(({ cell }) => cell.columnSpan === undefined && cell.rowSpan === undefined),
    );
    const boldRow = (row: (typeof rows)[number]) =>
        row.cells.some((cell) => cell.filled) && row.cells.every((cell) => !cell.filled || cell.bold);
    const headed =
        first.inTheHead ||
        first.cells.every(({ cell }) => cell.element === 'th') ||
        (second !== undefined && boldRow(first) && !boldRow(second));

    const markup = withSpansWithinTheGrid({
        element: 'table',
        address: null,
        holds: rows.map((row, index) => ({
            element: 'tr',
            address: null,
            holds: [
                ...row.cells.map(({ cell }) =>
                    spanned(
                        { ...cell, element: index === 0 && headed ? 'th' : cell.element },
                        cell.columnSpan ?? 1,
                        Math.min(cell.rowSpan ?? 1, rows.length - index),
                    ),
                ),
                // A row a sheet wrote short is filled out to the grid, where no span makes the grid a question.
                ...(spanless
                    ? Array.from({ length: width - row.cells.length }, () => ({
                          element: index === 0 && headed ? ('th' as const) : ('td' as const),
                          address: null,
                          holds: [],
                      }))
                    : []),
            ],
        })),
    });
    const columns = columnsOf(markup);

    if (rows.length > mostTableRows || columns > mostTableColumns) {
        reading.tooLarge ??= { rows: rows.length, columns };

        return [];
    }

    reading.nodes -= rows.length * (width + 1) + 1;

    // A table whose spans lay out to no grid the composer holds keeps its words, a line to each cell.
    return tableWithinBounds(markup)
        ? [markup]
        : rows.flatMap((row) =>
              row.cells
                  .filter((cell) => cell.filled)
                  .map(({ cell }) => ({ element: 'div' as const, address: null, holds: cell.holds })),
          );
}

function cellOf(cell: HTMLTableCellElement, depth: number, reading: Reading): ReadCell {
    const named = cell.tagName.toLowerCase();
    const presentation = presentationOf(cell, named, reading.sheet);
    const words = withoutTheLastBreak(readPasted(cell, depth + 3, reading, 'cell'));
    const holds = dressed(words, presentation, reading);
    const alignment = presentation.alignment;

    return {
        cell: spanned(
            {
                element: named === 'th' ? 'th' : 'td',
                address: null,
                ...(alignment === null || alignment === 'start' ? {} : { alignment }),
                ...(presentation.background === null ? {} : { background: presentation.background }),
                holds,
            },
            Math.max(1, cell.colSpan),
            Math.max(1, cell.rowSpan),
        ),
        filled: saysAnything(holds),
        bold: boldThroughout(holds, false),
    };
}

// A table inside a cell, which the composer does not hold, read as its cells' words, a line to each.
function cellLinesOf(table: HTMLTableElement, depth: number, reading: Reading): readonly WrittenNode[] {
    return [...table.rows].flatMap((row) =>
        [...row.cells].flatMap((cell) => {
            const lines = withoutTheLastBreak(readPasted(cell, depth + 1, reading, 'cell'));

            return lines.length === 0 ? [] : [...lines, lineBreak];
        }),
    );
}

function withoutTheLastBreak(holds: WrittenNode[]): WrittenNode[] {
    const last = holds[holds.length - 1];

    return last !== undefined && isBreak(last) ? holds.slice(0, -1) : holds;
}

function isBreak(node: WrittenNode): boolean {
    return !('text' in node) && node.element === 'br';
}

// Whether a tree holds a word or a picture, rather than only space and breaks.
function saysAnything(written: readonly WrittenNode[]): boolean {
    return written.some((node) =>
        'text' in node ? /\S/u.test(node.text) : node.element === 'img' || saysAnything(node.holds),
    );
}

// Whether every word a tree holds stands inside bold, and it holds at least one.
function boldThroughout(written: readonly WrittenNode[], bold: boolean): boolean {
    const words = written.filter((node) => saysAnything([node]));

    return (
        words.length > 0 &&
        words.every((node) =>
            'text' in node
                ? bold
                : node.element === 'img'
                  ? false
                  : boldThroughout(node.holds, bold || node.element === 'b' || node.element === 'strong'),
        )
    );
}
