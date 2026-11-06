// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The colours the composer's picker writes, under ADR 0047's one rule for them: neither too dark nor too light, so a
// coloured run reads on the light theme's panel and on the dark theme's alike and is drawn as the value it is sent
// with in both. "Reads" is at least 3:1 against each panel, which puts the colour's relative luminance between the two
// bounds below — the darker one 3:1 against the dark panel, `oklch(0.246 0.016 262)`, and the lighter one 3:1 against
// the light panel, `oklch(0.996 0.002 262)`. The palette is that band sampled, and a colour of somebody's own is
// brought into it.

import type { MessageKey } from '../localization/en';
import type { Translate } from '../localization/useLocalization';

/** The darkest relative luminance the picker writes: 3:1 against the dark theme's panel. */
export const darkestReadable = 0.1447;

/** The lightest relative luminance the picker writes: 3:1 against the light theme's panel. */
export const lightestReadable = 0.296;

/** The five shades each hue is offered in, in the order the grid's rows draw them. */
export const textColourShades = ['deep', 'vivid', 'light', 'muted', 'soft'] as const;

export type TextColourShade = (typeof textColourShades)[number];

/** One column of the grid: a hue, what it is called, and its value in each shade, in `textColourShades`' order. */
export interface TextColourHue {
    readonly hue: string;
    readonly named: MessageKey;
    readonly shades: readonly [string, string, string, string, string];
}

/** The grid's columns: grey and ten hues, each sampled across the readable band at full and at half colourfulness. */
export const textColourHues: readonly TextColourHue[] = [
    { hue: 'grey', named: 'compose.colourGrey', shades: ['#6c6c6c', '#818181', '#939393', '#797979', '#8c8c8c'] },
    { hue: 'red', named: 'compose.colourRed', shades: ['#db0118', '#f73030', '#f86056', '#b95e55', '#ce7369'] },
    { hue: 'orange', named: 'compose.colourOrange', shades: ['#b05100', '#ca6520', '#e47325', '#9f6d51', '#b97f5f'] },
    { hue: 'amber', named: 'compose.colourAmber', shades: ['#8e6503', '#a67a21', '#bc8a26', '#8c7651', '#a2895e'] },
    { hue: 'lime', named: 'compose.colourLime', shades: ['#597604', '#6c8c20', '#7b9f25', '#6e7f50', '#7f935d'] },
    { hue: 'green', named: 'compose.colourGreen', shades: ['#017d38', '#219449', '#29a855', '#54835e', '#62986e'] },
    { hue: 'teal', named: 'compose.colourTeal', shades: ['#097a70', '#229085', '#28a397', '#54817b', '#63958e'] },
    { hue: 'cyan', named: 'compose.colourCyan', shades: ['#077690', '#238ca8', '#299fbe', '#557f8d', '#6393a3'] },
    { hue: 'blue', named: 'compose.colourBlue', shades: ['#035dff', '#3e7cf5', '#5b91f6', '#5378bd', '#6d8dc7'] },
    { hue: 'purple', named: 'compose.colourPurple', shades: ['#9625ff', '#9f5ef5', '#ab79f6', '#8968bf', '#9a7fc8'] },
    { hue: 'pink', named: 'compose.colourPink', shades: ['#cf0388', '#ec2a9f', '#f852ae', '#b35c88', '#ce6c9d'] },
];

/** How a shade's name is said around its hue's: *Red, dark*, and the vivid one by the hue's name alone. */
export const shadeNames: Readonly<Record<TextColourShade, MessageKey>> = {
    deep: 'compose.colourShadeDeep',
    vivid: 'compose.colourShadeVivid',
    light: 'compose.colourShadeLight',
    muted: 'compose.colourShadeMuted',
    soft: 'compose.colourShadeSoft',
};

/**
 * What a colour is called: its hue and shade where the grid offers it — *Blue*, *Blue, dark* — and its own value as a
 * colour of somebody's own where it does not, so a control holding it and a disc drawing it say the same.
 */
export function nameOfColour(value: string, translate: Translate): string {
    for (const hue of textColourHues) {
        const shade = hue.shades.indexOf(value.toLowerCase());

        if (shade >= 0) {
            return translate(shadeNames[textColourShades[shade] ?? 'vivid'], { colour: translate(hue.named) });
        }
    }

    return translate('compose.colourOwn', { value: value.toLowerCase() });
}

/** How many colours of somebody's own the picker keeps beside the grid while the composer is open. */
export const ownColoursKept = 11;

