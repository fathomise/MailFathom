// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// How tall a growing field stands for what is written in it, as the design draws both drafting fields: it grows a line
// at a time up to six, and past that it stops and scrolls.

const mostLinesShown = 6;

/** What a field's height is worked out from, in CSS pixels: what is written, one line of it, and its padding and border. */
export interface FieldMeasure {
    readonly written: number;
    readonly line: number;
    readonly frame: number;
}

/** The height a field takes for what is written, and whether it scrolls: it grows with the text and stops at six lines. */
export function heightFor({ written, line, frame }: FieldMeasure): {
    readonly height: number;
    readonly scrolls: boolean;
} {
    const tallest = line * mostLinesShown + frame;

    return { height: Math.min(written, tallest), scrolls: written > tallest };
}
