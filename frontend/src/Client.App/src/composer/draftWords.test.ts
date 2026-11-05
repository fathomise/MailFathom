// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type {
    MailBody,
    MailDocumentBlock,
    MailInlineRun,
    MailTableCell,
    MailTableRow,
    MailTextEmphasis,
} from '@mailfathom/client-backend';
import { draftWords } from './draftWords';
import { htmlOf, plainTextOf } from './writtenText';

const nothingEmphasised: MailTextEmphasis = {
    bold: false,
    italic: false,
    underline: false,
    strikethrough: false,
    monospace: false,
};

function run(text: string, emphasis: Partial<MailTextEmphasis> = {}, target: string | null = null): MailInlineRun {
    return {
        text,
        emphasis: { ...nothingEmphasised, ...emphasis },
        foreground: null,
        size: 'Normal',
        link:
            target === null
                ? null
                : {
                      target,
                      host: null,
                      asciiHost: null,
                      deception: 'NotApplicable',
                      worthWarningAbout: false,
                  },
    };
}

function paragraph(...content: readonly MailInlineRun[]): MailDocumentBlock {
    return { type: 'paragraph', content, alignment: 'Inherited' };
}

function cell(blocks: readonly MailDocumentBlock[], carries: Partial<MailTableCell> = {}): MailTableCell {
    return { columnSpan: 1, rowSpan: 1, alignment: 'Inherited', background: null, blocks, ...carries };
}

function row(isHeader: boolean, ...cells: readonly MailTableCell[]): MailTableRow {
    return { isHeader, cells };
}

function tableOf(rows: readonly MailTableRow[]): MailDocumentBlock {
    return { type: 'table', columns: [], rows };
}

// Every block the composer has no shape for, which is what it drops rather than approximates.
const undrawn: readonly MailDocumentBlock[] = [
    { type: 'separator' },
    {
        type: 'image',
        image: { source: 'cid:one', alternativeText: null, width: null, height: null },
        link: null,
        alignment: 'Inherited',
    },
    { type: 'unimplemented', identity: 'chart', version: 3 },
];

const asContentIdentifier = (picture: string) => `cid:${picture}`;

function wordsOf(body: MailBody) {
    return draftWords(body).words;
}

// The smallest PNG header that states a size: the signature, then the header chunk naming itself and 3 by 2 pixels.
const tinyPng = new Uint8Array([
    0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 3, 0, 0, 0, 2,
]);

function imageBlock(source: string, width: number | null = null, height: number | null = null): MailDocumentBlock {
    return {
        type: 'image',
        image: { source, alternativeText: 'chart.png', width, height },
        link: null,
        alignment: 'Inherited',
    };
}

function bodyOf(blocks: readonly MailDocumentBlock[], plainText = ''): MailBody {
    return {
        storedEmailId: 'message-1',
        availability: 'Readable',
        plainText: { text: plainText, originalCharacterCount: plainText.length, truncation: 'None' },
        document:
            blocks.length === 0
                ? null
                : {
                      blocks,
                      refusal: 'None',
                      removedRemoteReferenceCount: 0,
                      retainedRemoteImageCount: 0,
                      inlineImageCount: 0,
                      undrawnInlineImageCount: 0,
                      truncated: false,
                      footer: null,
                  },
        selfContainedHtml: null,
        remoteImagesRequested: false,
    };
}

