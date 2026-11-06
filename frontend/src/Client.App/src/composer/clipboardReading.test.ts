// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    calcRange,
    excelRange,
    formattedBlock,
    hostileDocument,
    nestedTable,
    ranMarker,
    sheetsRange,
} from '../../../../tests/fixtures/clipboard';
import { readClipboardHtml, readTabbedText, type ClipboardReading } from './clipboardReading';
import type {
    WrittenAlignment,
    WrittenElement,
    WrittenMarkup,
    WrittenNode,
    WrittenSize,
    WrittenText,
} from './writtenText';

// The reader is proven as attacks first and as examples second: what a hostile document carries never reaches the
// tree, and only then does each construct map the way ADR 0047 states. jsdom has no canvas, so a colour here is read
// through the CSS parser's own serialization; the browser and desktop suites read named colours through the engine's.

const heldPicture = '01890a5d-ac96-774b-bcce-b302099a8057';
const mintedPicture = '01890a5d-ac96-774b-bcce-b302099a8058';
const onePixelPng = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';

function read(html: string): ClipboardReading {
    return readClipboardHtml(
        html,
        (source) => (source === 'blob:held' ? { picture: heldPicture, width: 4, height: 3 } : null),
        () => mintedPicture,
    );
}

function writtenOf(html: string): readonly WrittenNode[] {
    const reading = read(html);

    expect(reading.kind).toBe('written');

    return reading.kind === 'written' ? reading.written : [];
}

function text(words: string): WrittenText {
    return { text: words };
}

interface Carried {
    readonly address?: string;
    readonly colour?: string;
    readonly size?: WrittenSize;
    readonly alignment?: WrittenAlignment;
    readonly background?: string;
    readonly columnSpan?: number;
}

function markup(element: WrittenElement, holds: readonly WrittenNode[], carried: Carried = {}): WrittenMarkup {
    return { element, address: null, ...carried, holds };
}

function rowOf(...cells: readonly WrittenMarkup[]): WrittenMarkup {
    return markup('tr', cells);
}

function cellOf(holds: readonly WrittenNode[], carried: Carried = {}, element: 'td' | 'th' = 'td'): WrittenMarkup {
    return markup(element, holds, carried);
}

const lineBreak = markup('br', []);

