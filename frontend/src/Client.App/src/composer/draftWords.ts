// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type {
    MailBlockAlignment,
    MailBody,
    MailDocumentBlock,
    MailInlineImage,
    MailInlineRun,
    MailTableBlock,
    MailTableCell,
    MailTextEmphasis,
    MailTextSize,
} from '@mailfathom/client-backend';
import { newIdentifier } from '../agent/newIdentifier';
import { isPictureSide, octetsOf, pictureIn, refusalOfAdding, type HeldPicture } from './pictures';
import { spanned, tableWithinBounds } from './writtenTable';
import {
    followable,
    writtenColour,
    type WrittenAlignment,
    type WrittenElement,
    type WrittenMarkup,
    type WrittenNode,
    type WrittenSize,
} from './writtenText';

// A draft the deployment holds, read back into the closed tree the composer writes. It is the one direction
// `writtenText.ts` does not already cover: that module reads an editable region into the tree and renders the tree
// back out, and this reads the *deployment's* description of a message into the same tree so a draft can be carried
// on with rather than only read.
//
// **It reads the reduced document rather than the sender's markup**, which is the whole reason it can exist at all.
// ADR 0024 keeps this client from parsing mail markup anywhere, and nothing here does: what arrives is already the
// typed tree the reading pane draws, checked at the client's trust boundary in `mailBody.ts`, and this walks it into a
// second typed tree. No string is parsed, no sanitizer is pinned, and what a hostile draft can put in the composer is
// bounded by what `WrittenNode` can express.
//
// **What the composer cannot write is not written.** A draft carrying a block a later deployment publishes has no shape
// here — the composer's set is what a person can type with its own controls, and inventing an approximation would put
// words on the screen the author never wrote. A table the composer could not have made — one past a bound — still has
// its cells walked, because the words in them are the message; the grid is what is lost. A body that leaves nothing the
// composer can write — one the deployment could not reduce at all, and one whose every block is a block this has no
// shape for — falls back to its plain text, which is the rendering the deployment always answers with.
//
// **A picture the draft carried inside it is one the composer holds again.** The reduced document inlines each as a
// `data:` source, which is decoded here by hand, judged by its octets like a pasted one, and held under a name of the
// composer's own — so a draft reopens with its pictures and sends them again. A remote picture is not one the draft
// carries, and is dropped, and so is one inside a table cell, which holds no picture.

/** A draft's words as the closed tree, beside the pictures that tree refers to. */
export interface DraftWords {
    readonly words: readonly WrittenNode[];
    readonly pictures: ReadonlyMap<string, HeldPicture>;
}

// The document's alignment and size as the tree spells them. What the document calls inherited or normal is the
// absence of a value in the tree rather than a fifth one.
const writtenAlignments: Readonly<Record<MailBlockAlignment, WrittenAlignment | null>> = {
    Inherited: null,
    Start: 'start',
    Center: 'center',
    End: 'end',
    Justify: 'justify',
};

const writtenSizes: Readonly<Record<MailTextSize, WrittenSize | null>> = {
    Normal: null,
    Small: 'small',
    Large: 'large',
    Larger: 'larger',
    Largest: 'largest',
};

/**
 * The words of a draft, as the closed tree the composer writes, and the pictures it carried.
 *
 * It reads the two parts any reading of a draft carries rather than one route's answer, because a draft in a folder is
 * read through the message's body and a draft MailFathom keeps through its own record, and both carry the same pair.
 */
export function draftWords(
    body: Pick<MailBody, 'document'> & { readonly plainText: { readonly text: string } },
): DraftWords {
    const pictures = new Map<string, HeldPicture>();
    const written = (body.document?.blocks ?? []).flatMap((block) => writtenBlock(block, pictures));

    return written.length === 0
        ? { words: writtenParagraphs(body.plainText.text), pictures: new Map() }
        : { words: written, pictures };
}

