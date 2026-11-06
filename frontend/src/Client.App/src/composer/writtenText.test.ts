// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    colourOfRgb,
    followable,
    htmlOf,
    pastedAddress,
    plainTextOf,
    writtenIn,
    writtenTableIn,
    writtenTextIn,
    picturesNamedIn,
    sentSizeOf,
    type PictureAt,
    type RunDrawing,
    type WrittenMarkup,
    type WrittenNode,
} from './writtenText';

const noPicture: PictureAt = () => null;
const asContentIdentifier = (picture: string) => ({ address: `cid:${picture}`, label: null });
const nothingKept = () => false;

const pictureName = '01890a5d-ac96-774b-bcce-b302099a8057';
const otherPictureName = '01890a5d-ac96-774b-bcce-b302099a8058';

// A region shaped the way a browser leaves one after somebody has typed and formatted in it. It is built element by
// element rather than from a string of markup, for the reason the module itself gives: nothing on this path turns a
// string into nodes, and a test that did would be proving the code against an arrangement it refuses.
function region(...holds: (Node | string)[]): HTMLElement {
    return element('div', ...holds);
}

function element(name: string, ...holds: (Node | string)[]): HTMLElement {
    const made = document.createElement(name);

    made.append(...holds);

    return made;
}

describe('writtenIn', () => {
    it('reads what was typed and the emphasis it was given', () => {
        const written = writtenIn(region('Read the ', element('b', 'third'), ' column.'), noPicture);

        expect(written).toEqual([
            { text: 'Read the ' },
            { element: 'b', address: null, holds: [{ text: 'third' }] },
            { text: ' column.' },
        ]);
    });

    it('unwraps an element it has no name for, so what was typed inside one survives', () => {
        const written = writtenIn(region(element('font', element('b', 'Bold'))), noPicture);

        expect(written).toEqual([{ element: 'b', address: null, holds: [{ text: 'Bold' }] }]);
    });

    it('keeps a link somebody could follow', () => {
        const link = element('a', 'the invoice');

        link.setAttribute('href', 'https://example.invalid/invoice');

        expect(writtenIn(region(link), noPicture)).toEqual([
            { element: 'a', address: 'https://example.invalid/invoice', holds: [{ text: 'the invoice' }] },
        ]);
    });

    it.each([['javascript:alert(1)'], ['data:text/html,<script>'], ['/drafts/1'], ['']])(
        'keeps the words of a link to %s and not the link',
        (address) => {
            const link = element('a', 'press here');

            link.setAttribute('href', address);

            expect(writtenIn(region(link), noPicture)).toEqual([{ text: 'press here' }]);
        },
    );

    it('carries no attribute an engine left behind, a link’s address excepted', () => {
        const emphasis = element('b', 'Bold');

        emphasis.setAttribute('onclick', 'steal()');
        emphasis.setAttribute('style', 'position:fixed');

        expect(writtenIn(region(emphasis), noPicture)).toEqual([
            { element: 'b', address: null, holds: [{ text: 'Bold' }] },
        ]);
    });

    it('reads a list as the list it is', () => {
        const written = writtenIn(region(element('ul', element('li', 'One'), element('li', 'Two'))), noPicture);

        expect(written).toEqual([
            {
                element: 'ul',
                address: null,
                holds: [
                    { element: 'li', address: null, holds: [{ text: 'One' }] },
                    { element: 'li', address: null, holds: [{ text: 'Two' }] },
                ],
            },
        ]);
    });

    it('cuts a run longer than a kept message may carry, rather than composing one that is refused on reload', () => {
        const written = writtenIn(region('a'.repeat(150_000)), noPicture);

        expect(plainTextOf(written).length).toBeLessThan(150_000);
        expect(writtenTextIn(JSON.parse(JSON.stringify(written)) as unknown, nothingKept)).not.toBeNull();
    });

    it('stops at the count a kept message may carry, rather than composing one that is refused on reload', () => {
        const many = region();

        for (let node = 0; node < 120_000; node += 1) {
            many.append(element('div', 'One'));
        }

        const written = writtenIn(many, noPicture);

        expect(written.length).toBeLessThan(120_000);
        expect(writtenTextIn(JSON.parse(JSON.stringify(written)) as unknown, nothingKept)).not.toBeNull();
    });

    it('reads the colour and the size an engine wrote on one font element as one run carrying both', () => {
        const font = element('font', 'urgent');

        font.setAttribute('color', '#B42318');
        font.setAttribute('size', '4');

        expect(writtenIn(region(font), noPicture)).toEqual([
            { element: 'span', address: null, colour: '#b42318', size: 'large', holds: [{ text: 'urgent' }] },
        ]);
    });

    it.each([
        ['1', 'small'],
        ['2', 'small'],
        ['4', 'large'],
        ['5', 'larger'],
        ['6', 'largest'],
        ['7', 'largest'],
    ])('reads a font of size %s as the %s step of the scale', (written, step) => {
        const font = element('font', 'words');

        font.setAttribute('size', written);

        expect(writtenIn(region(font), noPicture)).toEqual([
            { element: 'span', address: null, size: step, holds: [{ text: 'words' }] },
        ]);
    });

    it.each([
        ['a normal size', 'size', '3'],
        ['a relative size', 'size', '+1'],
        ['a colour by name', 'color', 'red'],
        ['a colour with transparency', 'color', '#b4231880'],
        ['a colour in a function', 'color', 'rgb(180, 35, 24)'],
    ])('drops %s rather than approximating it, keeping the words', (_, attribute, value) => {
        const font = element('font', 'words');

        font.setAttribute(attribute, value);

        expect(writtenIn(region(font), noPicture)).toEqual([{ text: 'words' }]);
    });

    it('reads no colour or size off a span when nothing says how the region draws one', () => {
        const span = element('span', 'merged');

        span.setAttribute('style', 'color: rgb(180, 35, 24); font-size: 24px');

        expect(writtenIn(region(span), noPicture)).toEqual([{ text: 'merged' }]);
    });

    it('reads the colour and the size an engine resolved onto a span back through how the region draws them', () => {
        const span = element('span', 'moved');
        const drawing: RunDrawing = {
            colourDrawnAs: (drawn) => (drawn === 'rgb(249, 112, 102)' ? '#b42318' : null),
            sizeDrawnAs: (drawn) => (drawn === '18px' ? 'large' : null),
        };

        span.setAttribute('style', 'color: rgb(249, 112, 102); font-size: 18px');

        expect(writtenIn(region(span), noPicture, drawing)).toEqual([
            { element: 'span', address: null, colour: '#b42318', size: 'large', holds: [{ text: 'moved' }] },
        ]);
    });

    it('reads the size an engine carried onto an emphasis it merged into a line as the run around that emphasis', () => {
        const bold = element('b', 'Yard handover');
        const drawing: RunDrawing = {
            colourDrawnAs: () => null,
            sizeDrawnAs: (drawn) => (drawn === '25.995px' ? 'largest' : null),
        };

        bold.setAttribute('style', 'font-size: 25.995px; text-wrap-mode: initial;');

        expect(writtenIn(region(bold), noPicture, drawing)).toEqual([
            {
                element: 'span',
                address: null,
                size: 'largest',
                holds: [{ element: 'b', address: null, holds: [{ text: 'Yard handover' }] }],
            },
        ]);
    });

    it('asks the region about a font’s colour too, so one written in the region’s own colour is no colour', () => {
        const font = element('font', 'plain');
        const drawing: RunDrawing = {
            colourDrawnAs: (drawn) => (drawn === '#3d4350' ? null : drawn),
            sizeDrawnAs: () => null,
        };

        font.setAttribute('color', '#3D4350');

        expect(writtenIn(region(font), noPicture, drawing)).toEqual([{ text: 'plain' }]);
    });

    it.each([['constructor'], ['toString']])(
        'reads a font of size %s as no size, rather than as an inherited member',
        (value) => {
            const font = element('font', 'words');

            font.setAttribute('size', value);

            expect(writtenIn(region(font), noPicture)).toEqual([{ text: 'words' }]);
        },
    );

    it.each([['constructor'], ['__proto__'], ['hasOwnProperty']])(
        'reads a block aligned %s as not aligned at all',
        (value) => {
            const block = element('div', 'Line');

            block.setAttribute('align', value);

            expect(writtenIn(region(block), noPicture)).toEqual([
                { element: 'div', address: null, holds: [{ text: 'Line' }] },
            ]);
        },
    );

    it('drops a span whose colour and size the region draws no choice in, keeping the words', () => {
        const span = element('span', 'inherited');
        const drawing: RunDrawing = { colourDrawnAs: () => null, sizeDrawnAs: () => null };

        span.setAttribute('style', 'color: rgb(1, 2, 3); font-size: 15px');

        expect(writtenIn(region(span), noPicture, drawing)).toEqual([{ text: 'inherited' }]);
    });

    it.each([
        ['p', 'text-align: center', 'center'],
        ['div', 'text-align: right', 'end'],
        ['li', 'text-align: left', 'start'],
        ['p', 'text-align: justify', 'justify'],
    ])('reads the alignment a %s states as %s', (block, style, alignment) => {
        const aligned = element(block, 'Line');

        aligned.setAttribute('style', style);

        expect(writtenIn(region(aligned), noPicture)).toEqual([
            { element: block, address: null, alignment: alignment, holds: [{ text: 'Line' }] },
        ]);
    });

    it('reads an alignment an engine wrote as the old attribute', () => {
        const aligned = element('div', 'Line');

        aligned.setAttribute('align', 'center');

        expect(writtenIn(region(aligned), noPicture)).toEqual([
            { element: 'div', address: null, alignment: 'center', holds: [{ text: 'Line' }] },
        ]);
    });

    it('reads no alignment on a block that cannot carry one', () => {
        const quotation = element('blockquote', 'Quoted');

        quotation.setAttribute('style', 'text-align: center');

        expect(writtenIn(region(quotation), noPicture)).toEqual([
            { element: 'blockquote', address: null, holds: [{ text: 'Quoted' }] },
        ]);
    });

    it('reads a region nested past the bound as words, so what it composes is always kept and read back', () => {
        let deepest = element('div', 'Still here');

        for (let depth = 0; depth < 200; depth += 1) {
            deepest = element('blockquote', deepest);
        }

        const written = writtenIn(region(deepest), noPicture);

        expect(plainTextOf(written)).toContain('Still here');
        expect(writtenTextIn(JSON.parse(JSON.stringify(written)) as unknown, nothingKept)).not.toBeNull();
    });

    it('keeps a picture by the name its address resolves to, and drops one whose address resolves to nothing', () => {
        const issued = element('img');
        const remote = element('img');
        const pictureAt: PictureAt = (source) =>
            source === 'blob:composer/one' ? { picture: pictureName, width: 800, height: 400 } : null;

        issued.setAttribute('src', 'blob:composer/one');
        remote.setAttribute('src', 'https://example.invalid/tracker.png');

        expect(writtenIn(region('Before', issued, remote, 'after'), pictureAt)).toEqual([
            { text: 'Before' },
            { element: 'img', picture: pictureName, width: 800, height: 400 },
            { text: 'after' },
        ]);
    });
});