describe('readClipboardHtml', () => {
    it('reads a hostile document to its words, with no script, handler, style sheet, address, or picture kept', () => {
        const written = writtenOf(hostileDocument);

        expect(written).toStrictEqual([
            markup('p', [text('Quarterly '), text('notes')]),
            markup('table', [rowOf(cellOf([text('Kept')]), cellOf([text('words')]))]),
        ]);
        expect(JSON.stringify(written)).not.toMatch(/invalid|javascript|file:|cid:|clipboardFixtureRan/u);
        expect(Reflect.get(globalThis, ranMarker)).toBeUndefined();
    });

    it('reads a formatted passage into the closed set, a heading to bold on the size scale, and the rest to its words', () => {
        expect(writtenOf(formattedBlock)).toStrictEqual([
            markup('p', [markup('span', [markup('b', [text('Yard handover')])], { size: 'largest' })]),
            markup('p', [
                text('The racking is '),
                markup('b', [text('ready')]),
                text(', the '),
                markup('i', [text('north')]),
                text(' bay is '),
                markup('u', [text('marked')]),
                text(', and the old plan is '),
                markup('s', [text('void')]),
                text('.'),
            ]),
            markup('p', [
                markup('span', [text('Gate code changes Friday.')], { colour: '#b42318' }),
                text(' '),
                text('Ask the desk'),
                text(' '),
                text('today'),
                text('.'),
            ]),
            markup('ul', [markup('li', [text('Bay 4')]), markup('li', [text('Bay 5')], { alignment: 'center' })]),
            markup('ol', [markup('li', [text('Sweep')])]),
            markup('blockquote', [text('Keep the fire lane clear.')]),
            markup('p', [
                text('See '),
                markup('a', [text('the plan')], { address: 'https://yard.example.invalid/plan' }),
                text(' or '),
                text('nothing'),
                text('.'),
            ]),
            markup('p', [markup('span', [markup('b', [text('Contacts')])], { size: 'larger' })]),
            markup('p', [markup('b', [text('Desk')])]),
            markup('p', [markup('span', [text('Small print')], { size: 'small' })]),
            markup('p', [markup('span', [text('Green and larger')], { colour: '#067647', size: 'larger' })]),
        ]);
    });

    // The three sheets mark the same range three ways: Excel by classes in a style element, Calc by `<b>`, `<i>`,
    // `<font>`, and `bgcolor`, and Sheets by inline declarations. Each arrives as the same grid with its own fill and
    // its own red. Calc states its document default as `font-size: x-small` on every cell, which ADR 0047's keyword
    // row reads as small.
    it.each([
        { sheet: 'Excel', html: excelRange, fill: '#d9e1f2', red: '#c00000', size: null },
        { sheet: 'LibreOffice Calc', html: calcRange, fill: '#dee6ef', red: '#c9211e', size: 'small' as const },
        { sheet: 'Google Sheets', html: sheetsRange, fill: '#fff2cc', red: '#ff0000', size: null },
    ])(
        'reads a range copied from $sheet as a table with its header row, alignment, emphasis, colour, and fill',
        ({ html, fill, red, size }) => {
            const sized = (holds: readonly WrittenNode[]): readonly WrittenNode[] =>
                size === null ? holds : [markup('span', holds, { size })];
            const header = (words: string) => cellOf(sized([markup('b', [text(words)])]), { background: fill }, 'th');
            const figure = (holds: readonly WrittenNode[]) => cellOf(sized(holds), { alignment: 'end' });

            expect(writtenOf(html)).toStrictEqual([
                markup('table', [
                    rowOf(header('Region'), header('Units'), header('Change')),
                    rowOf(
                        cellOf(sized([text('North yard')])),
                        figure([text('1,240')]),
                        figure([markup('span', [text('-35')], { colour: red })]),
                    ),
                    rowOf(
                        cellOf(sized([markup('i', [text('South yard')])])),
                        figure([text('980')]),
                        figure([text('12')]),
                    ),
                ]),
            ]);
        },
    );

    it('reads a row of header cells, or one in the head of the table, as the header row', () => {
        const headed = (html: string) => writtenOf(html)[0];
        const expected = markup('table', [rowOf(cellOf([text('Bay')], {}, 'th')), rowOf(cellOf([text('4')]))]);

        expect(headed('<table><tr><th>Bay</th></tr><tr><td>4</td></tr></table>')).toStrictEqual(expected);
        expect(headed('<table><thead><tr><td>Bay</td></tr></thead><tr><td>4</td></tr></table>')).toStrictEqual(
            expected,
        );
    });

    it('keeps a bold first row as ordinary cells where the row under it is bold too', () => {
        expect(writtenOf('<table><tr><td><b>A</b></td></tr><tr><td><b>B</b></td></tr></table>')).toStrictEqual([
            markup('table', [rowOf(cellOf([markup('b', [text('A')])])), rowOf(cellOf([markup('b', [text('B')])]))]),
        ]);
    });

    it('unwraps a table inside a cell to its cells words, a line to each', () => {
        expect(writtenOf(nestedTable)).toStrictEqual([
            markup('table', [
                rowOf(cellOf([text('Bay')]), cellOf([text('Shelf A'), lineBreak, text('Shelf B')])),
                rowOf(cellOf([text('4')]), cellOf([text('Empty')])),
            ]),
        ]);
    });

    it.each([
        { rows: 201, columns: 2 },
        { rows: 2, columns: 21 },
    ])('refuses a table of $rows rows and $columns columns whole, saying how large it was', ({ rows, columns }) => {
        expect(read(tableOf(rows, columns))).toStrictEqual({ kind: 'tableTooLarge', rows, columns });
    });

    it('reads a table at both bounds, and one past them only in empty rows and columns it trims', () => {
        const atTheBounds = writtenOf(tableOf(200, 20))[0];
        const trimmed = writtenOf(
            `<table>${Array.from({ length: 200 }, () => `<tr>${'<td>1</td>'.repeat(20)}<td> </td></tr>`).join('')}<tr><td></td></tr></table>`,
        )[0];

        expect(atTheBounds).toStrictEqual(trimmed);
        expect(atTheBounds !== undefined && 'holds' in atTheBounds ? atTheBounds.holds : []).toHaveLength(200);
    });

    it.each([['100%'], ['99']])('reads a cell spanning %s columns as spanning the columns the table has', (span) => {
        expect(
            writtenOf(
                `<table><tr><td colspan="${span}">Totals</td></tr><tr><td>A</td><td>B</td><td>C</td></tr></table>`,
            ),
        ).toStrictEqual([
            markup('table', [
                rowOf(cellOf([text('Totals')], { columnSpan: 3 })),
                rowOf(cellOf([text('A')]), cellOf([text('B')]), cellOf([text('C')])),
            ]),
        ]);
    });

    // Each row of ADR 0047's size table, read through an inline declaration as a pasted page states it.
    it.each([
        ['9px', 'small'],
        ['12.4px', 'small'],
        ['9pt', 'small'],
        ['x-small', 'small'],
        ['small', 'small'],
        ['12.5px', null],
        ['16.9px', null],
        ['medium', null],
        ['1em', null],
        ['17px', 'large'],
        ['13pt', 'large'],
        ['large', 'large'],
        ['21px', 'larger'],
        ['x-large', 'larger'],
        ['1.5em', 'larger'],
        ['28px', 'largest'],
        ['xx-large', 'largest'],
        ['200%', 'largest'],
    ] as const)('reads a size of %s as %s', (stated, step) => {
        const words = [text('Bay')];

        expect(writtenOf(`<p style="font-size: ${stated}">Bay</p>`)).toStrictEqual([
            markup('p', step === null ? words : [markup('span', words, { size: step })]),
        ]);
    });

    it.each([
        ['1', 'small'],
        ['2', 'small'],
        ['3', null],
        ['4', 'large'],
        ['5', 'larger'],
        ['6', 'largest'],
        ['7', 'largest'],
    ] as const)('reads a <font size> of %s as %s', (stated, step) => {
        const words = [text('Bay')];

        expect(writtenOf(`<font size="${stated}">Bay</font>`)).toStrictEqual(
            step === null ? words : [markup('span', words, { size: step })],
        );
    });

    it.each([
        ['a colour with transparency', 'color: rgba(180, 35, 24, 0.5)'],
        ['the dark near-grey body text is written in', 'color: #333333'],
        ['black', 'color: #000000'],
        ['a system colour', 'color: windowtext'],
    ])('drops %s as a foreground', (_, declaration) => {
        expect(writtenOf(`<span style="${declaration}">Bay</span>`)).toStrictEqual([text('Bay')]);
    });

    it.each([
        ['white', '#ffffff', null],
        ['a fill as light as the page', '#f8f9fa', null],
        ['a fill a shade darker than the page', '#f7f9fa', '#f7f9fa'],
    ])('reads %s as a cell fill of %s', (_, fill, kept) => {
        expect(writtenOf(`<table><tr><td style="background-color: ${fill}">4</td></tr></table>`)).toStrictEqual([
            markup('table', [rowOf(cellOf([text('4')], kept === null ? {} : { background: kept }))]),
        ]);
    });

    it('drops an emphasis a declaration takes away, as a document wraps everything it copies in bold that is not', () => {
        expect(
            writtenOf('<b style="font-weight: normal">Bay <span style="font-weight: 700">4</span></b>'),
        ).toStrictEqual([text('Bay '), markup('b', [text('4')])]);
    });

    it('reads emphasis a declaration states on any element as the emphasis the set has', () => {
        expect(
            writtenOf(
                '<span style="font-style: italic; text-decoration: underline line-through; font-weight: bold">Bay</span>',
            ),
        ).toStrictEqual([markup('b', [markup('i', [markup('u', [markup('s', [text('Bay')])])])])]);
    });

    it('reads a block it has no name for as a line, and an inline element it has no name for as its words', () => {
        expect(writtenOf('<section>Bay <mark>4</mark></section><article><code>Bay 5</code></article>')).toStrictEqual([
            markup('div', [text('Bay '), text('4')]),
            markup('div', [text('Bay 5')]),
        ]);
    });

    it('keeps the space a line break in the source draws between two runs, and drops it between blocks', () => {
        expect(writtenOf('<p><b>Ann</b>\r\n<i>today</i></p>\n<p>Next</p>')).toStrictEqual([
            markup('p', [markup('b', [text('Ann')]), text(' '), markup('i', [text('today')])]),
            markup('p', [text('Next')]),
        ]);
    });

    it('keeps a link only where it goes somewhere this client would follow', () => {
        expect(
            writtenOf(
                '<a href="mailto:desk@yard.example.invalid">Desk</a> <a href="/plan">Plan</a> <a href="data:text/html,x">Data</a>',
            ),
        ).toStrictEqual([
            markup('a', [text('Desk')], { address: 'mailto:desk@yard.example.invalid' }),
            text(' '),
            text('Plan'),
            text(' '),
            text('Data'),
        ]);
    });

    it('holds a picture the clipboard carries as a PNG, JPEG, or GIF source of its own, decoded with no request', () => {
        const reading = read(`<p>Chart</p><img src="data:image/png;base64,${onePixelPng}">`);

        expect(reading).toMatchObject({
            kind: 'written',
            written: [markup('p', [text('Chart')]), { element: 'img', picture: mintedPicture, width: 1, height: 1 }],
            pictures: [{ name: mintedPicture, picture: { type: 'image/png', width: 1, height: 1, label: null } }],
        });
    });

    it('resolves an object URL the composer issued to the picture it names, and drops any other source', () => {
        expect(
            writtenOf(
                `<img src="blob:held"><img src="blob:https://other.example.invalid/x"><img src="data:image/svg+xml;base64,PHN2Zy8+"><table><tr><td>Bay<img src="data:image/png;base64,${onePixelPng}"></td></tr></table>`,
            ),
        ).toStrictEqual([
            { element: 'img', picture: heldPicture, width: 4, height: 3 },
            markup('table', [rowOf(cellOf([text('Bay')]))]),
        ]);
    });

    it.each([
        ['nothing at all', ''],
        ['only space', '<p> </p><div>\n</div>'],
        ['only a picture from elsewhere', '<img src="https://pictures.example.invalid/chart.png">'],
    ])('reads %s as nothing, which leaves the paste to the other flavours', (_, html) => {
        expect(read(html)).toStrictEqual({ kind: 'nothing' });
    });
});

