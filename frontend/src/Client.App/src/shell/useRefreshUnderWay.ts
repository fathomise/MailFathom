// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState } from 'react';
import { useSignalledChanges } from '../signals/signalledChanges';

/** How long a refresh is shown under way, which is one beat of the design's pulse. */
export const refreshShownFor = 1_000;

/**
 * Whether a refresh is under way, whoever asked for it: a press of *Refresh*, a pull of the message list, a
 * connection standing again, or the interval. Every surface that shows a refresh happening, or refuses a second one
 * while it is, reads this rather than a state of its own, so they cannot disagree about when one is under way.
 */
export function useRefreshUnderWay(): boolean {
    const changes = useSignalledChanges();
    const [working, setWorking] = useState(false);

    // Heard rather than set where it was asked for, so every surface says the same thing whoever asked.
    useEffect(
        () =>
            changes.listen((change) => {
                if (change.kind === 'refresh') {
                    setWorking(true);
                }
            }),
        [changes],
    );

    // One beat of the pulse rather than until every surface's read has landed, which no surface reports; tie
    // it to their reads if a refresh ever takes long enough for the difference to show.
    useEffect(() => {
        if (!working) {
            return;
        }

        const settling = window.setTimeout(() => {
            setWorking(false);
        }, refreshShownFor);

        return () => {
            window.clearTimeout(settling);
        };
    }, [working]);

    return working;
}
