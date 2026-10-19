// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';

/** Which way the view in front of a one-pane composition last moved, or `null` before it has moved at all. */
export type PaneMotion = 'push' | 'back';

/**
 * Says which way one pane at a time last moved, so the pane arriving plays the direction it came from: deeper when a
 * view was put in front of the one it was opened from, back when it was taken away again.
 *
 * Derived during the render that moved it rather than in an effect watching for it, because it is the same change
 * read twice. A change of composition is not a move — a window widened under an open message shows both panes rather
 * than going back from one — so a render that changes the composition as well records nothing.
 *
 * @param inFront Whether the deeper view is the one in front.
 * @param composition What the screen is composed as, which is whatever the caller's own breakpoint reads.
 */
export function usePaneMotion(inFront: boolean, composition: unknown): PaneMotion | null {
    const [drawn, setDrawn] = useState({ inFront, composition });
    const [motion, setMotion] = useState<PaneMotion | null>(null);

    if (drawn.inFront !== inFront || drawn.composition !== composition) {
        setDrawn({ inFront, composition });
        setMotion(drawn.composition === composition ? (inFront ? 'push' : 'back') : null);
    }

    return motion;
}

/** The arrival a pane plays when it is the one now in front: the deeper view on a push, the one it left on a back. */
export function paneArrival(motion: PaneMotion | null, deeper: boolean): string {
    if (motion === null) {
        return '';
    }

    return deeper === (motion === 'push') ? (deeper ? 'animate-pane-push' : 'animate-pane-back') : '';
}
