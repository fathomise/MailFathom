// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The composer's words read as text a line to a block, and the way back from a place in that text to the document.
//
// The words are the document's rather than React's, so a rewrite cannot be laid over them from state without throwing
// their formatting away. What it needs instead is to count: where a selection stands in the text, which whole sentences
// it touches there, and which stretch of the document each of those sentences is — however many runs of bold, links
// and colour it crosses. Every block — a paragraph, a list item, a quote, a table cell — and every line break starts a
// line, so the sentences of two paragraphs are never read as one.

const blocks = new Set([
    'P',
    'DIV',
    'LI',
    'BLOCKQUOTE',
    'UL',
    'OL',
    'TABLE',
    'TR',
    'TD',
    'TH',
    'H1',
    'H2',
    'H3',
    'PRE',
]);

/** The words of a region as text, and the two readings between that text and the document. */
export interface RegionText {
    /** The words, a line to a block. */
    readonly text: string;

    /** Where a place in the document stands in the text, or `null` where it is not inside the region. */
    readonly offsetOf: (node: Node, offset: number) => number | null;

    /** The stretch of the document a stretch of the text is, or `null` where the text holds no such stretch. */
    readonly rangeOf: (start: number, end: number) => Range | null;
}

interface TextPiece {
    readonly node: Text;
    readonly start: number;
}

/**
 * Reads a region's words into text.
 *
 * @param region The editable region, read as it stands now: a reading is stale after the region changes.
 * @returns The text and the readings between it and the region.
 */
export function regionText(region: HTMLElement): RegionText {
    const pieces: TextPiece[] = [];
    let text = '';

    function breakLine(): void {
        if (text !== '' && !text.endsWith('\n')) {
            text += '\n';
        }
    }

    function read(node: Node): void {
        if (node instanceof Text) {
            pieces.push({ node, start: text.length });
            text += node.data;

            return;
        }

        if (node instanceof HTMLBRElement) {
            text += '\n';

            return;
        }

        const block = node instanceof Element && blocks.has(node.tagName);

        if (block) {
            breakLine();
        }

        node.childNodes.forEach(read);

        if (block) {
            breakLine();
        }
    }

    region.childNodes.forEach(read);

    function offsetOf(node: Node, offset: number): number | null {
        if (node !== region && !region.contains(node)) {
            return null;
        }

        if (node instanceof Text) {
            const piece = pieces.find((candidate) => candidate.node === node);

            return piece === undefined ? null : piece.start + offset;
        }

        // A place between two children: the text from the first piece standing after it.
        const place = document.createRange();
        place.setStart(node, offset);

        const after = pieces.find(({ node: piece }) => place.comparePoint(piece, 0) >= 0);

        return after === undefined ? text.length : after.start;
    }

    function pointAt(at: number, preferEnd: boolean): { readonly node: Text; readonly offset: number } | null {
        const piece = preferEnd
            ? pieces.find(({ node, start }) => at > start && at <= start + node.data.length)
            : pieces.find(({ node, start }) => at >= start && at < start + node.data.length);

        return piece === undefined ? null : { node: piece.node, offset: at - piece.start };
    }

    function rangeOf(start: number, end: number): Range | null {
        const from = pointAt(start, false);
        const to = pointAt(end, true);

        if (from === null || to === null) {
            return null;
        }

        const range = document.createRange();
        range.setStart(from.node, from.offset);
        range.setEnd(to.node, to.offset);

        return range;
    }

    return { text, offsetOf, rangeOf };
}
