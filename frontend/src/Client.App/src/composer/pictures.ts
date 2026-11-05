// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// A picture somebody pasted into what they are writing, as ADR 0047 Question 4 states it: octets the composer holds
// under a name it minted, never an address, and only of the three kinds every mail client draws. The service judges
// each picture again by the same rules, so what is checked here is what lets a person learn of a refusal at the paste
// rather than at the send.
//
// **The kind is read from the octets, never from what the clipboard calls them.** A file named `.png` holding anything
// else is refused here exactly as the deployment would refuse it, and the size it is drawn at is read from the same
// header, so a picture's width and height are facts about its octets rather than something a page could claim.

/** The three kinds of picture the composer takes, as the media type a message names them by. */
export type PictureType = 'image/png' | 'image/jpeg' | 'image/gif';

/** A picture the composer holds, by the name the tree refers to it with. */
export interface HeldPicture {
    readonly octets: Uint8Array;
    readonly type: PictureType;
    readonly width: number;
    readonly height: number;

    /** What it was called where it came from, which is how the composer names it to the person, or `null` for nothing. */
    readonly label: string | null;
}

/** The most one picture may hold, which is the most a reopened draft draws inline. */
export const mostPictureOctets = 2 * 1024 * 1024;

/** The most one composition's pictures may hold together. */
export const mostPicturesOctets = 2.5 * 1024 * 1024;

/** The most pictures one composition may hold. */
export const mostPictures = 16;

/** The widest a picture is written in the message that goes out, which Outlook honours where it ignores a style. */
export const widestSentPicture = 600;

// The largest side a picture may state, which is the most a JPEG or a GIF header can and far beyond any screen. A PNG
// may claim more, and one that does is refused rather than kept in a tree a reload would then refuse whole.
const largestPictureSide = 65_535;

/** Whether a number is a side a picture may be drawn at: a whole number of pixels, at least one and at most the bound. */
export function isPictureSide(side: unknown): side is number {
    return typeof side === 'number' && Number.isInteger(side) && side > 0 && side <= largestPictureSide;
}

/** Why a picture was not added, with what the sentence saying so needs. */
export type PictureRefusal =
    | { readonly reason: 'notTaken'; readonly kind: string | null }
    | { readonly reason: 'tooLarge'; readonly octets: number }
    | { readonly reason: 'tooMany' }
    | { readonly reason: 'tooMuchTogether'; readonly octets: number };

// A version 7 UUID in the lower case `newIdentifier` writes, which is the only name the composer mints and therefore the
// only one a kept tree may carry. It also satisfies the deployment's own rule for a picture's name.
const pictureNamePattern = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/u;

/** Whether a name is one the composer would have minted for a picture. */
export function isPictureName(name: string): boolean {
    return pictureNamePattern.test(name);
}

/**
 * Which of the three kinds the octets open as, read from their signature alone.
 *
 * Enough of a file to answer is its first eight octets, which is what lets a file too large to take be named by its
 * kind before the rest of it is read.
 */
export function pictureTypeOf(octets: Uint8Array): PictureType | null {
    const opens = (signature: readonly number[]) => signature.every((octet, index) => octets[index] === octet);

    if (opens([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a])) {
        return 'image/png';
    }

    if (opens([0xff, 0xd8, 0xff])) {
        return 'image/jpeg';
    }

    return opens([0x47, 0x49, 0x46, 0x38, 0x37, 0x61]) || opens([0x47, 0x49, 0x46, 0x38, 0x39, 0x61])
        ? 'image/gif'
        : null;
}

/**
 * The kind and the pixel size of a picture, read from its header, or `null` for octets that are not a picture the
 * composer takes — a signature it does not know, or a header too short or too broken to say how large it is.
 *
 * A JPEG's orientation tag is not read, so a photograph its camera stored on its side states the size of the stored
 * frame rather than of the one drawn. A screenshot, which is what is pasted, carries none; reading the tag is the next
 * step where a rotated photograph turns up drawn at the wrong proportions in a client that honours only the attributes.
 */
