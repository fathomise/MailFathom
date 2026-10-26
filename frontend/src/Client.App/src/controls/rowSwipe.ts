// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState, type PointerEvent } from 'react';
import { swipeCarry, swipeSoFar } from './swipeAcross';
import { pressedByFinger, type RowPress } from '../contextMenu/rowPress';

// The other thing a finger on a list row can mean: carried aside, leftward or rightward, to ask for whatever the list
// offers that way — a message row answers its message leftward and files it away rightward, and a notification row
// toggles its read state either way. The arithmetic is `swipeAcross.ts`'s, because a toast answers to the same numbers;
// what is here is the part that belongs to a row — which direction a swipe went, and how the gesture shares one finger
// with the press that opens the row's menu.
//
// **One touch, two gestures, and they never both fire.** They begin together and each rules the other out as it takes
// over: the press is already off by the time the swipe engages, because it cancels on a shorter travel than the swipe
// needs; and a press that has opened a menu suppresses everything behind it, which is the same window the tap answers
// to. So the finger is only ever doing one of the three things a row offers.
//
// **It is bound to the pointer rather than to a head**, exactly as the press is: a finger or a pen swipes, a mouse
// never does. Dragging a row with a mouse would act on a slipped button, and every act either direction performs is on
// the row's own menu for a pointer that has one.

/** Which way the finger carried the row, which is what decides what lifting it asks for. */
export type RowSwipeDirection = 'leftward' | 'rightward';

/** How long the tap arriving after a swipe that acted is ignored for, so an act does not also open what it acted on. */
export const swipeSuppressesTapFor = 400;

/** What a row binds to answer a swipe, beside what it is drawing while one is in flight. */
export interface RowSwipe {
    /** How far the row is drawn from where it started, which is `0` for a row no finger is carrying. */
    readonly carried: number;

    /** What lifting the finger now would do, or `null` while the row would spring back instead. */
    readonly commits: RowSwipeDirection | null;

    readonly onPointerDown: (event: PointerEvent) => void;
    readonly onPointerMove: (event: PointerEvent) => void;
    readonly onPointerUp: (event: PointerEvent) => void;
    readonly onPointerCancel: () => void;

    /** Whether the tap now arriving is the one that follows a swipe which has already acted. */
    readonly tapSuppressed: () => boolean;
}

/**
 * The gesture, bound to one row.
 *
 * @param press The row's press, which the swipe rules out as it engages and defers to once a menu is open.
 * @param acts What a swipe `leftward` and one `rightward` perform, or `undefined` for a direction this list offers
 * nothing in — a row carried that way stays where it is and springs back, rather than promising an act nobody would
 * see happen.
 */
export function useRowSwipe(
    press: RowPress,
    acts: { readonly leftward?: (() => void) | undefined; readonly rightward?: (() => void) | undefined },
): RowSwipe {
    // How far the row is drawn is on the screen, so it is state; where the finger landed is not, so it is a ref. A
    // render per pointer move costs this one row, which is what keeps the list's scrolling out of it.
    const [carried, setCarried] = useState(0);
    const from = useRef<{ readonly pointer: number; readonly x: number; readonly y: number } | null>(null);
    const suppressing = useRef<number | null>(null);

    function stop(): void {
        from.current = null;
        setCarried(0);
    }

    // A row scrolled out of the window while its suppression window is still running takes the timer with it.
    useEffect(
        () => () => {
            if (suppressing.current !== null) {
                window.clearTimeout(suppressing.current);
            }
        },
        [],
    );

    /** Which way a swipe this far across went. */
    function directionOf(across: number): RowSwipeDirection {
        return across < 0 ? 'leftward' : 'rightward';
    }

    /** What that direction performs, or `undefined` where the list offers nothing that way. */
    function actOf(across: number): (() => void) | undefined {
        return acts[directionOf(across)];
    }

    return {
        carried,
        commits: swipeSoFar(carried, 0) === 'committed' ? directionOf(carried) : null,

        onPointerDown: (event) => {
            if (!pressedByFinger(event.pointerType)) {
                return;
            }

            from.current = { pointer: event.pointerId, x: event.clientX, y: event.clientY };
        },

        onPointerMove: (event) => {
            const started = from.current;

            if (started?.pointer !== event.pointerId) {
                return;
            }

            // A menu is open over this row, so the finger still on it belongs to the press rather than to a swipe.
            if (press.tapSuppressed()) {
                stop();

                return;
            }

            const across = event.clientX - started.x;

            if (swipeSoFar(across, event.clientY - started.y) === 'cancelled') {
                stop();

                return;
            }

            // A direction the list does not offer draws nothing and carries nothing, so a finger that goes that way
            // meets a row that did not move rather than one that moved and then refused.
            setCarried(actOf(across) === undefined ? 0 : swipeCarry(across));
        },

        onPointerUp: (event) => {
            const started = from.current;
            const carriedTo = carried;

            stop();

            if (started?.pointer !== event.pointerId || swipeSoFar(carriedTo, 0) !== 'committed') {
                return;
            }

            const act = actOf(carriedTo);

            if (act === undefined) {
                return;
            }

            // The lift that finished the swipe is also a tap, and a row that acted twice on one finger would open what
            // it has just acted on.
            if (suppressing.current !== null) {
                window.clearTimeout(suppressing.current);
            }

            suppressing.current = window.setTimeout(() => {
                suppressing.current = null;
            }, swipeSuppressesTapFor);

            act();
        },

        onPointerCancel: stop,
        tapSuppressed: () => suppressing.current !== null,
    };
}
