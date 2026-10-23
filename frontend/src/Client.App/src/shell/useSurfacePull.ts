// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect } from 'react';
import { rowSlop, springMilliseconds, swipeSettles } from '../notifications/panelSwipe';

// A sheet pulled down, or a drawer pushed back toward its edge, closes the way its close control does — the design's
// rule for every surface that rises from the foot of the window or enters from its start edge under a finger.
//
// **One listener for every such surface**, installed once at the root: a surface opts in with `data-pull`, `down` for
// a sheet and `start` for a drawer, and is pulled only while it is drawn as one. A surface whose shape changes with the
// width is a sheet on one side of its breakpoint and a centred dialog on the other, and only the sheet travels on
// `translate` — so the transition it is styled with right now is what says whether a finger may take it.
//
// **Closing is the platform's**, through `close()` or `hidePopover()`, so a pulled surface leaves by the path Escape
// and the back gesture take and its owner hears of it the way it already does.
//
// **Touch events rather than pointer events**, because a vertical drag on a touch screen is a pan the browser answers
// by cancelling the pointer; a `touchmove` that is cancelled while the content is still at its top is what keeps the
// drag the sheet's rather than the page's.

type Direction = 'down' | 'start';

interface Held {
    readonly surface: HTMLElement;
    readonly target: Element;
    readonly direction: Direction;
    readonly size: number;
    readonly crossFrom: number;
    from: number;
    at: number;
    when: number;
    velocity: number;
    taken: boolean;
}

function isOpen(surface: HTMLElement): boolean {
    return surface instanceof HTMLDialogElement ? surface.open : surface.matches(':popover-open');
}

/** Whether the surface is drawn as a sheet or a drawer right now, which is what travels on `translate`. */
export function drawnAsSheet(surface: HTMLElement): boolean {
    return getComputedStyle(surface)
        .transitionProperty.split(',')
        .some((property) => property.trim() === 'translate');
}

function close(surface: HTMLElement): void {
    if (surface instanceof HTMLDialogElement) {
        surface.close();
    } else {
        surface.hidePopover();
    }
}

/** Whether something between the touch and the surface is scrolled away from its top, and so scrolls before it moves. */
function scrolledBelowTop(target: Element, surface: HTMLElement): boolean {
    for (
        let element: Element | null = target;
        element !== null && element !== surface;
        element = element.parentElement
    ) {
        if (element.scrollTop > 0.5) {
            return true;
        }
    }

    return surface.scrollTop > 0.5;
}

function draw(surface: HTMLElement, direction: Direction, offset: number): void {
    surface.style.transition = 'none';
    surface.style.translate = direction === 'down' ? `0 ${String(offset)}px` : `${String(-offset)}px 0`;
}

function release(surface: HTMLElement): void {
    surface.style.removeProperty('transition');
    surface.style.removeProperty('translate');
}

function springBack(surface: HTMLElement): void {
    surface.style.transition = `translate ${String(springMilliseconds)}ms var(--ease-arrive)`;
    surface.style.removeProperty('translate');
    window.setTimeout(() => {
        surface.style.removeProperty('transition');
    }, springMilliseconds);
}

/** Lets a finger close every sheet and drawer marked with `data-pull` by pulling it the way it came. */
export function useSurfacePull(): void {
    useEffect(() => {
        let held: Held | null = null;

        function started(event: TouchEvent): void {
            held = null;

            const touch = event.touches.length === 1 ? event.touches[0] : undefined;

            if (touch === undefined || !(event.target instanceof Element)) {
                return;
            }

            const surface = event.target.closest<HTMLElement>('[data-pull]');

            if (surface === null || !isOpen(surface) || !drawnAsSheet(surface)) {
                return;
            }

            const direction: Direction = surface.getAttribute('data-pull') === 'start' ? 'start' : 'down';
            const along = direction === 'down' ? touch.clientY : -touch.clientX;

            held = {
                surface,
                target: event.target,
                direction,
                size: direction === 'down' ? surface.offsetHeight : surface.offsetWidth,
                crossFrom: direction === 'down' ? touch.clientX : touch.clientY,
                from: along,
                at: along,
                when: event.timeStamp,
                velocity: 0,
                taken: false,
            };
        }

        function moved(event: TouchEvent): void {
            const gesture = held;
            const touch = event.touches[0];

            if (gesture === null || touch === undefined) {
                return;
            }

            const along = gesture.direction === 'down' ? touch.clientY : -touch.clientX;
            const across = (gesture.direction === 'down' ? touch.clientX : touch.clientY) - gesture.crossFrom;

            gesture.velocity = (along - gesture.at) / Math.max(1, event.timeStamp - gesture.when);
            gesture.at = along;
            gesture.when = event.timeStamp;

            if (!gesture.taken) {
                const travelled = along - gesture.from;

                if (Math.abs(across) > Math.max(rowSlop, Math.abs(travelled)) || travelled < -rowSlop) {
                    held = null;

                    return;
                }

                if (travelled < rowSlop) {
                    return;
                }

                // Content scrolled away from its top scrolls first, and the zero point moves with the finger while it
                // does, so the same movement becomes the sheet's the moment the content reaches the top.
                if (gesture.direction === 'down' && scrolledBelowTop(gesture.target, gesture.surface)) {
                    gesture.from = along;

                    return;
                }

                gesture.taken = true;
                gesture.from = along;
            }

            if (event.cancelable) {
                event.preventDefault();
            }

            draw(gesture.surface, gesture.direction, Math.max(0, Math.min(gesture.size, along - gesture.from)));
        }

        function released(): void {
            const gesture = held;

            held = null;

            if (!gesture?.taken) {
                return;
            }

            const travelled = gesture.size > 0 ? Math.max(0, gesture.at - gesture.from) / gesture.size : 0;

            if (swipeSettles(travelled, gesture.velocity, true)) {
                release(gesture.surface);
                close(gesture.surface);

                return;
            }

            springBack(gesture.surface);
        }

        document.addEventListener('touchstart', started, { passive: true });
        document.addEventListener('touchmove', moved, { passive: false });
        document.addEventListener('touchend', released);
        document.addEventListener('touchcancel', released);

        return () => {
            document.removeEventListener('touchstart', started);
            document.removeEventListener('touchmove', moved);
            document.removeEventListener('touchend', released);
            document.removeEventListener('touchcancel', released);
        };
    }, []);
}
