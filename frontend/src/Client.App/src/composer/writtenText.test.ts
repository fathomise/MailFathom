// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    followable,
    htmlOf,
    pastedAddress,
    plainTextOf,
    writtenIn,
    writtenTextIn,
    type WrittenNode,
} from './writtenText';

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
        const written = writtenIn(region('Read the ', element('b', 'third'), ' column.'));

        expect(written).toEqual([
            { text: 'Read the ' },
            { element: 'b', address: null, holds: [{ text: 'third' }] },
            { text: ' column.' },
        ]);
    });

    it('unwraps an element it has no name for, so what was typed inside one survives', () => {
        const written = writtenIn(region(element('font', element('b', 'Bold'))));

        expect(written).toEqual([{ element: 'b', address: null, holds: [{ text: 'Bold' }] }]);
    });

    it('keeps a link somebody could follow', () => {
        const link = element('a', 'the invoice');

        link.setAttribute('href', 'https://example.invalid/invoice');

        expect(writtenIn(region(link))).toEqual([
            { element: 'a', address: 'https://example.invalid/invoice', holds: [{ text: 'the invoice' }] },
        ]);
    });

    it.each([['javascript:alert(1)'], ['data:text/html,<script>'], ['/drafts/1'], ['']])(
        'keeps the words of a link to %s and not the link',
        (address) => {
            const link = element('a', 'press here');

            link.setAttribute('href', address);

            expect(writtenIn(region(link))).toEqual([{ text: 'press here' }]);
        },
    );

    it('carries no attribute an engine left behind, a link’s address excepted', () => {
        const emphasis = element('b', 'Bold');

        emphasis.setAttribute('onclick', 'steal()');
        emphasis.setAttribute('style', 'position:fixed');

        expect(writtenIn(region(emphasis))).toEqual([{ element: 'b', address: null, holds: [{ text: 'Bold' }] }]);
    });

    it('reads a list as the list it is', () => {
        const written = writtenIn(region(element('ul', element('li', 'One'), element('li', 'Two'))));

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
        const written = writtenIn(region('a'.repeat(150_000)));

        expect(plainTextOf(written).length).toBeLessThan(150_000);
        expect(writtenTextIn(JSON.parse(JSON.stringify(written)) as unknown)).not.toBeNull();
    });

    it('stops at the count a kept message may carry, rather than composing one that is refused on reload', () => {
        const many = region();

        for (let node = 0; node < 120_000; node += 1) {
            many.append(element('div', 'One'));
        }

        const written = writtenIn(many);

        expect(written.length).toBeLessThan(120_000);
        expect(writtenTextIn(JSON.parse(JSON.stringify(written)) as unknown)).not.toBeNull();
    });

    it('reads the colour and the size an engine wrote on one font element as one run carrying both', () => {
        const font = element('font', 'urgent');

        font.setAttribute('color', '#B42318');
        font.setAttribute('size', '4');

        expect(writtenIn(region(font))).toEqual([
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

        expect(writtenIn(region(font))).toEqual([{ element: 'span', address: null, size: step, holds: [{ text: 'words' }] }]);
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

        expect(writtenIn(region(font))).toEqual([{ text: 'words' }]);
    });

    it('reads no colour or size off a span an engine left behind, which is its bookkeeping rather than a choice', () => {
        const span = element('span', 'merged');

        span.setAttribute('style', 'color: rgb(180, 35, 24); font-size: 24px');

        expect(writtenIn(region(span))).toEqual([{ text: 'merged' }]);
    });

    it.each([
        ['p', 'text-align: center', 'center'],
        ['div', 'text-align: right', 'end'],
        ['li', 'text-align: left', 'start'],
        ['p', 'text-align: justify', 'justify'],
    ])('reads the alignment a %s states as %s', (block, style, alignment) => {
        const aligned = element(block, 'Line');

        aligned.setAttribute('style', style);

        expect(writtenIn(region(aligned))).toEqual([
            { element: block, address: null, alignment: alignment, holds: [{ text: 'Line' }] },
        ]);
    });

    it('reads an alignment an engine wrote as the old attribute', () => {
        const aligned = element('div', 'Line');

        aligned.setAttribute('align', 'center');

        expect(writtenIn(region(aligned))).toEqual([
            { element: 'div', address: null, alignment: 'center', holds: [{ text: 'Line' }] },
        ]);
    });

    it('reads no alignment on a block that cannot carry one', () => {
        const quotation = element('blockquote', 'Quoted');

        quotation.setAttribute('style', 'text-align: center');

        expect(writtenIn(region(quotation))).toEqual([
            { element: 'blockquote', address: null, holds: [{ text: 'Quoted' }] },
        ]);
    });

    it('reads a region nested past the bound as words, so what it composes is always kept and read back', () => {
        let deepest = element('div', 'Still here');

        for (let depth = 0; depth < 200; depth += 1) {
            deepest = element('blockquote', deepest);
        }

        const written = writtenIn(region(deepest));

        expect(plainTextOf(written)).toContain('Still here');
        expect(writtenTextIn(JSON.parse(JSON.stringify(written)) as unknown)).not.toBeNull();
    });
});