describe('picturesNamedIn', () => {
    it('names each picture once, in the order the tree first refers to it', () => {
        const written: readonly WrittenNode[] = [
            { element: 'img', picture: otherPictureName, width: 1, height: 1 },
            { element: 'div', address: null, holds: [{ element: 'img', picture: pictureName, width: 1, height: 1 }] },
            { element: 'img', picture: otherPictureName, width: 1, height: 1 },
        ];

        expect(picturesNamedIn(written)).toEqual([otherPictureName, pictureName]);
    });
});

describe('sentSizeOf', () => {
    it.each([
        [400, 300, 400, 300],
        [1200, 900, 600, 450],
        [6000, 1, 600, 1],
    ])('writes a %dx%d picture at %dx%d', (width, height, sentWidth, sentHeight) => {
        expect(sentSizeOf({ element: 'img', picture: pictureName, width, height })).toEqual({
            width: sentWidth,
            height: sentHeight,
        });
    });
});

describe('colourOfRgb', () => {
    it.each([
        ['rgb(180, 35, 24)', '#b42318'],
        [' #B42318 ', '#b42318'],
        ['rgb(0, 0, 0)', '#000000'],
    ])('reads %s as %s', (drawn, colour) => {
        expect(colourOfRgb(drawn)).toBe(colour);
    });

    it.each([['oklch(0.44 0.02 262)'], ['rgba(180, 35, 24, 0.5)'], ['rgb(300, 35, 24)'], ['red'], ['']])(
        'reads no colour out of %s',
        (drawn) => {
            expect(colourOfRgb(drawn)).toBeNull();
        },
    );
});