describe('readTabbedText', () => {
    it('reads lines of the same two or more tab-separated cells as a table, leaving out empty lines', () => {
        expect(readTabbedText('Region\tUnits\r\n\r\nNorth\t1,240\nSouth\t\n')).toStrictEqual({
            kind: 'written',
            written: [
                markup('table', [
                    rowOf(cellOf([text('Region')]), cellOf([text('Units')])),
                    rowOf(cellOf([text('North')]), cellOf([text('1,240')])),
                    rowOf(cellOf([text('South')]), cellOf([])),
                ]),
            ],
            pictures: [],
        });
    });

    it.each([
        ['one column', 'Region\nNorth'],
        ['lines of different widths', 'Region\tUnits\nNorth'],
        ['no tab at all', 'The racking arrives on the ninth.'],
    ])('reads %s as nothing', (_, plain) => {
        expect(readTabbedText(plain)).toStrictEqual({ kind: 'nothing' });
    });

    it.each([
        { rows: 201, columns: 2 },
        { rows: 1, columns: 21 },
    ])('refuses $rows rows of $columns cells whole', ({ rows, columns }) => {
        const plain = Array.from({ length: rows }, () => Array.from({ length: columns }, () => '1').join('\t')).join(
            '\n',
        );

        expect(readTabbedText(plain)).toStrictEqual({ kind: 'tableTooLarge', rows, columns });
    });
});

function tableOf(rows: number, columns: number): string {
    return `<table>${Array.from({ length: rows }, () => `<tr>${'<td>1</td>'.repeat(columns)}</tr>`).join('')}</table>`;
}
