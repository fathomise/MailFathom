// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useMemo, useSyncExternalStore } from 'react';

// The widths and heights a component has to ask about rather than compose against. A stylesheet answers a size question
// for anything CSS can lay out two ways from one tree, and that is almost everything; what it cannot do is decide
// *which* of two screens is in the document, or whether a control is one a person may operate at all.
//
// Every query is built from the tokens in `styles.css` rather than from a second copy of the number, so each
// breakpoint stays one decision. Width and height are separate axes and no query here reads both, or the window's
// orientation or aspect: upright and on its side are each one width band beside one height band.

function windowSize(
    token: string,
    fallback: string,
    condition: (size: string) => string,
    unanswered: boolean,
    view: Window = window,
) {
    function query(): MediaQueryList | null {
        if (typeof view.matchMedia !== 'function') {
            return null;
        }

        const size = getComputedStyle(document.documentElement).getPropertyValue(token).trim();

        return view.matchMedia(condition(size === '' ? fallback : size));
    }

    return {
        /** Whether the window is in this band. A runtime that cannot answer is read as wide and as neither low nor tall. */
        matches: (): boolean => query()?.matches ?? unanswered,

        watch: (changed: () => void): (() => void) => {
            const watched = query();

            watched?.addEventListener('change', changed);

            return () => {
                watched?.removeEventListener('change', changed);
            };
        },
    };
}

// What the pointer can do, which is the third axis a screen adapts to and the only one that is not a size. It is asked
// here beside the other two because a component asking any of them asks it the same way, and because
// what it answers decides which gestures exist rather than how something is laid out — a query CSS cannot answer for
// a listener that has to be bound or not bound at all.
function pointerIsCoarse() {
    function query(): MediaQueryList | null {
        return typeof window.matchMedia === 'function' ? window.matchMedia('(pointer: coarse)') : null;
    }

    return {
        /** Whether the pointer is one a finger drives. A runtime that cannot answer is read as fine. */
        matches: (): boolean => query()?.matches ?? false,

        watch: (changed: () => void): (() => void) => {
            const watched = query();

            watched?.addEventListener('change', changed);

            return () => {
                watched?.removeEventListener('change', changed);
            };
        },
    };
}

// Built once each, because `useSyncExternalStore` compares the two functions it is handed by identity and a pair
// rebuilt per render would resubscribe on every one of them.
const atLeastWide = (width: string) => `(min-width: ${width})`;
const workspaceWidth = windowSize('--breakpoint-workspace', '43.75rem', atLeastWide, true);
const paneWidth = windowSize('--breakpoint-panes', '51.25rem', atLeastWide, true);
const desktopBandIn = (view: Window) => windowSize('--breakpoint-desktop', '73.75rem', atLeastWide, true, view);
const desktopWidth = desktopBandIn(window);
const roomyWidth = windowSize('--breakpoint-roomy', '63.75rem', atLeastWide, true);
const lowHeight = windowSize('--window-height-low', '30rem', (height) => `(height < ${height})`, false);
const tallHeight = windowSize('--window-height-tall', '60rem', (height) => `(height >= ${height})`, false);
const coarsePointer = pointerIsCoarse();

/**
 * Whether the window is at or above the width the phone shape stops at, kept current as it is resized across that
 * breakpoint.
 *
 * What needs asking rather than composing is where a surface stands at all: bottom navigation is a row of five places
 * and a rail is a column of nine, and a sheet rising from the foot of the window is a different element from a panel
 * beside the rail.
 */
export function useWideWorkspace(): boolean {
    return useSyncExternalStore(workspaceWidth.watch, workspaceWidth.matches);
}

/**
 * Whether the window has room for the list and the message side by side, kept current as it is resized across that
 * breakpoint.
 *
 * The single-pane Mail space is what needs asking: it draws the list or the message and never both, so a row that is
 * not on the screen is not in the document either — and going between them is navigation the back gesture returns
 * through rather than a layout that changed.
 */
export function useTwoPanes(): boolean {
    return useSyncExternalStore(paneWidth.watch, paneWidth.matches);
}

/**
 * Whether the window is at or above the width where the tablet composition stops being cramped.
 *
 * Below it, and with two panes, is the fold: the one composition where the list beside an open message leaves the
 * message too little to read, so Mail folds the list to a strip of senders while something is open.
 */
export function useRoomyComposition(): boolean {
    return useSyncExternalStore(roomyWidth.watch, roomyWidth.matches);
}

/**
 * Whether the window is in the desktop composition rather than the tablet one.
 *
 * It is the widest of the three, and the design draws three things at it together: the mailbox column stands
 * beside the list rather than in a drawer over it, the toolbar's controls carry their labels, and the tab mode is
 * worth offering at all — a row of tabs above the columns needs room a rail beside two of them does not. Below it the
 * tab switch is inert rather than absent: a control that vanished by width alone would leave somebody who had turned
 * it on with no way to reach it, and the row says why instead.
 */
export function useDesktopComposition(): boolean {
    return useSyncExternalStore(desktopWidth.watch, desktopWidth.matches);
}

/** Whether a window the client draws into beside its own is in the desktop composition, measured in that window. */
export function useDesktopCompositionIn(view: Window): boolean {
    const band = useMemo(() => desktopBandIn(view), [view]);

    return useSyncExternalStore(band.watch, band.matches);
}

/**
 * Whether the window is in the low height band, kept current as it is resized across that breakpoint.
 *
 * It is the band a phone on its side is in, where the design folds what is vertical — the rail's labels, a message's
 * head — so nothing needed is pushed off the bottom. A stylesheet asks it as `low:`; this is for what decides a tree.
 */
export function useLowWindow(): boolean {
    return useSyncExternalStore(lowHeight.watch, lowHeight.matches);
}

/**
 * Whether the window is in the tall height band, kept current as it is resized across that breakpoint.
 *
 * It is the band an upright tablet, half a desktop screen, and a monitor on its side are in, where the extra height
 * goes to lists and threads and what stands over them stops growing. A stylesheet asks it as `tall:`.
 */
export function useTallWindow(): boolean {
    return useSyncExternalStore(tallHeight.watch, tallHeight.matches);
}

/**
 * Whether the pointer driving this client is one a finger drives, kept current as a device is picked up or put down.
 *
 * What needs asking is a gesture rather than a size: a drag that follows a finger one to one is bound or not bound at
 * all, and a stylesheet cannot decide that. Everything a query can decide — a target big enough to hit, an affordance
 * that would otherwise exist only under a hover — stays in the stylesheet as `pointer-coarse:`.
 */
export function useCoarsePointer(): boolean {
    return useSyncExternalStore(coarsePointer.watch, coarsePointer.matches);
}