describe('htmlOf', () => {
    it('writes a picture by the source and the name the reading asks for, and differs in nothing else', () => {
        const written: readonly WrittenNode[] = [{ element: 'img', picture: pictureName, width: 1200, height: 900 }];

        expect(htmlOf(written, asContentIdentifier)).toBe(
            `<img src="cid:${pictureName}" width="600" height="450" style="max-width:100%;height:auto" />`,
        );
        expect(htmlOf(written, () => ({ address: 'blob:composer/one', label: '"chart" <1>.png' }))).toBe(
            '<img src="blob:composer/one" alt="&quot;chart&quot; &lt;1&gt;.png" width="600" height="450" style="max-width:100%;height:auto" />',
        );
    });

    it('leaves a picture out of the plain text', () => {
        expect(
            plainTextOf([
                { text: 'See' },
                { element: 'img', picture: pictureName, width: 1, height: 1 },
                { text: ' here' },
            ]),
        ).toBe('See here');
    });

    it('escapes what somebody typed rather than letting it become markup', () => {
        expect(htmlOf([{ text: 'a < b && c > d' }], asContentIdentifier)).toBe('a &lt; b &amp;&amp; c &gt; d');
    });

    it('escapes a link’s address in the attribute it is written into', () => {
        const written: readonly WrittenNode[] = [
            { element: 'a', address: 'https://example.invalid/?a="b&c', holds: [{ text: 'there' }] },
        ];

        expect(htmlOf(written, asContentIdentifier)).toBe(
            '<a href="https://example.invalid/?a=&quot;b&amp;c">there</a>',
        );
    });

    it('writes a line break as the element that carries no content', () => {
        expect(
            htmlOf(
                [{ text: 'One' }, { element: 'br', address: null, holds: [] }, { text: 'Two' }],
                asContentIdentifier,
            ),
        ).toBe('One<br />Two');
    });

    it('writes a run’s colour and size as an inline style from the closed values', () => {
        const written: readonly WrittenNode[] = [
            { element: 'span', address: null, colour: '#175cd3', size: 'larger', holds: [{ text: 'Note' }] },
        ];

        expect(htmlOf(written, asContentIdentifier)).toBe('<span style="color:#175cd3;font-size:24px">Note</span>');
    });

    it.each([
        ['small', '12px'],
        ['large', '18px'],
        ['larger', '24px'],
        ['largest', '32px'],
    ] as const)('writes the %s step as %s', (size, length) => {
        expect(htmlOf([{ element: 'span', address: null, size, holds: [{ text: 'a' }] }], asContentIdentifier)).toBe(
            `<span style="font-size:${length}">a</span>`,
        );
    });

    it.each([
        ['start', 'left'],
        ['center', 'center'],
        ['end', 'right'],
        ['justify', 'justify'],
    ] as const)('writes the %s alignment as %s, the composer writing no direction', (alignment, written) => {
        expect(htmlOf([{ element: 'p', address: null, alignment, holds: [{ text: 'a' }] }], asContentIdentifier)).toBe(
            `<p style="text-align:${written}">a</p>`,
        );
    });

    it('writes no colour that is not one, whatever the tree was handed', () => {
        expect(
            htmlOf(
                [{ element: 'span', address: null, colour: 'red;background:url(x)', holds: [{ text: 'a' }] }],
                asContentIdentifier,
            ),
        ).toBe('<span>a</span>');
    });
});

