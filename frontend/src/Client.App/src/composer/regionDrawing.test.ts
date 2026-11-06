// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { drawingOf, type DrawnRegion } from './regionDrawing';
import { writtenIn } from './writtenText';

const steps = [
    { size: 'small', token: '--step-small' },
    { size: null, token: null },
    { size: 'large', token: '--step-large' },
] as const;

const darkTokens = new Map([
    ['--step-small', '0.867em'],
    ['--step-large', '1.2em'],
]);

// The region in the dark theme, its words in a colour of its own.
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
    it('reads the region’s own colour as no colour, however it is spelled', () => {
        const drawing = drawingOf(darkRegion, steps);

        expect(drawing.colourDrawnAs('rgb(230, 232, 236)')).toBeNull();
        expect(drawing.colourDrawnAs('#e6e8ec')).toBeNull();
    });

    it('reads any other colour as itself, since a colour is drawn as the value it is sent with in either theme', () => {
        expect(drawingOf(darkRegion, steps).colourDrawnAs('rgb(18, 52, 86)')).toBe('#123456');
    });

    it.each([
        ['18px', 'large'],
        ['13.005px', 'small'],
        ['1.2em', 'large'],
        ['15px', null],
        ['21px', null],
    ])('reads a run drawn at %s as the %s step', (drawn, step) => {
        expect(drawingOf(darkRegion, steps).sizeDrawnAs(drawn)).toBe(step);
    });

    it('lets the reading drop a moved run drawn in the region’s own colour and at its own size', () => {
        const drawing = drawingOf(darkRegion, steps);

        expect(writtenIn(spanStyled('color: rgb(230, 232, 236); font-size: 15px'), () => null, drawing)).toEqual([
            { text: 'moved' },
        ]);
        expect(writtenIn(spanStyled('color: rgb(62, 124, 245); font-size: 18px'), () => null, drawing)).toEqual([
            { element: 'span', address: null, colour: '#3e7cf5', size: 'large', holds: [{ text: 'moved' }] },
        ]);
    });
});
