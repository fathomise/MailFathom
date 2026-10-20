// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import {
    useCoarsePointer,
    useDesktopComposition,
    useLowWindow,
    useTallWindow,
    useTwoPanes,
    useWideWorkspace,
} from './useWideWorkspace';

// What the pointer can do is the half of a composition a stylesheet cannot answer, because it decides whether a
// listener is bound at all. It is stated here the way `theme/Theme.test.tsx` states a machine's appearance: a query
// this suite defines over the one the setup file leaves answering nothing.

const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

let listeners: (() => void)[] = [];
let pointerIsCoarse = false;

function theDeviceIsPickedUp(coarse: boolean): void {
    pointerIsCoarse = coarse;
    act(() => {
        for (const listener of listeners) {
            listener();
        }
    });
}

beforeEach(() => {
    listeners = [];
    pointerIsCoarse = false;

    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: pointerIsCoarse && query.includes('coarse'),
            addEventListener: (_: string, listener: () => void) => {
                listeners.push(listener);
            },
            removeEventListener: (_: string, listener: () => void) => {
                listeners = listeners.filter((listening) => listening !== listener);
            },
        }),
    });
});

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

describe('useCoarsePointer', () => {
    it('reads a pointer a mouse drives as fine, which is what leaves a gesture unbound', () => {
        const { result } = renderHook(() => useCoarsePointer());

        expect(result.current).toBe(false);
    });

    it('reads a pointer a finger drives as coarse', () => {
        pointerIsCoarse = true;

        const { result } = renderHook(() => useCoarsePointer());

        expect(result.current).toBe(true);
    });

    it('keeps the answer current as a device is picked up, rather than answering once at the first render', () => {
        const { result } = renderHook(() => useCoarsePointer());

        theDeviceIsPickedUp(true);

        expect(result.current).toBe(true);
    });
});

// The three widths themselves, at the four sizes the design frames a composition at. jsdom lays nothing out
// and declares no custom property, so each query is answered from the width alone and the hooks fall back to the
// stylesheet's own numbers — which is exactly what is being asserted, those numbers being the boundaries.
function atWidth(pixels: number): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => {
            const named = /([\d.]+)rem/.exec(query)?.[1];

            return {
                media: query,
                matches: named !== undefined && pixels >= Number(named) * 16,
                addEventListener: () => undefined,
                removeEventListener: () => undefined,
            };
        },
    });
}

describe('the widths a composition changes at', () => {
    it.each([
        ['the phone', 390, false, false, false],
        ['the fold', 884, true, true, false],
        ['the tablet', 1024, true, true, false],
        ['the desktop', 1440, true, true, true],
    ])('composes %s as the design frames it', (_, pixels, workspace, panes, desktop) => {
        atWidth(pixels);

        const composition = renderHook(() => ({
            workspace: useWideWorkspace(),
            panes: useTwoPanes(),
            desktop: useDesktopComposition(),
        }));

        expect(composition.result.current).toEqual({ workspace, panes, desktop });
    });
});

// The two heights, asked of a window whose width is held at the desktop's so nothing but the height moves. The query
// answered here is the one the hooks build: a range over `height`, never `orientation` or `aspect-ratio`.
function atHeight(pixels: number): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => {
            const [, comparison, named] = /\(height (<|>=) ([\d.]+)rem\)/.exec(query) ?? [];
            const boundary = Number(named) * 16;

            return {
                media: query,
                matches: comparison === '<' ? pixels < boundary : comparison === '>=' && pixels >= boundary,
                addEventListener: () => undefined,
                removeEventListener: () => undefined,
            };
        },
    });
}

describe('the heights a composition changes at', () => {
    it.each([
        ['a phone on its side', 390, true, false],
        ['the last low window', 479, true, false],
        ['the first regular one', 480, false, false],
        ['the desktop the design frames', 900, false, false],
        ['the last regular one', 959, false, false],
        ['the first tall one', 960, false, true],
        ['an upright tablet', 1366, false, true],
    ])('reads %s as the design bands it', (_, pixels, low, tall) => {
        atHeight(pixels);

        const band = renderHook(() => ({ low: useLowWindow(), tall: useTallWindow() }));

        expect(band.result.current).toEqual({ low, tall });
    });
});
