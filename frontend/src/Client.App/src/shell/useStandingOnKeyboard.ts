// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, type RefObject } from 'react';

// An on-screen keyboard lies over the window: the viewport meta asks for it, so the layout, the height bands, and where
// the navigation stands are the screen's and do not move when a field takes the keyboard. What answers the keyboard is
// the one surface being written in, which stands on it — and this is how such a surface learns how much of itself the
// keyboard covers.
//
// **Measured rather than assumed.** The visual viewport is what the keyboard leaves, and what it covers of a surface is
// the distance from the viewport's foot to where the surface would otherwise end — wherever a pane, a bar of
// navigation, or the window itself put that. The surface's own box says where it ends now, which is already shorter by
// what was last published, so that is added back. The distance is published on the surface as `--keyboard-cover`, which the `above-keyboard` utility
// in `styles.css` takes off its height, and the surface is marked `data-on-keyboard` while it is more than nothing, which
// the `on-keyboard:` variant reads — so the measurement stays here and the layout stays in the stylesheet.
//
// A pinch zoom shrinks the same viewport and is not a keyboard, so a scaled viewport covers nothing.

/** How much of the surface the keyboard covers, in CSS pixels. */
export function keyboardCover(surface: HTMLElement, viewport: VisualViewport, alreadyTakenOff: number): number {
    if (viewport.scale !== 1) {
        return 0;
    }

    const foot = surface.getBoundingClientRect().bottom + alreadyTakenOff;

    return Math.max(0, Math.round(foot - (viewport.offsetTop + viewport.height)));
}

/** Keeps the surface standing on the on-screen keyboard, and what is being written in it on the screen. */
export function useStandingOnKeyboard(surface: RefObject<HTMLElement | null>): void {
    useEffect(() => {
        // A runtime with no visual viewport answers `undefined` rather than the `null` its type states.
        const viewport = window.visualViewport ?? null;
        const element = surface.current;

        if (viewport === null || element === null) {
            return;
        }

        let covered = 0;

        function stand(): void {
            if (viewport === null || element === null) {
                return;
            }

            const now = keyboardCover(element, viewport, covered);

            if (now === covered) {
                return;
            }

            const risen = now > covered;

            covered = now;
            element.style.setProperty('--keyboard-cover', `${String(now)}px`);
            element.toggleAttribute('data-on-keyboard', now > 0);

            // The field being written in was placed for the taller surface, so it is brought back above the foot.
            if (risen && document.activeElement instanceof HTMLElement && element.contains(document.activeElement)) {
                document.activeElement.scrollIntoView({ block: 'nearest' });
            }
        }

        stand();
        viewport.addEventListener('resize', stand);
        viewport.addEventListener('scroll', stand);

        return () => {
            viewport.removeEventListener('resize', stand);
            viewport.removeEventListener('scroll', stand);
            element.style.removeProperty('--keyboard-cover');
            element.removeAttribute('data-on-keyboard');
        };
    }, [surface]);
}