describe('plainTextOf', () => {
    it('reads each line an engine wrote as its own line', () => {
        const written = writtenIn(region(element('div', 'One'), element('div', 'Two')), noPicture);

        expect(plainTextOf(written)).toBe('One\nTwo');
    });

    it('reads a paragraph as a break in the reading rather than the next line', () => {
        const written = writtenIn(region(element('p', 'One'), element('p', 'Two')), noPicture);

        expect(plainTextOf(written)).toBe('One\n\nTwo');
    });

    it('marks a bulleted list so it still reads as one with no markup to draw it', () => {
        const written = writtenIn(region(element('ul', element('li', 'One'), element('li', 'Two'))), noPicture);

        expect(plainTextOf(written)).toBe('- One\n- Two');
    });

    it('numbers a numbered list from where it starts', () => {
        const written = writtenIn(region(element('ol', element('li', 'First'), element('li', 'Second'))), noPicture);

        expect(plainTextOf(written)).toBe('1. First\n2. Second');
    });

    it('quotes what was quoted, as mail has always written a quotation', () => {
        const written = writtenIn(
            region(element('blockquote', element('div', 'You wrote'), element('div', 'this')), 'Agreed.'),
            noPicture,
        );

        expect(plainTextOf(written)).toBe('> You wrote\n> this\nAgreed.');
    });

    it('reads emphasis as the words it emphasized, there being no plain-text spelling of it', () => {
        const written = writtenIn(region('Read the ', element('b', 'third'), ' column.'), noPicture);

        expect(plainTextOf(written)).toBe('Read the third column.');
    });

    it('reads a coloured, sized, aligned line as its words alone', () => {
        const written: readonly WrittenNode[] = [
            {
                element: 'p',
                address: null,
                alignment: 'center',
                holds: [{ element: 'span', address: null, colour: '#b42318', size: 'large', holds: [{ text: 'Due' }] }],
            },
            { element: 'p', address: null, holds: [{ text: 'Friday' }] },
        ];

        expect(plainTextOf(written)).toBe('Due\n\nFriday');
    });

    it('is nothing for a message nobody has written in', () => {
        expect(plainTextOf([])).toBe('');
        expect(plainTextOf([{ element: 'br', address: null, holds: [] }])).toBe('');
    });
});

