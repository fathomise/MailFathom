// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useLayoutEffect, useRef, useState, type KeyboardEvent, type PointerEvent } from 'react';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';

// The grip on the chosen picture's bottom corner, dragged the way a window's corner is: the width follows the pointer
// and the height keeps the picture's proportions, which the picture's own `height: auto` does while the grip writes
// only its width. It is a separator that takes focus, as `WidthGrip` is, so everything a pointer does with it a key
// does too — the arrows move it in steps, and `Home` and a double-click take the picture back to its widest.
//
// **It stands beside the editable region rather than inside it**, so nothing of it is ever written: it is placed over
// the picture's corner from the two boxes, and placed again whenever anything scrolls or the region changes size. Its
// target reaches inward over the picture from the corner, so it is in view exactly when the corner is, and it is not
// drawn while something else — the composer's foot, a scrolled sheet's edge — stands over that corner.

/** How far one arrow press moves the picture's corner. */
const keyStep = 10;

export function PictureGrip({
    picture,
    width,
    narrowest,
    widest,
    onWidth,
    onChosen,
}: {
    /** The chosen picture, as the editor draws it inside the editable region that scrolls it. */
    readonly picture: HTMLImageElement;

    /** The width the picture is written at, which is the position the grip reports. */
    readonly width: number;

    /** The narrowest the grip makes it. */
    readonly narrowest: number;

    /** The widest it may be, which is the widest the picture is sent at. */
    readonly widest: number;

    /** The width to draw while the corner is being moved. */
    readonly onWidth: (width: number) => void;

    /** The width somebody settled on, which is the one the message keeps. */
    readonly onChosen: (width: number) => void;
}) {
    const { translate } = useLocalization();
    const grip = useRef<HTMLDivElement | null>(null);
    const dragging = useRef<{ pointer: number; from: number; startedAt: number; reached: number } | null>(null);
    const [corner, setCorner] = useState<{ left: number; top: number } | null>(null);

    // A finger's hold on the grip is the drag, so it never brings the hint up — one held finger opens one surface.
    const { anchor, tip } = useTip({
        says: translate('compose.resizePictureHint'),
        describes: true,
        tapReveals: false,
    });

    const within = (wanted: number) => Math.round(Math.min(widest, Math.max(narrowest, wanted)));

    // The width a drag or a key starts from: the one drawn, which a region narrower than the picture fits below the
    // one written, so the first move of a drag never jumps the picture past where the pointer took hold of it. A
    // picture drawn at nothing — its octets not decoded yet, or a runtime with no layout — starts from the one written.
    const shownWidth = () => {
        const drawn = picture.getBoundingClientRect().width;

        return drawn > 0 ? Math.min(width, drawn) : width;
    };

    useLayoutEffect(() => {
        const region = picture.closest<HTMLElement>('[contenteditable]');

        function place(): void {
            const target = grip.current;
            const base = target?.offsetParent;

            // A runtime with no layout, which is what the unit suite renders in, places nothing.
            if (
                target === null ||
                base === null ||
                base === undefined ||
                typeof document.elementFromPoint !== 'function'
            ) {
                return;
            }

            // What a press just inside the corner would land on: the picture, or the grip already over it, where the
            // corner is in view, and anything else where it is scrolled away or something is drawn over it.
            const drawn = picture.getBoundingClientRect();
            const from = base.getBoundingClientRect();
            const atCorner = document.elementFromPoint(drawn.right - 1, drawn.bottom - 1);
            const inView = atCorner === picture || (atCorner !== null && target.contains(atCorner));

            setCorner(inView ? { left: drawn.right - from.left, top: drawn.bottom - from.top } : null);
        }

        place();

        const resized = typeof ResizeObserver === 'function' ? new ResizeObserver(place) : null;

        resized?.observe(picture);

        if (region !== null) {
            resized?.observe(region);
        }

        // Any scroll may move the corner — the region's own, or a sheet's around it — so every one is listened to.
        document.addEventListener('scroll', place, true);

        return () => {
            resized?.disconnect();
            document.removeEventListener('scroll', place, true);
        };
    }, [picture, width]);

    function beginDrag(event: PointerEvent<HTMLDivElement>): void {
        if (dragging.current !== null && dragging.current.pointer !== event.pointerId) {
            return;
        }

        // Without this a drag selects the words it passes over, and the picture's selection goes with them.
        event.preventDefault();

        const startedAt = shownWidth();

        dragging.current = { pointer: event.pointerId, from: event.clientX, startedAt, reached: startedAt };

        try {
            event.currentTarget.setPointerCapture(event.pointerId);
        } catch {
            // A synthesised pointer has no capture to hand over; the drag then follows the handlers on the grip alone.
        }
    }

    function moveGrip(event: PointerEvent<HTMLDivElement>): void {
        const drag = dragging.current;

        if (drag?.pointer !== event.pointerId) {
            return;
        }

        drag.reached = within(drag.startedAt + (event.clientX - drag.from));
        onWidth(drag.reached);
    }

    // A release settles where the pointer was let go; a cancelled pointer says nothing about where it was, so the drag
    // settles where it last reached.
    function endDrag(event: PointerEvent<HTMLDivElement>, released: boolean): void {
        const drag = dragging.current;

        if (drag?.pointer !== event.pointerId) {
            return;
        }

        dragging.current = null;
        onChosen(released ? within(drag.startedAt + (event.clientX - drag.from)) : drag.reached);
    }

    function moveByKey(event: KeyboardEvent<HTMLDivElement>): void {
        const keyed: Readonly<Record<string, number | undefined>> = {
            ArrowLeft: shownWidth() - keyStep,
            ArrowRight: shownWidth() + keyStep,
            Home: widest,
        };
        const moved = keyed[event.key];

        if (moved === undefined) {
            return;
        }

        // The arrows move the region's scroll otherwise, and `Home` its caret.
        event.preventDefault();
        onChosen(within(moved));
    }

    return (
        <>
            <div
                {...anchor}
                ref={grip}
                role="separator"
                aria-orientation="vertical"
                aria-label={translate('compose.resizePicture')}
                aria-valuenow={width}
                aria-valuemin={narrowest}
                aria-valuemax={widest}
                tabIndex={0}
                // A 12 px square ringed in the panel's colour and centred on the corner, in a 20 px target reaching
                // inward over the picture; under a finger an 18 px square in a 44 px one.
                className={`absolute z-10 flex size-5 -translate-full cursor-nwse-resize touch-none items-end justify-end rounded-xs pointer-coarse:size-11 ${corner === null ? 'invisible' : ''}`}
                style={corner === null ? undefined : { left: corner.left, top: corner.top }}
                onPointerDown={(event) => {
                    anchor.onPointerDown(event);
                    beginDrag(event);
                }}
                onPointerMove={(event) => {
                    anchor.onPointerMove(event);
                    moveGrip(event);
                }}
                onPointerUp={(event) => {
                    anchor.onPointerUp();
                    endDrag(event, true);
                }}
                onPointerCancel={(event) => {
                    anchor.onPointerCancel();
                    endDrag(event, false);
                }}
                onDoubleClick={() => {
                    onChosen(widest);
                }}
                onKeyDown={(event) => {
                    anchor.onKeyDown(event);
                    moveByKey(event);
                }}
            >
                <span
                    aria-hidden="true"
                    className="size-3 translate-1/2 rounded-xs border-2 border-panel bg-accent shadow-raised pointer-coarse:size-4.5"
                />
            </div>
            {tip}
        </>
    );
}
