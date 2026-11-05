// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What somebody has written, as a closed tree rather than as markup, and the two readings a message going out needs
// from it: the HTML part and the plain-text alternative beside it.
//
// **Nothing here parses a string into nodes.** ADR 0024 states that for a message arriving, and the reasoning holds
// just as firmly for one going out: the composer reads its own editable region node by node into the closed set below,
// renders that set back as ordinary elements, and writes the HTML part by escaping every value it puts in one. The one
// markup the editor is ever handed is that same escaped writing of this set, through `editing.ts` under ADR 0047. So
// no foreign markup is parsed anywhere on this path, no sanitizer is pinned, and what a poisoned session store could
// put back on the screen is bounded by what this type can express rather than by what a filter remembered to strip.
//
// The set is DOM-shaped rather than semantic, and deliberately so. What produces it is a browser editing a
// `contenteditable` region, and each engine wraps a line, a list, and an emphasis its own way — a semantic block model
// would have to guess which of those it was looking at, and would lose whatever it guessed wrong. Reading the shape
// that is actually there loses nothing, and closing the *set* is what makes it safe.

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

    /** How a block places its lines, which only a paragraph, a line, and a list item carry. */
    readonly alignment?: WrittenAlignment;

    readonly holds: readonly WrittenNode[];
}

export type WrittenNode = WrittenText | WrittenMarkup;

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
    'u',
    'ul',
];

// A `span` an engine leaves in the region is its own bookkeeping — the computed style it carries a merged line over
// with — rather than a colour or a size anybody chose, so the region's spans are unwrapped. A run's colour and size are
// read off the `font` element the editing commands write instead, and the tree's `span` is that reading.
const readableElements: readonly string[] = writableElements.filter((element) => element !== 'span');

const writtenSizes: readonly string[] = ['small', 'large', 'larger', 'largest'];
const writtenAlignments: readonly string[] = ['start', 'center', 'end', 'justify'];
const alignedElements: readonly string[] = ['div', 'li', 'p'];

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

/** How an alignment is drawn, which is the same `text-align` the HTML part writes. */
export function drawnAlignment(alignment: WrittenAlignment): string {
    return sentAlignments[alignment];
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
const mostWrittenNodes = 100_000;
const longestWrittenRun = 100_000;
const deepestWriting = 64;

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
 */
export function writtenIn(parent: Node): readonly WrittenNode[] {
    return readWritten(parent, 0, { nodes: mostWrittenNodes });
}

// What is left of the one bound that is spent across a whole reading rather than measured node by node.
interface WritingBudget {
    nodes: number;
}

function readWritten(parent: Node, depth: number, budget: WritingBudget): readonly WrittenNode[] {
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
        const run = named === 'font' ? runStyleOf(node) : null;
        const writable = readableElements.includes(named) || run !== null;

        // An element that is kept is one of the nodes the budget counts, and it is counted before what it holds is
        // read, so a wide tree cannot spend the whole budget on children and then add a parent past the bound.
        if (writable) {
            budget.nodes -= 1;
        }

        const holds = readWritten(node, depth + 1, budget);

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

        written.push(alignment === null ? { element, address, holds } : { element, address, alignment, holds });
    }

    return written;
}

// The colour and the size a `font` element states, each only where it is on the closed set: a colour an engine wrote
// in some other notation and a size off the scale are dropped rather than approximated, and a `font` left with neither
// is unwrapped like any other element this client has no name for.
function runStyleOf(font: Element): { readonly colour?: string; readonly size?: WrittenSize } | null {
    const colour = writtenColour(font.getAttribute('color') ?? '');
    const size = fontSizeSteps[font.getAttribute('size') ?? ''] ?? null;

    if (colour === null && size === null) {
        return null;
    }

    return { ...(colour === null ? {} : { colour }), ...(size === null ? {} : { size }) };
}

// The alignment a block states, written by the engine as `text-align` or, in an engine that still spells it the old
// way, as an `align` attribute. Anything else is no alignment at all.
function alignmentOf(block: Element): WrittenAlignment | null {
    const stated = block instanceof HTMLElement && block.style.textAlign !== '' ? block.style.textAlign : null;

    return readAlignments[(stated ?? block.getAttribute('align') ?? '').toLowerCase()] ?? null;
}

/** The HTML part of a message going out, with every value in it escaped rather than trusted. */
export function htmlOf(written: readonly WrittenNode[]): string {
    return written.map(markupOf).join('');
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
 */
export function writtenTextIn(value: unknown): readonly WrittenNode[] | null {
    let remaining = mostWrittenNodes;

    function nodesIn(held: unknown, depth: number): readonly WrittenNode[] | null {
        if (!Array.isArray(held) || depth > deepestWriting) {
            return null;
        }

        const written: WrittenNode[] = [];

        for (const node of held) {
            remaining -= 1;

            if (remaining < 0) {
                return null;
            }

            const read = nodeIn(node, depth);

            if (read === null) {
                return null;
            }

            written.push(read);
        }

        return written;
    }

    function nodeIn(node: unknown, depth: number): WrittenNode | null {
        if (typeof node !== 'object' || node === null || Array.isArray(node)) {
            return null;
        }

        const record = node as Record<string, unknown>;
        const text = record['text'];

        if (text !== undefined) {
            return typeof text === 'string' && text.length <= longestWrittenRun ? { text } : null;
        }

        const element = record['element'];
        const address = record['address'] ?? null;

        if (typeof element !== 'string' || !writableElements.includes(element)) {
            return null;
        }

        // Only a link carries one, which is what `writtenIn` writes and therefore the whole of what this may read
        // back: an address on anything else is a stored tree this client did not compose, and it would otherwise
        // leave as an `href` on an element that has no business holding one.
        if (address !== null && (element !== 'a' || typeof address !== 'string' || !followable(address))) {
            return null;
        }

        const presented = presentationIn(element, record);

        if (presented === null) {
            return null;
        }

        const holds = nodesIn(record['holds'], depth + 1);

        return holds === null ? null : { element: element as WrittenElement, address, ...presented, holds };
    }

    return nodesIn(value, 0);
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

function markupOf(node: WrittenNode): string {
    if ('text' in node) {
        return escaped(node.text);
    }

    if (node.element === 'br') {
        return '<br />';
    }

    const address = node.address === null ? '' : ` href="${escaped(node.address).replace(/"/gu, '&quot;')}"`;
    const style = sentStyleOf(node);

    return `<${node.element}${address}${style === '' ? '' : ` style="${style}"`}>${htmlOf(node.holds)}</${node.element}>`;
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

function escaped(text: string): string {
    return text.replace(/&/gu, '&amp;').replace(/</gu, '&lt;').replace(/>/gu, '&gt;');
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
