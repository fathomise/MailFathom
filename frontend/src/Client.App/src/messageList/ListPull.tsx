// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useRef, useState, type ReactNode, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import { springMilliseconds } from '../notifications/panelSwipe';
import { useRefreshUnderWay } from '../shell/useRefreshUnderWay';
import { useSignalledChanges } from '../signals/signalledChanges';
import { pullDisc, pullOffset, pullRefreshes, pullRefreshesAt, pullRests, pullSoFar } from './pullTravel';

// The message list pulled down from its top asks for the one refresh — the same act the *Refresh* control asks for, so
// every surface reads again under what it draws, the control shows it under way, and a pull while one is already under
// way asks nothing. The control stays where it is: it is the pull's single-pointer alternative, and the only way a mouse
// or a key asks.
//
// **A finger's alone.** It is bound only under a coarse pointer, and a touch decides it rather than a pointer: a vertical
// drag on a touch screen is a pan the browser answers by cancelling the pointer, so a cancellable `touchmove` is what
// keeps the drag the pull's rather than the list's — which is why the listeners are the platform's rather than React's,
// whose touch listeners cannot cancel anything.
//
// **One finger, one gesture.** The pull starts only on a list already at its first row, and is decided at the distance a
// row's swipe engages at: more down than across is a pull, and by then the row's long press is already off; anything
// else is a scroll or the row's swipe, and that touch takes no pull. A pull that has engaged keeps the row from being
// carried, because the row's own swipe gives up on a finger travelling further down than across.
//
// **Nothing is emptied.** The rows stay as they are while the read is on the wire, which is the refresh's own rule, and
// the disc is drawn over them rather than pushing them down — a windowed list placing its reader itself would otherwise
// have to be told about a gap at its top.

/** What a finger on the list is doing, as far as the pull is concerned. */
interface Held {
    readonly x: number;
    readonly y: number;
    down: number;
    taken: boolean;
}

/** Whether the reader asked for less motion, read when a pull starts rather than watched: one pull is one answer. */
function motionReduced(): boolean {
    return window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

/**
 * The list's pull, around the list.
 *
 * @param scroller The element the rows scroll in, which says whether the list is at its first row.
 * @param enabled Whether the pointer driving the client is a finger's. Without one nothing about the list changes.
 */
export function ListPull({
    scroller,
    enabled,
    children,
}: {
    readonly scroller: RefObject<HTMLElement | null>;
    readonly enabled: boolean;
    readonly children: ReactNode;
}) {
    const changes = useSignalledChanges();
    const refreshing = useRefreshUnderWay();
    const surface = useRef<HTMLDivElement>(null);

    // How far the finger has travelled while a pull is taken, and `null` while none is.
    const [pulled, setPulled] = useState<number | null>(null);
    const [still, setStill] = useState(false);

    // Whether the last pull ended in a refresh it is waiting for. Only a pull puts the disc in its waiting place, so a
    // refresh asked for anywhere else does not cover the first row, and it is let go of the moment no refresh is under
    // way — including at once, when the refresh it asked for was one there was nothing to read over.
    const [settled, setSettled] = useState(false);

    if (settled && !refreshing) {
        setSettled(false);
    }

    const released = useEffectEvent((down: number, lifted: boolean) => {
        setPulled(null);

        if (refreshing) {
            setSettled(true);
        } else if (lifted && pullRefreshes(down)) {
            setSettled(true);
            changes.refresh();
        }
    });

    useEffect(() => {
        const element = surface.current;

        if (!enabled || element === null) {
            return;
        }

        let held: Held | null = null;

        function started(event: TouchEvent): void {
            // A second finger landing ends a pull the first one had taken, as the platform taking the touch would.
            if (held?.taken === true) {
                released(held.down, false);
            }

            const touch = event.touches.length === 1 ? event.touches[0] : undefined;
            const atTop = (scroller.current?.scrollTop ?? 0) <= 0.5;

            held = touch === undefined || !atTop ? null : { x: touch.clientX, y: touch.clientY, down: 0, taken: false };
        }

        function moved(event: TouchEvent): void {
            const gesture = held;
            const touch = event.touches[0];

            if (gesture === null || touch === undefined) {
                return;
            }

            const down = touch.clientY - gesture.y;

            gesture.down = down;

            if (!gesture.taken) {
                const decision = pullSoFar(touch.clientX - gesture.x, down);

                if (decision === 'undecided') {
                    return;
                }

                if (decision === 'declined') {
                    held = null;

                    return;
                }

                gesture.taken = true;
                setStill(motionReduced());
            }

            if (event.cancelable) {
                event.preventDefault();
            }

            setPulled(Math.max(0, down));
        }

        function ended(lifted: boolean): () => void {
            return () => {
                const gesture = held;

                held = null;

                if (gesture?.taken === true) {
                    released(gesture.down, lifted);
                }
            };
        }

        const lifted = ended(true);
        const cancelled = ended(false);

        element.addEventListener('touchstart', started, { passive: true });
        element.addEventListener('touchmove', moved, { passive: false });
        element.addEventListener('touchend', lifted);
        element.addEventListener('touchcancel', cancelled);

        return () => {
            element.removeEventListener('touchstart', started);
            element.removeEventListener('touchmove', moved);
            element.removeEventListener('touchend', lifted);
            element.removeEventListener('touchcancel', cancelled);
        };
    }, [enabled, scroller]);

    const waiting = settled && refreshing;
    const pulling = pulled !== null;
    const armed = refreshing || (pulling && pullRefreshes(pulled));

    // The disc's top edge: following half the finger, standing in its waiting place, or back above the list, where the
    // transition it is released into is the spring back. Under reduced motion it never travels, so it stands in its
    // waiting place for the whole of a pull and is gone the moment the pull is.
    const offset = waiting || (pulling && still) ? pullRests : pulling ? pullOffset(pulled) : -pullDisc;
    const opacity = waiting || (pulling && still) ? 1 : Math.min(1, Math.max(0, (offset + pullDisc) / pullDisc));
    const turned = pulling && !still ? (Math.min(1, pulled / pullRefreshesAt) * 270).toFixed(0) : '0';

    return (
        <div ref={surface} className="relative flex min-h-0 flex-1 flex-col">
            {enabled ? (
                <div
                    aria-hidden="true"
                    className={`pointer-events-none absolute top-0 left-1/2 z-10 -ml-5 flex size-10 items-center justify-center rounded-full border bg-panel shadow-raised ${armed ? 'border-accent-line text-accent-deep' : 'border-line text-text-soft'}`}
                    style={{
                        translate: `0 ${String(offset)}px`,
                        opacity,
                        transition: pulling
                            ? 'none'
                            : `translate ${String(springMilliseconds)}ms var(--ease-sheet), opacity ${String(springMilliseconds)}ms var(--ease-sheet)`,
                    }}
                >
                    <span className="flex" style={{ rotate: `${turned}deg` }}>
                        <Icon name="refresh" className={refreshing ? 'size-icon-lg animate-working' : 'size-icon-lg'} />
                    </span>
                </div>
            ) : null}

            {children}
        </div>
    );
}
