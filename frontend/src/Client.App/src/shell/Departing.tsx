// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState, type ReactNode } from 'react';
import { animationsReported, departureFinished, DepartureContext } from './departure';

// How a surface drawn in the document leaves rather than vanishing. A surface the platform puts in its top layer leaves
// by a transition the stylesheet already holds, because the platform keeps it drawn while it travels; one a component
// renders conditionally is gone the moment its value is, and there is nothing left to animate. So the last value is kept
// for as long as the surface's own departure is running, and the surface is told it is leaving so it can play that
// departure and stop answering anything.
//
// How long that is belongs to the stylesheet rather than to this file, which is why nothing here names a duration: what
// is waited on is the animations the platform reports running on the surface, and the value goes when they have
// finished. One that never finishes — a spinner inside the surface — is not waited on. Where the platform reports none
// at all, the motion was removed under `prefers-reduced-motion` or the surface is not drawn, and the value goes at once;
// where it cannot report any — jsdom implements no animation — nothing is held to begin with.

/**
 * Draws a surface from `value` while there is one, and from the last one while the surface leaves.
 *
 * The surface reads `DepartureContext` to learn it is leaving and plays its departure; everything inside is inert
 * meanwhile, so a press on something already going reaches nothing. What is waited on is every animation running
 * inside the one element this draws around the surface, which draws no box of its own.
 *
 * @param value What the surface is drawn from, or `null` once it should go.
 */
export function Departing<T>({
    value,
    children,
}: {
    readonly value: T | null;
    readonly children: (shown: T) => ReactNode;
}) {
    // Compared by identity, and safe to be because it arrives as a prop: a render of this component's own leaves the
    // value it was handed as it was, so a caller building a fresh one every render costs one extra render and no more.
    const [held, setHeld] = useState<T | null>(value);
    const [element, setElement] = useState<HTMLDivElement | null>(null);

    // Every arrival is a fresh surface, including one that comes back while it is still leaving: what a surface does as
    // it opens — showing a dialog, moving focus in — runs when it mounts, so a surface kept through its own departure
    // and asked for again would otherwise stand on the screen without having opened.
    const [previous, setPrevious] = useState(value);
    const [arrivals, setArrivals] = useState(0);

    if (value !== previous) {
        setPrevious(value);

        if (previous === null) {
            setArrivals(arrivals + 1);
        }
    }

    if (value !== null && value !== held) {
        setHeld(value);
    }

    const leaving = animationsReported && value === null && held !== null;
    const shown = leaving ? held : value;

    useEffect(() => {
        if (!leaving) {
            return;
        }

        let waiting = true;

        void departureFinished(element).then(() => {
            if (waiting) {
                setHeld(null);
            }
        });

        return () => {
            waiting = false;
        };
    }, [leaving, element]);

    return shown === null ? null : (
        <div key={arrivals} ref={setElement} inert={leaving} className="contents">
            <DepartureContext value={leaving}>{children(shown)}</DepartureContext>
        </div>
    );
}
