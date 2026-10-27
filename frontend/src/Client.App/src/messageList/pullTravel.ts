// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { swipeEngages } from '../controls/swipeAcross';

// The arithmetic of pulling the message list down from its top to refresh it, apart from the touches that produce it.
// Every number is the design's own, so it can be asserted without a browser; what a finger does with them is
// `ListPull.tsx`.

/** How large the disc that follows the pull is. */
export const pullDisc = 40;

/** How far the finger travels before a release refreshes rather than springs back. */
export const pullRefreshesAt = 128;

/** How much of the finger's travel the disc follows, which is the pull's resistance. */
export const pullResistance = 0.5;

/** The furthest the disc travels, however far the finger goes. */
export const pullCarriesTo = 96;

/** How far below the list's top the disc waits while a refresh is under way. */
export const pullRests = 16;

/**
 * What a touch has come to.
 *
 * It is decided once, at the distance a row's swipe engages at, so the same finger is never a pull and a swipe:
 * `undecided` is a touch still inside that distance, `pull` one that went down and more down than across, and
 * `declined` anything else — a scroll upward, or a row's swipe — which takes no pull for the rest of the touch.
 */
export type PullDecision = 'undecided' | 'pull' | 'declined';

/** What a finger that has travelled `across` and `down` from where it landed amounts to. */
export function pullSoFar(across: number, down: number): PullDecision {
    if (Math.max(Math.abs(across), Math.abs(down)) < swipeEngages) {
        return 'undecided';
    }

    return down > Math.abs(across) ? 'pull' : 'declined';
}

/** Where the disc's top edge stands below the list's top for a finger that has travelled `down`. */
export function pullOffset(down: number): number {
    return Math.min(pullCarriesTo, Math.max(0, down) * pullResistance) - pullDisc;
}

/** Whether letting go after `down` asks for a refresh. */
export function pullRefreshes(down: number): boolean {
    return down >= pullRefreshesAt;
}