describe('draftWords', () => {
    it('carries each paragraph across as a paragraph, which is what the composer writes one as', () => {
        expect(
            htmlOf(
                wordsOf(bodyOf([paragraph(run('Half a thought')), paragraph(run('and the rest'))])),
                asContentIdentifier,
            ),
        ).toBe('<p>Half a thought</p><p>and the rest</p>');
    });

    it('wraps a run in each emphasis it carries, one element inside another rather than a shape named for the pair', () => {
        expect(
            htmlOf(wordsOf(bodyOf([paragraph(run('urgent', { bold: true, italic: true }))])), asContentIdentifier),
        ).toBe('<p><em><strong>urgent</strong></em></p>');
    });

    // The composer writes no code, so borrowing an element that means something else would say what the author did
    // not: what is lost is the face the run was set in rather than the words.
    it('drops the one emphasis the composer has no element for, keeping the words that carried it', () => {
        expect(htmlOf(wordsOf(bodyOf([paragraph(run('rm -rf', { monospace: true }))])), asContentIdentifier)).toBe(
            '<p>rm -rf</p>',
        );
    });

    it('keeps a link the client would follow, and draws one it would not as its own words', () => {
        const followed = wordsOf(bodyOf([paragraph(run('the page', {}, 'https://example.test/page'))]));
        const refused = wordsOf(bodyOf([paragraph(run('the page', {}, 'javascript:alert(1)'))]));

        expect(htmlOf(followed, asContentIdentifier)).toBe('<p><a href="https://example.test/page">the page</a></p>');
        expect(htmlOf(refused, asContentIdentifier)).toBe('<p>the page</p>');
    });

    // The composer writes no headings, so the line keeps the weight that says it was one rather than levelling into
    // the prose around it.
    it('carries a heading across as the strongest emphasis the composer does write', () => {
        const heading: MailDocumentBlock = {
            type: 'heading',
            level: 2,
            content: [run('Agenda')],
            alignment: 'Inherited',
        };

        expect(htmlOf(wordsOf(bodyOf([heading])), asContentIdentifier)).toBe('<p><strong>Agenda</strong></p>');
    });

    it('keeps a run’s colour and its step of the size scale, so a reopened draft looks as it was written', () => {
        const coloured: MailInlineRun = { ...run('Due Friday', { bold: true }), foreground: '#B42318', size: 'Larger' };

        expect(htmlOf(wordsOf(bodyOf([paragraph(coloured)])), asContentIdentifier)).toBe(
            '<p><span style="color:#b42318;font-size:24px"><strong>Due Friday</strong></span></p>',
        );
    });

    it('keeps the step a heading’s level gave its words, beside the emphasis that says it was one', () => {
        const heading: MailDocumentBlock = {
            type: 'heading',
            level: 1,
            content: [{ ...run('Agenda'), size: 'Largest' }],
            alignment: 'Center',
        };

        expect(htmlOf(wordsOf(bodyOf([heading])), asContentIdentifier)).toBe(
            '<p style="text-align:center"><strong><span style="font-size:32px">Agenda</span></strong></p>',
        );
    });

    it.each([
        ['Start', 'left'],
        ['Center', 'center'],
        ['End', 'right'],
        ['Justify', 'justify'],
    ] as const)('keeps a paragraph aligned to its %s, written as %s', (alignment, written) => {
        const aligned: MailDocumentBlock = { type: 'paragraph', content: [run('Line')], alignment };

        expect(htmlOf(wordsOf(bodyOf([aligned])), asContentIdentifier)).toBe(
            `<p style="text-align:${written}">Line</p>`,
        );
    });

    it('carries a list across as the list it was, its items in the order they were written', () => {
        const list: MailDocumentBlock = {
            type: 'list',
            ordered: true,
            items: [{ blocks: [paragraph(run('first'))] }, { blocks: [paragraph(run('second'))] }],
        };

        expect(htmlOf(wordsOf(bodyOf([list])), asContentIdentifier)).toBe(
            '<ol><li><p>first</p></li><li><p>second</p></li></ol>',
        );
    });

    it('carries a quotation across as one, which is a shape the composer draws', () => {
        const quote: MailDocumentBlock = { type: 'quote', depth: 1, blocks: [paragraph(run('you wrote'))] };

        expect(htmlOf(wordsOf(bodyOf([quote])), asContentIdentifier)).toBe('<blockquote><p>you wrote</p></blockquote>');
    });

    it('carries a table across as a table, its header row, alignments, fills, and spans kept', () => {
        const table = tableOf([
            row(true, cell([paragraph(run('Day'))]), cell([paragraph(run('Hours'))])),
            row(
                false,
                cell([paragraph(run('Monday'))], { rowSpan: 2, background: '#F0F4FF' }),
                cell([paragraph(run('9'))], { alignment: 'End' }),
            ),
            row(false, cell([paragraph(run('10'))], { alignment: 'Start' })),
        ]);

        expect(wordsOf(bodyOf([table]))).toEqual([
            {
                element: 'table',
                address: null,
                holds: [
                    {
                        element: 'tr',
                        address: null,
                        holds: [
                            { element: 'th', address: null, holds: [{ text: 'Day' }] },
                            { element: 'th', address: null, holds: [{ text: 'Hours' }] },
                        ],
                    },
                    {
                        element: 'tr',
                        address: null,
                        holds: [
                            {
                                element: 'td',
                                address: null,
                                background: '#f0f4ff',
                                rowSpan: 2,
                                holds: [{ text: 'Monday' }],
                            },
                            { element: 'td', address: null, alignment: 'end', holds: [{ text: '9' }] },
                        ],
                    },
                    {
                        element: 'tr',
                        address: null,
                        holds: [{ element: 'td', address: null, holds: [{ text: '10' }] }],
                    },
                ],
            },
        ]);
    });

    it('carries what a cell holds as lines, a list, a quote, and a table inside it included', () => {
        const nested = tableOf([row(false, cell([paragraph(run('x'))]), cell([paragraph(run('y'))]))]);
        const list: MailDocumentBlock = {
            type: 'list',
            ordered: false,
            items: [{ blocks: [paragraph(run('one'))] }, { blocks: [paragraph(run('two'))] }],
        };
        const table = tableOf([row(false, cell([paragraph(run('Note')), list, nested]))]);
        const written = wordsOf(bodyOf([table]));

        expect(plainTextOf(written)).toBe('Note one two x y');
        expect(htmlOf(written, asContentIdentifier)).toContain('>Note<br />one<br />two<br />x<br />y</td>');
    });

    it('drops a picture a cell carried, since a table cell holds none', () => {
        const picture = imageBlock(`data:image/png;base64,${btoa(String.fromCharCode(...tinyPng))}`);
        const { words, pictures } = draftWords(bodyOf([tableOf([row(false, cell([paragraph(run('x')), picture]))])]));

        expect(pictures.size).toBe(0);
        expect(htmlOf(words, asContentIdentifier)).not.toContain('<img');
    });

    it('reads a row span past the last row as reaching the last row', () => {
        const table = tableOf([row(false, cell([paragraph(run('a'))], { rowSpan: 3 }))]);

        expect(htmlOf(wordsOf(bodyOf([table])), asContentIdentifier)).not.toContain('rowspan');
    });

    // A table past a bound is one the composer could not have made. Dropped whole it would take the message's words
    // with it, so what is kept is what the cells say, in the order they say it.
    it('keeps what the cells of a table past a bound say, although the grid around them is lost', () => {
        const wide = tableOf([
            row(false, ...Array.from({ length: 21 }, (_, index) => cell([paragraph(run(`c${String(index)}`))]))),
        ]);

        expect(htmlOf(wordsOf(bodyOf([wide])), asContentIdentifier)).toBe(
            Array.from({ length: 21 }, (_, index) => `<p>c${String(index)}</p>`).join(''),
        );
    });

    it('drops a block the composer has nothing to write it with, rather than approximating one', () => {
        expect(
            htmlOf(
                wordsOf(bodyOf([...undrawn.slice(0, 1), paragraph(run('one')), ...undrawn.slice(1)])),
                asContentIdentifier,
            ),
        ).toBe('<p>one</p>');
    });

    // The blocks are dropped one by one, so a message made of nothing else reduces to nothing at all — and what is
    // left to open the composer on is the rendering the deployment always carries beside the document.
    it('falls back to the plain text where every block is one it has nothing to write', () => {
        expect(plainTextOf(wordsOf(bodyOf(undrawn, 'The chart is attached')))).toBe('The chart is attached');
    });

    it('keeps preformatted text line by line, which is the shape the composer’s own region produces', () => {
        const preformatted: MailDocumentBlock = { type: 'preformatted', text: 'one\ntwo' };

        expect(htmlOf(wordsOf(bodyOf([preformatted])), asContentIdentifier)).toBe('<p>one</p><p>two</p>');
    });

    // A body the deployment could not reduce is answered as plain text, which is the rendering it always carries.
    it('falls back to the plain text where the deployment reduced no document at all', () => {
        expect(plainTextOf(wordsOf(bodyOf([], 'Sent from a phone\n\nsorry')))).toBe('Sent from a phone\n\nsorry');
    });

    it('holds a picture the draft carried inline again, under a name of its own, at the size it was drawn', () => {
        const { words, pictures } = draftWords(
            bodyOf([
                paragraph(run('See')),
                imageBlock(`data:image/png;base64,${btoa(String.fromCharCode(...tinyPng))}`, 300, 200),
            ]),
        );
        const [name] = [...pictures.keys()];

        expect(pictures.size).toBe(1);
        expect(name).toBeDefined();
        expect(pictures.get(name ?? '')).toEqual({
            octets: tinyPng,
            type: 'image/png',
            width: 300,
            height: 200,
            label: 'chart.png',
        });
        expect(words).toEqual([
            { element: 'p', address: null, holds: [{ text: 'See' }] },
            { element: 'div', address: null, holds: [{ element: 'img', picture: name, width: 300, height: 200 }] },
        ]);
    });

    it('takes the size from the picture’s own header where the draft states none', () => {
        const { words } = draftWords(
            bodyOf([imageBlock(`data:image/png;base64,${btoa(String.fromCharCode(...tinyPng))}`)]),
        );

        expect(words).toEqual([
            {
                element: 'div',
                address: null,
                holds: [expect.objectContaining({ element: 'img', width: 3, height: 2 })],
            },
        ]);
    });

    it.each([
        ['a remote address', 'https://example.invalid/chart.png'],
        ['a kind it does not take', `data:image/png;base64,${btoa('a page of text, not a picture')}`],
        ['text that is not base64', 'data:image/png;base64,not base64 at all!'],
    ])('drops a picture behind %s', (_, source) => {
        const { words, pictures } = draftWords(bodyOf([paragraph(run('one')), imageBlock(source)]));

        expect(pictures.size).toBe(0);
        expect(htmlOf(words, asContentIdentifier)).toBe('<p>one</p>');
    });
});