// One block as the elements the composer has for it. An empty array is a block it has none for, which is dropped
// rather than approximated. A picture it holds is added to the pictures it was handed.
function writtenBlock(block: MailDocumentBlock, pictures: Map<string, HeldPicture>): readonly WrittenNode[] {
    const within = (blocks: readonly MailDocumentBlock[]) => blocks.flatMap((inner) => writtenBlock(inner, pictures));

    switch (block.type) {
        case 'paragraph':
            return [aligned(block.alignment, runsWritten(block.content))];

        // The composer writes no headings, so a heading is a paragraph in the strongest emphasis it does write, at the
        // step of the size scale its level maps to — which its runs already carry. That keeps the line reading as the
        // line it was rather than levelling it into the prose around it.
        case 'heading':
            return [aligned(block.alignment, [wrapping('strong', runsWritten(block.content))])];

        case 'list':
            return [
                wrapping(
                    block.ordered ? 'ol' : 'ul',
                    block.items.map((item) => wrapping('li', within(item.blocks))),
                ),
            ];

        case 'quote':
            return [wrapping('blockquote', within(block.blocks))];

        case 'preformatted':
            return writtenParagraphs(block.text);

        case 'table':
            return writtenTable(block, within);

        // On a line of its own, which is how an editor holds a picture pasted between two lines.
        case 'image': {
            const picture = pictureHeld(block.image, pictures);

            return picture === null ? [] : [wrapping('div', [picture])];
        }

        case 'separator':
        case 'unimplemented':
            return [];
    }
}

// A picture the draft carried inside it, held under a name of the composer's own, or `null` for one it did not carry
// or the composer would not take — a remote address, a kind it does not take, or one past the bounds a composition
// holds, which a draft written elsewhere could be.
function pictureHeld(image: MailInlineImage, pictures: Map<string, HeldPicture>): WrittenNode | null {
    const encoded = /^data:[^,]*;base64,/u.exec(image.source);
    const octets = encoded === null ? null : octetsOf(image.source.slice(encoded[0].length));
    const read = octets === null ? null : pictureIn(octets);

    if (octets === null || read === null || refusalOfAdding(octets.length, [...pictures.values()]) !== null) {
        return null;
    }

    // The size it was drawn at where the draft states one, which is what the composer wrote it at.
    const drawn =
        isPictureSide(image.width) && isPictureSide(image.height)
            ? { width: image.width, height: image.height }
            : { width: read.width, height: read.height };
    const picture = newIdentifier();

    pictures.set(picture, { octets, type: read.type, ...drawn, label: image.alternativeText });

    return { element: 'img', picture, ...drawn };
}

// The runs of one paragraph, each wrapped in whatever it carries. A run is wrapped from the inside out, so a bold
// italic link is one element inside another rather than a shape this had to name.
function runsWritten(runs: readonly MailInlineRun[]): readonly WrittenNode[] {
    return runs.map((run) => {
        let written: WrittenNode = { text: run.text };

        for (const element of emphasised(run.emphasis)) {
            written = { element, address: null, holds: [written] };
        }

        const colour = run.foreground === null ? null : writtenColour(run.foreground);
        const size = writtenSizes[run.size];

        if (colour !== null || size !== null) {
            written = {
                element: 'span',
                address: null,
                ...(colour === null ? {} : { colour }),
                ...(size === null ? {} : { size }),
                holds: [written],
            };
        }

        // A link the composer would not make is drawn as its own words instead, which is the same refusal
        // `writtenText.ts` states for one somebody pastes: a scheme this client will not follow is not one it will
        // put back on the screen as a link either.
        return run.link !== null && followable(run.link.target)
            ? { element: 'a' as const, address: run.link.target, holds: [written] }
            : written;
    });
}

// Which of the composer's own emphases a run carries. Monospace is not among them: the composer writes no code and a
// run that was monospace loses that rather than borrowing an element that means something else.
function emphasised(emphasis: MailTextEmphasis): readonly WrittenElement[] {
    return [
        ...(emphasis.bold ? (['strong'] as const) : []),
        ...(emphasis.italic ? (['em'] as const) : []),
        ...(emphasis.underline ? (['u'] as const) : []),
        ...(emphasis.strikethrough ? (['s'] as const) : []),
    ];
}