/** A colour of somebody's own, as the custom picker holds it: a hue in degrees, and saturation and shade from 0 to 1. */
export interface OwnColour {
    readonly hue: number;
    readonly saturation: number;

    /** Where in the readable band it stands, 0 being the darkest the picker writes and 1 the lightest. */
    readonly shade: number;
}

/** The `#rrggbb` the custom picker writes for a hue, a saturation, and a place in the readable band. */
export function colourAt({ hue, saturation, shade }: OwnColour): string {
    const target = darkestReadable + (lightestReadable - darkestReadable) * clamped(shade);
    let darker = 0;
    let lighter = 1;

    // Luminance only grows with lightness for a fixed hue and saturation, so the lightness that meets the target is
    // found by halving; twenty halvings is finer than one step of an 8-bit channel.
    for (let halving = 0; halving < 20; halving += 1) {
        const lightness = (darker + lighter) / 2;

        if (luminanceOf(hslChannels(hue, clamped(saturation), lightness)) < target) {
            darker = lightness;
        } else {
            lighter = lightness;
        }
    }

    return hexOf(hslChannels(hue, clamped(saturation), (darker + lighter) / 2));
}

/** Where a `#rrggbb` stands for the custom picker, its shade read off the band and pinned to it where it falls outside. */
export function ownColourOf(value: string): OwnColour | null {
    const channels = channelsOf(value);

    if (channels === null) {
        return null;
    }

    const [red, green, blue] = channels;
    const highest = Math.max(red, green, blue);
    const lowest = Math.min(red, green, blue);
    const range = highest - lowest;
    const lightness = (highest + lowest) / 2;
    const saturation = range === 0 ? 0 : range / (1 - Math.abs(2 * lightness - 1));
    const hue = range === 0 ? 0 : hueOf(red, green, blue, highest, range);
    const shade = (luminanceOf(channels) - darkestReadable) / (lightestReadable - darkestReadable);

    return { hue, saturation: clamped(saturation), shade: clamped(shade) };
}

/** Whether a `#rrggbb` reads on both themes' panels, which is what every colour the picker writes does. */
export function readsOnBothThemes(value: string): boolean {
    const channels = channelsOf(value);
    const luminance = channels === null ? Number.NaN : luminanceOf(channels);

    return luminance >= darkestReadable && luminance <= lightestReadable;
}

/**
 * The colour the picker writes for a `#rrggbb` somebody typed: itself where it already reads on both themes, and
 * otherwise the same hue and saturation moved to the nearer edge of the band. Null for anything that is not a colour.
 */
export function readableColourNear(value: string): string | null {
    const normalized = value.trim().toLowerCase();
    const own = ownColourOf(normalized);

    if (own === null) {
        return null;
    }

    return readsOnBothThemes(normalized) ? normalized : colourAt(own);
}

function hueOf(red: number, green: number, blue: number, highest: number, range: number): number {
    const sextant =
        highest === red ? ((green - blue) / range + 6) % 6 : highest === green ? (blue - red) / range + 2 : (red - green) / range + 4;

    return sextant * 60;
}

function hslChannels(hue: number, saturation: number, lightness: number): [number, number, number] {
    const chroma = (1 - Math.abs(2 * lightness - 1)) * saturation;
    const at = (offset: number): number => {
        const position = (offset + hue / 30) % 12;

        return lightness - (chroma / 2) * Math.max(-1, Math.min(position - 3, 9 - position, 1));
    };

    return [at(0), at(8), at(4)];
}

function luminanceOf([red, green, blue]: readonly [number, number, number]): number {
    const linear = (channel: number): number =>
        channel <= 0.04045 ? channel / 12.92 : ((channel + 0.055) / 1.055) ** 2.4;

    return 0.2126 * linear(red) + 0.7152 * linear(green) + 0.0722 * linear(blue);
}

function channelsOf(value: string): [number, number, number] | null {
    const match = /^#([0-9a-f]{2})([0-9a-f]{2})([0-9a-f]{2})$/iu.exec(value.trim());

    return match === null ? null : [byte(match[1]), byte(match[2]), byte(match[3])];
}

function byte(hexadecimal: string | undefined): number {
    return parseInt(hexadecimal ?? '0', 16) / 255;
}

function hexOf(channels: readonly [number, number, number]): string {
    return `#${channels.map((channel) => Math.round(clamped(channel) * 255).toString(16).padStart(2, '0')).join('')}`;
}

function clamped(value: number): number {
    return Math.min(1, Math.max(0, value));
}