describe('followable', () => {
    it.each([
        ['https://example.invalid/invoice', true],
        ['http://example.invalid', true],
        ['mailto:ada@example.invalid', true],
        ['javascript:alert(1)', false],
        ['data:text/html,<script>', false],
        ['/drafts/1', false],
        ['example.invalid', false],
        ['', false],
    ])('reads %s as a link this client would make: %s', (address, expected) => {
        expect(followable(address)).toBe(expected);
    });
});

describe('pastedAddress', () => {
    it.each([
        ['https://example.invalid/invoice', 'https://example.invalid/invoice'],
        ['  https://example.invalid/invoice\n', 'https://example.invalid/invoice'],
        ['mailto:ada@example.invalid', 'mailto:ada@example.invalid'],
        ['javascript:alert(1)', null],
        ['data:text/html,<script>', null],
        ['/drafts/1', null],
        ['https://example.invalid/invoice and the rest', null],
        ['https://example.invalid/a https://example.invalid/b', null],
        ['See https://example.invalid/invoice', null],
        ['', null],
    ])('reads a paste of %j as the address %j', (pasted, expected) => {
        expect(pastedAddress(pasted)).toBe(expected);
    });
});

describe('writtenTextIn', () => {
    it('reads back a message this client wrote', () => {
        const written: readonly WrittenNode[] = [
            { element: 'div', address: null, holds: [{ text: 'Half a sentence' }] },
        ];

        expect(writtenTextIn(JSON.parse(JSON.stringify(written)), nothingKept)).toEqual(written);
    });

    it('reads back the colours, sizes, and alignments this client wrote', () => {
        const written: readonly WrittenNode[] = [
            {
                element: 'li',
                address: null,
                alignment: 'justify',
                holds: [
                    { element: 'span', address: null, colour: '#667085', holds: [{ text: 'quiet' }] },
                    { element: 'span', address: null, size: 'small', holds: [{ text: 'print' }] },
                ],
            },
        ];

        expect(writtenTextIn(JSON.parse(JSON.stringify(written)), nothingKept)).toEqual(written);
    });

    it.each([
        ['a colour that is not #rrggbb', [{ element: 'span', address: null, colour: 'red', holds: [] }]],
        [
            'a colour in a spelling this client does not write',
            [{ element: 'span', address: null, colour: '#B42318', holds: [] }],
        ],
        ['a step off the scale', [{ element: 'span', address: null, size: 'huge', holds: [] }]],
        ['normal written as a step', [{ element: 'span', address: null, size: 'normal', holds: [] }]],
        ['an alignment outside the four', [{ element: 'p', address: null, alignment: 'left', holds: [] }]],
        ['a colour on something that is not a run', [{ element: 'p', address: null, colour: '#b42318', holds: [] }]],
        ['a size on something that is not a run', [{ element: 'b', address: null, size: 'large', holds: [] }]],
        ['an alignment on a run', [{ element: 'span', address: null, size: 'large', alignment: 'center', holds: [] }]],
        [
            'an alignment on a block that carries none',
            [{ element: 'blockquote', address: null, alignment: 'end', holds: [] }],
        ],
        ['a run carrying neither a colour nor a size', [{ element: 'span', address: null, holds: [] }]],
    ])('refuses %s', (_, kept) => {
        expect(writtenTextIn(kept, nothingKept)).toBeNull();
    });

    it.each([
        ['something that is not a list of nodes at all', 'Half a sentence'],
        ['a node that is neither text nor an element', [{}]],
        ['text that is not text', [{ text: 7 }]],
        ['an element this client cannot write', [{ element: 'script', address: null, holds: [] }]],
        ['an element whose contents are not a list', [{ element: 'div', address: null, holds: 'here' }]],
        ['a link to somewhere this client would not follow', [{ element: 'a', address: 'javascript:x', holds: [] }]],
        [
            'an address on something that is not a link',
            [{ element: 'blockquote', address: 'https://example.invalid', holds: [] }],
        ],
    ])('refuses %s', (_, kept) => {
        expect(writtenTextIn(kept, nothingKept)).toBeNull();
    });

    it('refuses a message nested deeper than anything the composer writes', () => {
        let deepest: WrittenNode = { text: 'here' };

        for (let depth = 0; depth < 200; depth += 1) {
            deepest = { element: 'div', address: null, holds: [deepest] };
        }

        expect(writtenTextIn([deepest], nothingKept)).toBeNull();
    });

    it('reads back a picture the kept pictures hold, and drops one they do not without refusing the rest', () => {
        const kept = [
            { element: 'img', picture: pictureName, width: 10, height: 20 },
            { text: 'words' },
            { element: 'img', picture: otherPictureName, width: 10, height: 20 },
        ];

        expect(writtenTextIn(kept, (picture) => picture === pictureName)).toEqual([
            { element: 'img', picture: pictureName, width: 10, height: 20 },
            { text: 'words' },
        ]);
    });

    it.each([
        ['a name the composer never mints', { element: 'img', picture: 'one.png', width: 10, height: 20 }],
        [
            'a side that is not a whole number of pixels',
            { element: 'img', picture: pictureName, width: 10.5, height: 20 },
        ],
        ['a side of nothing', { element: 'img', picture: pictureName, width: 0, height: 20 }],
        ['a side past any header', { element: 'img', picture: pictureName, width: 10, height: 70_000 }],
    ])('refuses a picture with %s', (_, picture) => {
        expect(writtenTextIn([picture], () => true)).toBeNull();
    });
});

