// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext } from 'react';

/** What a surface rendered inside `Departing` reads to learn it is leaving. Outside one, nothing ever is. */
export const DepartureContext = createContext(false);

/** Whether this platform reports running animations at all; jsdom implements none, so nothing is held there. */
export const animationsReported =
    typeof Element === 'function' && typeof Element.prototype.getAnimations === 'function';

/**
 * Settles once every animation running inside `element` has finished, which is how long a departure is held.
 *
 * One that never finishes — a spinner — is not waited on, and where nothing is running it settles at once, which is
 * what `prefers-reduced-motion` leaves.
 */
export function departureFinished(element: Element | null): Promise<unknown> {
    const running = (element?.getAnimations({ subtree: true }) ?? []).filter(
        (animation) => animation.effect?.getComputedTiming().endTime !== Infinity,
    );

    return Promise.allSettled(running.map((animation) => animation.finished));
}