describe('htmlOf', () => {
    it('escapes what somebody typed rather than letting it become markup', () => {
        expect(htmlOf([{ text: 'a < b && c > d' }])).toBe('a &lt; b &amp;&amp; c &gt; d');
    });

    it('escapes a link’s address in the attribute it is written into', () => {
        const written: readonly WrittenNode[] = [
            { element: 'a', address: 'https://example.invalid/?a="b&c', holds: [{ text: 'there' }] },
        ];

        expect(htmlOf(written)).toBe('<a href="https://example.invalid/?a=&quot;b&amp;c">there</a>');
    });

    it('writes a line break as the element that carries no content', () => {
        expect(htmlOf([{ text: 'One' }, { element: 'br', address: null, holds: [] }, { text: 'Two' }])).toBe(
            'One<br />Two',
        );
    });

    it('writes a run’s colour and size as an inline style from the closed values', () => {
        const written: readonly WrittenNode[] = [
            { element: 'span', address: null, colour: '#175cd3', size: 'larger', holds: [{ text: 'Note' }] },
        ];

        expect(htmlOf(written)).toBe('<span style="color:#175cd3;font-size:24px">Note</span>');
    });

    it.each([
        ['small', '12px'],
        ['large', '18px'],
        ['larger', '24px'],
        ['largest', '32px'],
    ] as const)('writes the %s step as %s', (size, length) => {
        expect(htmlOf([{ element: 'span', address: null, size, holds: [{ text: 'a' }] }])).toBe(
            `<span style="font-size:${length}">a</span>`,
        );
    });

    it.each([
        ['start', 'left'],
        ['center', 'center'],
        ['end', 'right'],
        ['justify', 'justify'],
    ] as const)('writes the %s alignment as %s, the composer writing no direction', (alignment, written) => {
        expect(htmlOf([{ element: 'p', address: null, alignment, holds: [{ text: 'a' }] }])).toBe(
            `<p style="text-align:${written}">a</p>`,
        );
    });

    it('writes no colour that is not one, whatever the tree was handed', () => {
        expect(
            htmlOf([{ element: 'span', address: null, colour: 'red;background:url(x)', holds: [{ text: 'a' }] }]),
        ).toBe('<span>a</span>');
    });
});

describe('plainTextOf', () => {
    it('reads each line an engine wrote as its own line', () => {
        const written = writtenIn(region(element('div', 'One'), element('div', 'Two')));

        expect(plainTextOf(written)).toBe('One\nTwo');
    });

    it('reads a paragraph as a break in the reading rather than the next line', () => {
        const written = writtenIn(region(element('p', 'One'), element('p', 'Two')));

        expect(plainTextOf(written)).toBe('One\n\nTwo');
    });

    it('marks a bulleted list so it still reads as one with no markup to draw it', () => {
        const written = writtenIn(region(element('ul', element('li', 'One'), element('li', 'Two'))));

        expect(plainTextOf(written)).toBe('- One\n- Two');
    });

    it('numbers a numbered list from where it starts', () => {
        const written = writtenIn(region(element('ol', element('li', 'First'), element('li', 'Second'))));

        expect(plainTextOf(written)).toBe('1. First\n2. Second');
    });

    it('quotes what was quoted, as mail has always written a quotation', () => {
        const written = writtenIn(
            region(element('blockquote', element('div', 'You wrote'), element('div', 'this')), 'Agreed.'),
        );

        expect(plainTextOf(written)).toBe('> You wrote\n> this\nAgreed.');
    });

    it('reads emphasis as the words it emphasized, there being no plain-text spelling of it', () => {
        const written = writtenIn(region('Read the ', element('b', 'third'), ' column.'));

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

        expect(writtenTextIn(JSON.parse(JSON.stringify(written)))).toEqual(written);
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

        expect(writtenTextIn(JSON.parse(JSON.stringify(written)))).toEqual(written);
    });

    it.each([
        ['a colour that is not #rrggbb', [{ element: 'span', address: null, colour: 'red', holds: [] }]],
        ['a colour in a spelling this client does not write', [{ element: 'span', address: null, colour: '#B42318', holds: [] }]],
        ['a step off the scale', [{ element: 'span', address: null, size: 'huge', holds: [] }]],
        ['normal written as a step', [{ element: 'span', address: null, size: 'normal', holds: [] }]],
        ['an alignment outside the four', [{ element: 'p', address: null, alignment: 'left', holds: [] }]],
        ['a colour on something that is not a run', [{ element: 'p', address: null, colour: '#b42318', holds: [] }]],
        ['a size on something that is not a run', [{ element: 'b', address: null, size: 'large', holds: [] }]],
        ['an alignment on a run', [{ element: 'span', address: null, size: 'large', alignment: 'center', holds: [] }]],
        ['an alignment on a block that carries none', [{ element: 'blockquote', address: null, alignment: 'end', holds: [] }]],
        ['a run carrying neither a colour nor a size', [{ element: 'span', address: null, holds: [] }]],
    ])('refuses %s', (_, kept) => {
        expect(writtenTextIn(kept)).toBeNull();
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
        expect(writtenTextIn(kept)).toBeNull();
    });

    it('refuses a message nested deeper than anything the composer writes', () => {
        let deepest: WrittenNode = { text: 'here' };

        for (let depth = 0; depth < 200; depth += 1) {
            deepest = { element: 'div', address: null, holds: [deepest] };
        }

        expect(writtenTextIn([deepest])).toBeNull();
    });
});
