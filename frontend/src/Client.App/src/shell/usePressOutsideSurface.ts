// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect } from 'react';
import { drawnAsSheet } from './useSurfacePull';

// A drawer or a sheet stands in for something a wider window draws in place, so pressing beside it puts it away the
// way its close control does: it was in the way of the screen, and the screen is what the press was reaching for.
//
// **The surfaces are the ones `useSurfacePull` already takes**, marked with `data-pull`, so the two ways of putting one
// away cannot disagree about which surfaces have them. A press on a modal dialog's backdrop is delivered to the dialog
// itself, which is why the press is held against the surface's own box rather than read off its target alone: a press
// in the dialog's own padding is a press inside it. A surface that is a centred dialog at the width it stands at is
// not one of them, for the reason the pull leaves it alone, and a popover marked the same way dismisses itself already.
//
// **The press has to begin beside the surface as well as end there.** A drag that starts inside — selecting the text
// in a field — and is let go over the backdrop is delivered as a click on the dialog outside its box, and putting the
// surface away under it would take what was being selected or typed with it.

/** Whether a press at that point fell outside the surface it was delivered to. */
export function pressedOutside(surface: Element, x: number, y: number): boolean {
    const box = surface.getBoundingClientRect();

    return x < box.left || x > box.right || y < box.top || y > box.bottom;
}

/** Closes every open `data-pull` dialog pressed beside rather than inside. */
export function usePressOutsideSurface(): void {
    useEffect(() => {
        let begunBeside: EventTarget | null = null;

        function begun(event: PointerEvent): void {
            const surface = event.target;

            begunBeside =
                surface instanceof HTMLDialogElement && pressedOutside(surface, event.clientX, event.clientY)
                    ? surface
                    : null;
        }

        function pressed(event: MouseEvent): void {
            const surface = event.target;

            if (
                surface === begunBeside &&
                surface instanceof HTMLDialogElement &&
                surface.open &&
                surface.hasAttribute('data-pull') &&
                drawnAsSheet(surface) &&
                pressedOutside(surface, event.clientX, event.clientY)
            ) {
                surface.close();
            }
        }

        document.addEventListener('pointerdown', begun);
        document.addEventListener('click', pressed);

        return () => {
            document.removeEventListener('pointerdown', begun);
            document.removeEventListener('click', pressed);
        };
    }, []);
}
