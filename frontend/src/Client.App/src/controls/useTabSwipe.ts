// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, type PointerEvent } from 'react';
import { swipeSoFar } from './swipeAcross';

// A finger swiped across what a strip of tabs or a segmented control shows steps to the tab beside it: left opens the
// one to the right, right the one before, and past either end nothing happens rather than the strip wrapping round. It
// is bound to the surface under the tabs rather than to the tabs, because the tabs are where a finger has to reach up
// to, and the surface is where it already is.
//
// The numbers are `swipeAcross.ts`'s, so a scroll of the list underneath is never a tab change and the distance is the
// one a row and a toast already taught. It acts the moment the distance is crossed, as a toast does, because nothing
// was carried that would have to spring back. A mouse drags nothing: the tabs are what a pointer that can aim already
// has, and a selection dragged across a section would otherwise switch it.

/** What the surface under the tabs binds, beside the `touch-pan-y` that leaves the sideways travel to it. */
export interface TabSwipe {
    readonly onPointerDown: (event: PointerEvent) => void;
    readonly onPointerMove: (event: PointerEvent) => void;
    readonly onPointerUp: (event: PointerEvent) => void;
    readonly onPointerCancel: (event: PointerEvent) => void;
}

/**
 * The gesture, over one ordered set of tabs.
 *
 * @param tabs Every tab in the order the strip draws them, which is the order a swipe walks.
 * @param current The tab open now.
 * @param onChoose Said with the tab a swipe reached, and not at all for a swipe past either end.
 */
export function useTabSwipe<Tab>(tabs: readonly Tab[], current: Tab, onChoose: (tab: Tab) => void): TabSwipe {
    // A ref rather than state: nothing on the screen is drawn from where the finger landed.
    const from = useRef<{ readonly pointer: number; readonly x: number; readonly y: number } | null>(null);

    function end(event: PointerEvent): void {
        if (from.current?.pointer === event.pointerId) {
            from.current = null;
        }
    }

    return {
        onPointerDown: (event) => {
            from.current =
                event.pointerType === 'mouse' ? null : { pointer: event.pointerId, x: event.clientX, y: event.clientY };
        },
        onPointerMove: (event) => {
            const swipe = from.current;

            if (swipe?.pointer !== event.pointerId) {
                return;
            }

            const across = event.clientX - swipe.x;
            const verdict = swipeSoFar(across, event.clientY - swipe.y);

            if (verdict === 'travelling') {
                return;
            }

            from.current = null;

            const reached = tabs[tabs.indexOf(current) + (across < 0 ? 1 : -1)];

            if (verdict === 'committed' && reached !== undefined) {
                onChoose(reached);
            }
        },
        onPointerUp: end,
        onPointerCancel: end,
    };
}