/**
 * Plain text as the closed tree, one paragraph per line.
 *
 * A line is a paragraph rather than a `br` because that is what the composer's own editable region produces from
 * typing, and text that goes back through it should come out the shape it went in. Exported because a drafted message
 * arrives as plain text too — the drafting route answers no document — and reading it any other way here would be a
 * second answer to the same question.
 *
 * @param text The text, with its line breaks.
 * @returns One paragraph per line, an empty line included.
 */
export function writtenParagraphs(text: string): readonly WrittenNode[] {
    return text.split('\n').map((line) => wrapping('p', line === '' ? [] : [{ text: line }]));
}

const lineBreak: WrittenNode = { element: 'br', address: null, holds: [] };

function wrapping(element: WrittenElement, holds: readonly WrittenNode[]): WrittenNode {
    return { element, address: null, holds };
}

// A paragraph placed the way the document places it. An inherited alignment is the absence of one, so it is written as
// no alignment rather than as a fifth value.
function aligned(alignment: MailBlockAlignment, holds: readonly WrittenNode[]): WrittenNode {
    const written = writtenAlignments[alignment];

    return written === null ? wrapping('p', holds) : { element: 'p', address: null, alignment: written, holds };
}

// A table as the composer holds one: a row of header cells where the document marks a header row, and each cell's
// alignment, fill, and spans with its words as lines. A table past a bound is one the composer could not have made, so
// it is taken apart into what its cells said, in the order they said it — dropped whole, it would take the message's
// words with it.
function writtenTable(
    block: MailTableBlock,
    within: (blocks: readonly MailDocumentBlock[]) => readonly WrittenNode[],
): readonly WrittenNode[] {
    const rows = block.rows.length;
    const table: WrittenMarkup = {
        element: 'table',
        address: null,
        holds: block.rows.map((row, index) => ({
            element: 'tr',
            address: null,
            holds: row.cells.map((cell) => writtenCell(cell, row.isHeader, rows - index)),
        })),
    };

    return tableWithinBounds(table)
        ? [table]
        : block.rows.flatMap((row) => row.cells.flatMap((cell) => within(cell.blocks)));
}

// One cell. A span reaching past the last row is drawn to the last row anyway, so it is read as reaching there.
function writtenCell(cell: MailTableCell, header: boolean, rowsFromHere: number): WrittenMarkup {
    const alignment = writtenAlignments[cell.alignment];
    const background = cell.background === null ? null : writtenColour(cell.background);

    return spanned(
        {
            element: header ? 'th' : 'td',
            address: null,
            ...(alignment === null || alignment === 'start' ? {} : { alignment }),
            ...(background === null ? {} : { background }),
            holds: cellLines(cell.blocks),
        },
        cell.columnSpan,
        Math.min(cell.rowSpan, rowsFromHere),
    );
}

// What a cell holds, as a paragraph holds it: each block a line, a break between one line and the next. A list, a
// quote, and a table inside the cell are none of them something a cell holds, so each is read as its lines too.
function cellLines(blocks: readonly MailDocumentBlock[]): readonly WrittenNode[] {
    return blocks
        .map(cellLine)
        .filter((line) => line.length > 0)
        .flatMap((line, index) => (index === 0 ? line : [lineBreak, ...line]));
}

function cellLine(block: MailDocumentBlock): readonly WrittenNode[] {
    switch (block.type) {
        case 'paragraph':
            return runsWritten(block.content);
        case 'heading':
            return [wrapping('strong', runsWritten(block.content))];
        case 'list':
            return cellLines(block.items.flatMap((item) => item.blocks));
        case 'quote':
            return cellLines(block.blocks);
        case 'preformatted':
            return block.text
                .split('\n')
                .flatMap((line, index) => [
                    ...(index === 0 ? [] : [lineBreak]),
                    ...(line === '' ? [] : [{ text: line }]),
                ]);
        case 'table':
            return cellLines(block.rows.flatMap((row) => row.cells.flatMap((cell) => cell.blocks)));
        case 'image':
        case 'separator':
        case 'unimplemented':
            return [];
    }
}