export function pictureIn(octets: Uint8Array): Omit<HeldPicture, 'octets' | 'label'> | null {
    const type = pictureTypeOf(octets);
    const size = type === null ? null : sizeOf(octets, type);

    return type === null || size === null || !isPictureSide(size.width) || !isPictureSide(size.height)
        ? null
        : { type, ...size };
}

/**
 * Why a picture of this size may not join the ones already held, or `null` where it may.
 *
 * In the order the design says them: one picture too large, then one too many, then too much together.
 *
 * @param octets How large the picture being added is.
 * @param held The pictures the composition already names, which are the only ones the bounds count.
 */
export function refusalOfAdding(
    octets: number,
    held: readonly { readonly octets: Uint8Array }[],
): PictureRefusal | null {
    if (octets > mostPictureOctets) {
        return { reason: 'tooLarge', octets };
    }

    if (held.length >= mostPictures) {
        return { reason: 'tooMany' };
    }

    const together = held.reduce((sum, picture) => sum + picture.octets.length, octets);

    return together > mostPicturesOctets ? { reason: 'tooMuchTogether', octets: together } : null;
}

/** The octets as base64, which is how a save carries a picture and how the session store keeps one. */
export function base64Of(octets: Uint8Array): string {
    // `btoa` reads one octet per character, and a spread of a whole picture into one call would pass the engine's
    // argument limit, so the characters are made a slice at a time.
    let characters = '';

    for (let start = 0; start < octets.length; start += 0x8000) {
        characters += String.fromCharCode(...octets.subarray(start, start + 0x8000));
    }

    return btoa(characters);
}

/** Base64 back into octets, or `null` for text that is not base64. */
export function octetsOf(base64: string): Uint8Array | null {
    let characters: string;

    try {
        characters = atob(base64);
    } catch {
        return null;
    }

    return Uint8Array.from(characters, (character) => character.charCodeAt(0));
}

function sizeOf(octets: Uint8Array, type: PictureType): { readonly width: number; readonly height: number } | null {
    const octet = (at: number) => octets[at] ?? 0;
    const bigEndian = (at: number) => octet(at) * 256 + octet(at + 1);

    switch (type) {
        // The header chunk follows the signature, and names itself, so octets that merely open like a PNG say nothing.
        case 'image/png':
            return octets.length >= 24 && String.fromCharCode(...octets.subarray(12, 16)) === 'IHDR'
                ? { width: bigEndian(16) * 65_536 + bigEndian(18), height: bigEndian(20) * 65_536 + bigEndian(22) }
                : null;

        case 'image/gif':
            return octets.length >= 10 ? { width: octet(6) + octet(7) * 256, height: octet(8) + octet(9) * 256 } : null;

        case 'image/jpeg':
            return jpegSizeOf(octets);
    }
}

// A JPEG states its size in the frame header, which comes after whatever segments a camera or an editor wrote first,
// so the segments are walked by their stated lengths until one of the frame markers is reached.
function jpegSizeOf(octets: Uint8Array): { readonly width: number; readonly height: number } | null {
    const octet = (at: number) => octets[at] ?? 0;
    const bigEndian = (at: number) => octet(at) * 256 + octet(at + 1);
    let at = 2;

    while (at + 3 < octets.length) {
        if (octet(at) !== 0xff) {
            return null;
        }

        const marker = octet(at + 1);

        // A fill octet, and the markers that stand alone with no length after them.
        if (marker === 0xff) {
            at += 1;
            continue;
        }

        if (marker === 0x01 || (marker >= 0xd0 && marker <= 0xd8)) {
            at += 2;
            continue;
        }

        // The image data, or its end, before any frame said how large it is.
        if (marker === 0xd9 || marker === 0xda) {
            return null;
        }

        const isFrame = marker >= 0xc0 && marker <= 0xcf && marker !== 0xc4 && marker !== 0xc8 && marker !== 0xcc;

        if (isFrame) {
            return at + 8 < octets.length ? { width: bigEndian(at + 7), height: bigEndian(at + 5) } : null;
        }

        const length = bigEndian(at + 2);

        if (length < 2) {
            return null;
        }

        at += 2 + length;
    }

    return null;
}