describe('a table', () => {
    function cell(text: string, carries: Partial<WrittenMarkup> = {}, element: 'td' | 'th' = 'td'): WrittenMarkup {
        return { element, address: null, ...carries, holds: text === '' ? [] : [{ text }] };
    }

    function row(...cells: WrittenNode[]): WrittenMarkup {
        return { element: 'tr', address: null, holds: cells };
    }

    function table(...rows: WrittenNode[]): WrittenMarkup {
        return { element: 'table', address: null, holds: rows };
    }

    function attributed(made: HTMLElement, attributes: Readonly<Record<string, string>>): HTMLElement {
        for (const [name, value] of Object.entries(attributes)) {
            made.setAttribute(name, value);
        }

        return made;
    }

    const lineBreak: WrittenNode = { element: 'br', address: null, holds: [] };

    describe('read from the region', () => {
        it('is read row by row, through the body an engine wraps its rows in', () => {
            const read = writtenIn(
                region(
                    element(
                        'table',
                        element(
                            'tbody',
                            element('tr', element('th', 'Item'), element('th', 'Price')),
                            element('tr', element('td', 'Tea'), element('td', '3')),
                        ),
                    ),
                ),
                noPicture,
            );

            expect(read).toEqual([
                table(row(cell('Item', {}, 'th'), cell('Price', {}, 'th')), row(cell('Tea'), cell('3'))),
            ]);
        });

        it('keeps what a cell carries: its alignment, its fill, and its spans', () => {
            const read = writtenIn(
                region(
                    element(
                        'table',
                        element(
                            'tr',
                            attributed(element('td', '12.50'), {
                                style: 'text-align:right;background-color:#F0F4FF',
                                colspan: '2',
                            }),
                        ),
                        element('tr', element('td', 'a'), attributed(element('td', 'b'), { bgcolor: '#FDE8E8' })),
                    ),
                ),
                noPicture,
            );

            expect(read).toEqual([
                table(
                    row(cell('12.50', { alignment: 'end', background: '#f0f4ff', columnSpan: 2 })),
                    row(cell('a'), cell('b', { background: '#fde8e8' })),
                ),
            ]);
        });

        it('reads a start alignment as none, since every cell is drawn at the start anyway', () => {
            const read = writtenIn(
                region(element('table', element('tr', attributed(element('td', 'a'), { style: 'text-align:left' })))),
                noPicture,
            );

            expect(read).toEqual([table(row(cell('a')))]);
        });

        it('drops the break an engine keeps at the end of a cell to stand on', () => {
            const read = writtenIn(
                region(
                    element('table', element('tr', element('td', element('br')), element('td', 'a', element('br')))),
                ),
                noPicture,
            );

            expect(read).toEqual([table(row(cell(''), cell('a')))]);
        });

        it('reads a block inside a cell as the lines it held', () => {
            const read = writtenIn(
                region(
                    element('table', element('tr', element('td', 'one', element('div', 'two'), element('p', 'three')))),
                ),
                noPicture,
            );

            expect(read).toEqual([
                table(
                    row({
                        element: 'td',
                        address: null,
                        holds: [{ text: 'one' }, lineBreak, { text: 'two' }, lineBreak, { text: 'three' }],
                    }),
                ),
            ]);
        });

        it('takes a table inside a cell apart into what its cells said, a line each', () => {
            const nested = element('table', element('tr', element('td', 'x'), element('td', 'y')));
            const read = writtenIn(region(element('table', element('tr', element('td', nested)))), noPicture);

            expect(read).toEqual([
                table(row({ element: 'td', address: null, holds: [{ text: 'x' }, lineBreak, { text: 'y' }] })),
            ]);
        });

        it('takes a table one column past the bound apart into its cells’ lines', () => {
            const wide = element('tr', ...Array.from({ length: 21 }, (_, index) => element('td', `c${String(index)}`)));
            const read = writtenIn(region(element('table', wide)), noPicture);

            expect(read).toHaveLength(21);
            expect(read[0]).toEqual({ element: 'div', address: null, holds: [{ text: 'c0' }] });
        });

        it('reads a row span reaching past the last row as reaching the last row', () => {
            const read = writtenIn(
                region(
                    element(
                        'table',
                        element('tr', attributed(element('td', 'a'), { rowspan: '5' }), element('td', 'b')),
                        element('tr', element('td', 'c')),
                    ),
                ),
                noPicture,
            );

            expect(read).toEqual([table(row(cell('a', { rowSpan: 2 }), cell('b')), row(cell('c')))]);
        });

        it('is read alone for an edit, and not at all where it is past a bound', () => {
            const wide = element('tr', ...Array.from({ length: 21 }, () => element('td', 'a')));

            expect(writtenTableIn(element('table', element('tr', element('td', 'a'))))).toEqual(table(row(cell('a'))));
            expect(writtenTableIn(element('table', wide))).toBeNull();
        });
    });

    describe('written out', () => {
        const grid = 'border:1px solid #d0d5dd;padding:4px 8px;vertical-align:top';

        it('is a bordered grid whose every cell says its alignment, with its fill and spans where it has them', () => {
            const written = table(
                row(cell('Item', {}, 'th')),
                row(cell('3', { alignment: 'end', background: '#f0f4ff', columnSpan: 2, rowSpan: 3 })),
            );

            expect(htmlOf([written], asContentIdentifier)).toBe(
                `<table style="border-collapse:collapse"><tr><th style="${grid};text-align:left">Item</th></tr>` +
                    `<tr><td colspan="2" rowspan="3" style="${grid};background-color:#f0f4ff;text-align:right">3</td></tr></table>`,
            );
        });

        it('gives a cell ending on a break a second break, so the line it ends is drawn', () => {
            expect(
                htmlOf([{ element: 'td', address: null, holds: [{ text: 'a' }, lineBreak] }], asContentIdentifier),
            ).toBe(`<td style="${grid};text-align:left">a<br /><br /></td>`);
        });

        it('gives the editor an empty cell a break to put the caret on, and the HTML part none', () => {
            expect(htmlOf([cell('')], asContentIdentifier)).toBe(`<td style="${grid};text-align:left"></td>`);
            expect(htmlOf([cell('')], asContentIdentifier, 'editor')).toBe(
                `<td style="${grid};text-align:left"><br /></td>`,
            );
        });

        it('spells a run for the editor as the font the editing commands write, and for the mail as a style', () => {
            const run: WrittenMarkup = {
                element: 'span',
                address: null,
                colour: '#b42318',
                size: 'large',
                holds: [{ text: 'late' }],
            };

            expect(htmlOf([run], asContentIdentifier, 'editor')).toBe('<font color="#b42318" size="4">late</font>');
            expect(htmlOf([run], asContentIdentifier)).toBe('<span style="color:#b42318;font-size:18px">late</span>');
        });

        it('reads in plain text as a row to a line, its cells side by side', () => {
            const written = table(
                row(cell('Item', {}, 'th'), cell('Price', {}, 'th')),
                row(
                    { element: 'td', address: null, holds: [{ text: 'Green' }, lineBreak, { text: 'tea' }] },
                    cell('3'),
                ),
            );

            expect(plainTextOf([{ element: 'p', address: null, holds: [{ text: 'Prices:' }] }, written])).toBe(
                'Prices:\n\nItem | Price\nGreen tea | 3',
            );
        });
    });

    describe('kept and read back', () => {
        it('reads back a table this client wrote, with everything its cells carry', () => {
            const kept = [
                table(
                    row(cell('Item', { alignment: 'center' }, 'th')),
                    row(cell('3', { alignment: 'end', background: '#f0f4ff', columnSpan: 2, rowSpan: 2 })),
                    row(cell('4')),
                ),
            ];

            expect(writtenTextIn(kept, nothingKept)).toEqual(kept);
        });

        const paragraph: WrittenMarkup = { element: 'p', address: null, holds: [] };

        it.each([
            ['a row outside a table', [row(cell('a'))]],
            ['a cell straight in a table', [table(cell('a'))]],
            ['words loose in a row', [table(row({ text: 'a' }))]],
            ['a paragraph in a cell', [table(row({ element: 'td', address: null, holds: [paragraph] }))]],
            ['a table in a cell', [table(row({ element: 'td', address: null, holds: [table(row(cell('a')))] }))]],
            ['a fill that is no colour the tree carries', [table(row(cell('a', { background: 'red' })))]],
            ['a span of one, which is no span', [table(row(cell('a', { columnSpan: 1 })))]],
            ['a fill on something that is not a cell', [{ ...paragraph, background: '#f0f4ff' }]],
            ['a span on something that is not a cell', [{ ...paragraph, columnSpan: 2 }]],
            ['a table one row past the bound', [table(...Array.from({ length: 201 }, () => row(cell('a'))))]],
            ['a table one column past the bound', [table(row(...Array.from({ length: 21 }, () => cell('a'))))]],
            ['a row span past the last row', [table(row(cell('a', { rowSpan: 2 })))]],
            ['a table with no row', [table()]],
            ['a row with no cell', [table(row(cell('a')), row())]],
            [
                'a picture in a cell',
                [
                    table(
                        row({
                            element: 'td',
                            address: null,
                            holds: [{ element: 'img', picture: pictureName, width: 1, height: 1 }],
                        }),
                    ),
                ],
            ],
        ])('refuses %s', (_, kept) => {
            expect(writtenTextIn(kept, () => true)).toBeNull();
        });
    });
});
