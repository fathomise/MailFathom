// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { drawingOf, type DrawnRegion } from './regionDrawing';
import { writtenIn } from './writtenText';

const swatches = [
    { value: '#b42318', token: '--ink-red' },
    { value: '#175cd3', token: '--ink-blue' },
];

const steps = [
    { size: 'small', token: '--step-small' },
    { size: null, token: null },
    { size: 'large', token: '--step-large' },
] as const;

const darkTokens = new Map([
    ['--ink-red', ' #f97066'],
    ['--ink-blue', '#84adff'],
    ['--step-small', '0.867em'],
    ['--step-large', '1.2em'],
]);

// The region in the dark theme: each swatch drawn in its lighter twin, and its words in a colour of its own.
const darkRegion: DrawnRegion = {
    colour: 'rgb(230, 232, 236)',
    fontSize: '15px',
    token: (name) => darkTokens.get(name) ?? '',
};

function spanStyled(style: string): HTMLElement {
    const region = document.createElement('div');
    const span = document.createElement('span');

    span.setAttribute('style', style);
    span.append('moved');
    region.append(span);

    return region;
}

describe('drawingOf', () => {
    it('reads a swatch drawn in its dark twin as the swatch the message is sent with', () => {
        expect(drawingOf(darkRegion, swatches, steps).colourDrawnAs('rgb(249, 112, 102)')).toBe('#b42318');
    });

    it('reads the region’s own colour as no colour, however it is spelled', () => {
        const drawing = drawingOf(darkRegion, swatches, steps);

        expect(drawing.colourDrawnAs('rgb(230, 232, 236)')).toBeNull();
        expect(drawing.colourDrawnAs('#e6e8ec')).toBeNull();
    });

    it('reads any other colour as itself, which is how a reopened draft’s colour is drawn', () => {
        expect(drawingOf(darkRegion, swatches, steps).colourDrawnAs('rgb(18, 52, 86)')).toBe('#123456');
    });

    it.each([
        ['18px', 'large'],
        ['13.005px', 'small'],
        ['1.2em', 'large'],
        ['15px', null],
        ['21px', null],
    ])('reads a run drawn at %s as the %s step', (drawn, step) => {
        expect(drawingOf(darkRegion, swatches, steps).sizeDrawnAs(drawn)).toBe(step);
    });

    it('lets the reading drop a moved run drawn in the region’s own colour and at its own size', () => {
        const drawing = drawingOf(darkRegion, swatches, steps);

        expect(writtenIn(spanStyled('color: rgb(230, 232, 236); font-size: 15px'), drawing)).toEqual([
            { text: 'moved' },
        ]);
        expect(writtenIn(spanStyled('color: rgb(249, 112, 102); font-size: 18px'), drawing)).toEqual([
            { element: 'span', address: null, colour: '#b42318', size: 'large', holds: [{ text: 'moved' }] },
        ]);
    });
});
