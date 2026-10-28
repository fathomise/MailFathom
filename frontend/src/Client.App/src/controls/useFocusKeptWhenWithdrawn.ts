// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useLayoutEffect, useRef, type RefObject } from 'react';

// Where focus goes when the controls somebody is using are withdrawn under them.
//
// A group of controls withdrawn as a whole — a fieldset disabled while the server is out of reach — is withdrawn while a
// surface stands open, so the control a person was on can stop taking focus in the middle of their using it. The platform
// then drops focus to the body, which is where keyboard and screen-reader use silently stops: `Control` refuses to
// disable what was just pressed for exactly that reason. So the sentence saying why the group was withdrawn takes focus
// instead, which is also the first thing that person needs to read.

/** What can take focus in a group offered again, which is any control that is not withdrawn. */
const offeredControl =
    'button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [tabindex="0"]';

/**
 * Hands focus to the sentence saying why a group was withdrawn, at the moment it is withdrawn, where focus would
 * otherwise be dropped.
 *
 * Hands it back to the group's first control once the group is offered again and the sentence is gone, for the same
 * reason.
 *
 * @param withdrawn Whether the group is withdrawn now.
 * @returns The ref the sentence is drawn with; it is focusable by script alone (`tabIndex={-1}`).
 */
export function useFocusKeptWhenWithdrawn<TElement extends HTMLElement>(
    withdrawn: boolean,
): RefObject<TElement | null> {
    const reason = useRef<TElement | null>(null);

    // Where the sentence stood while it held focus, kept so that focus has somewhere to go back to when the sentence
    // goes: the group offered again is inside it.
    const heldIn = useRef<HTMLElement | null>(null);

    // A layout effect, because it has to run after the controls were withdrawn and before the platform's own fix-up
    // moves focus to the body: that is the one moment focus can still be placed rather than recovered.
    useLayoutEffect(() => {
        const standing = document.activeElement;
        const dropped = standing === null || standing === document.body || standing.matches(':disabled');

        if (withdrawn) {
            if (dropped) {
                reason.current?.focus();
                heldIn.current = reason.current?.parentElement ?? null;
            }

            return;
        }

        // Offered again: the sentence that held focus has gone with the withdrawal, so focus goes to the first control
        // the group offers rather than being left on an element no longer there. Focus somebody moved elsewhere in the
        // meantime is theirs and stays.
        const holder = heldIn.current;

        heldIn.current = null;

        if (holder !== null && dropped) {
            holder.querySelector<HTMLElement>(offeredControl)?.focus();
        }
    }, [withdrawn]);

    return reason;
}
