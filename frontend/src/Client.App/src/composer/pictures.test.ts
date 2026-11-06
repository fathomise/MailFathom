// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    base64Of,
    drawnSizeOf,
    isPictureName,
    mostPictureOctets,
    mostPictures,
    octetsOf,
    pictureIn,
    pictureTypeOf,
    refusalOfAdding,
} from './pictures';

// Headers of each kind stating a size, and nothing past what the size is read from.
const png = new Uint8Array([
    0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0x03, 0x20, 0, 0, 0x02,
    0x58,
]);
const gif = new Uint8Array([0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x40, 0x01, 0xf0, 0x00]);

// A JPEG whose frame header comes after an application segment, which is where a camera or an editor puts one.
const jpeg = new Uint8Array([
    0xff, 0xd8, 0xff, 0xe0, 0x00, 0x04, 0x4a, 0x46, 0xff, 0xc0, 0x00, 0x11, 0x08, 0x01, 0xe0, 0x02, 0x80, 0x03,
]);

function held(octets: number) {
    return { octets: new Uint8Array(octets) };
}

describe('pictureTypeOf', () => {
    it.each([
        ['a PNG', png, 'image/png'],
        ['a GIF', gif, 'image/gif'],
        ['a JPEG', jpeg, 'image/jpeg'],
        ['text named like a picture', new TextEncoder().encode('not a picture at all'), null],
        ['a WebP, which not every mail client draws', new TextEncoder().encode('RIFF\0\0\0\0WEBPVP8 '), null],
    ])('reads %s by its signature', (_, octets, expected) => {
        expect(pictureTypeOf(octets)).toBe(expected);
    });
});

describe('pictureIn', () => {
    it.each([
        ['a PNG', png, { type: 'image/png', width: 800, height: 600 }],
        ['a GIF', gif, { type: 'image/gif', width: 320, height: 240 }],
        ['a JPEG', jpeg, { type: 'image/jpeg', width: 640, height: 480 }],
    ])('reads the size %s states in its header', (_, octets, expected) => {
        expect(pictureIn(octets)).toEqual(expected);
    });

    it.each([
        ['a PNG cut before its header chunk', png.subarray(0, 16)],
        ['a PNG whose first chunk is not the header', Uint8Array.from(png, (octet, at) => (at === 12 ? 0x41 : octet))],
        ['a PNG stating a side of nothing', Uint8Array.from(png, (octet, at) => (at >= 16 && at < 20 ? 0 : octet))],
        ['a JPEG whose image data begins before any frame', new Uint8Array([0xff, 0xd8, 0xff, 0xda, 0x00, 0x02, 0x00])],
        ['text', new TextEncoder().encode('not a picture at all')],
    ])('says nothing for %s', (_, octets) => {
        expect(pictureIn(octets)).toBeNull();
    });
});

describe('refusalOfAdding', () => {
    it('takes a picture within every bound', () => {
        expect(refusalOfAdding(1_000, [held(1_000)])).toBeNull();
    });

    it('refuses one picture past the most one may hold', () => {
        expect(refusalOfAdding(mostPictureOctets + 1, [])).toEqual({
            reason: 'tooLarge',
            octets: mostPictureOctets + 1,
        });
    });

    it('refuses a picture past the most a composition holds', () => {
        expect(
            refusalOfAdding(
                1,
                Array.from({ length: mostPictures }, () => held(1)),
            ),
        ).toEqual({ reason: 'tooMany' });
    });

    it('refuses a picture that would take the pictures together past their bound, naming what they would hold', () => {
        expect(refusalOfAdding(mostPictureOctets, [held(mostPictureOctets / 2), held(1)])).toEqual({
            reason: 'tooMuchTogether',
            octets: mostPictureOctets * 1.5 + 1,
        });
    });

    it('names the picture too large before anything else, in the order the design says them', () => {
        expect(
            refusalOfAdding(
                mostPictureOctets + 1,
                Array.from({ length: mostPictures }, () => held(1)),
            ),
        ).toMatchObject({
            reason: 'tooLarge',
        });
    });
});

describe('drawnSizeOf', () => {
    const own = { width: 800, height: 600 };

    it('draws a picture at a narrower width it was given, its height scaled to its own proportions', () => {
        expect(drawnSizeOf(own, 400)).toEqual({ width: 400, height: 300 });
        expect(drawnSizeOf(own, 1)).toEqual({ width: 1, height: 1 });
    });

    it.each([
        ['a width past its own', 1200],
        ['a width that is no side of a picture', 12.5],
        ['no width at all', null],
    ])('draws a picture at its own size for %s', (_, width) => {
        expect(drawnSizeOf(own, width)).toEqual(own);
    });
});

describe('isPictureName', () => {
    it.each([
        ['a version 7 UUID', '01890a5d-ac96-774b-bcce-b302099a8057', true],
        ['a version 4 UUID', '01890a5d-ac96-474b-bcce-b302099a8057', false],
        ['an upper-case one', '01890A5D-AC96-774B-BCCE-B302099A8057', false],
        ['a file name', 'chart.png', false],
    ])('reads %s', (_, name, expected) => {
        expect(isPictureName(name)).toBe(expected);
    });
});

describe('base64Of', () => {
    it('round-trips a picture larger than one slice of the encoding', () => {
        const octets = Uint8Array.from({ length: 0x8000 * 2 + 7 }, (_, at) => at % 256);

        expect(octetsOf(base64Of(octets))).toEqual(octets);
    });

    it('answers nothing for text that is not base64', () => {
        expect(octetsOf('not base64 at all!')).toBeNull();
    });
});
