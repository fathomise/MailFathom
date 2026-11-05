// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { colourOfRgb, type RunDrawing, type WrittenSize } from './writtenText';

/** What the region's own style answers at the moment it is read: its colour, its size, and the value of a token. */
export interface DrawnRegion {
    readonly colour: string;
    readonly fontSize: string;
    readonly token: (name: string) => string;
}

/** A swatch as the region draws it: the value a message is sent with, and the token it is drawn through. */
export interface DrawnSwatch {
    readonly value: string;
    readonly token: string;
}

/** A step of the size scale as the region draws it, normal being the step with no token. */
export interface DrawnStep {
    readonly size: WrittenSize | null;
    readonly token: string | null;
}

// A step is matched to the nearest hundredth of an em, which is rounding rather than approximation, since each length
// an engine resolved came from exactly one of these tokens.
const stepTolerance = 0.005;

/**
 * How the region draws a run, read off the token layer rather than restated, so a theme switched since the last
 * reading is the theme read: each swatch's token as the theme draws it, and each step's token against the size the
 * region draws its own words at. The region's own colour is no colour of a run's, which is what an engine wrote where
 * it carried the text around a run over; any other colour no swatch is drawn in is its own value, which is how a
 * reopened draft's colour is drawn.
 */
export function drawingOf(
    region: DrawnRegion,
    swatches: readonly DrawnSwatch[],
    steps: readonly DrawnStep[],
): RunDrawing {
    const regionColour = colourOfRgb(region.colour);
    const regionSize = parseFloat(region.fontSize);
    const drawnSwatches = new Map(swatches.map((swatch) => [colourOfRgb(region.token(swatch.token)), swatch.value]));

    return {
        colourDrawnAs: (drawn) => {
            const colour = colourOfRgb(drawn);

            if (colour === null || colour === regionColour) {
                return null;
            }

            return drawnSwatches.get(colour) ?? colour;
        },
        sizeDrawnAs: (drawn) => {
            const ems = drawn.endsWith('em') ? parseFloat(drawn) : parseFloat(drawn) / regionSize;

            return (
                steps.find(
                    (step) =>
                        step.token !== null && Math.abs(parseFloat(region.token(step.token)) - ems) < stepTolerance,
                )?.size ?? null
            );
        },
    };
}
