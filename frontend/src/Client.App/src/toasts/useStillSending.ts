// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState } from 'react';
import type { RequestsInFlight } from '../deployment/requestsInFlight';

// Whether a write this client asked for has been waiting long enough to say so.
//
// A write the server has not answered looks done to whoever made it: the act is drawn at the press, and the toast that
// reports it comes with the answer. That is right for the moment an answer takes, and wrong for a link that died — for
// up to a minute before the probe notices — and for a server that is alive and stalled, which no probe ever abandons
// and which can otherwise look done for as long as its operator's ceiling. So a write with no answer after
// {@link stillSendingAfter} is said, quietly, until it is answered or ends as unknown.

/** How long a write may wait for its answer before *Still sending…* is said. */
export const stillSendingAfter = 10_000;

/**
 * Whether some write in flight has waited at least {@link stillSendingAfter} for its answer.
 *
 * @param inFlight The requests this client has in flight, which is the one place a write's waiting is known.
 */
export function useStillSending(inFlight: RequestsInFlight): boolean {
    const [waiting, setWaiting] = useState(false);

    // Synchronizes with the requests in flight and with the clock, which is what an effect is for: the answer changes
    // as a write enters or leaves, and as the oldest one crosses the threshold, and nothing renders in between.
    useEffect(() => {
        let timer: ReturnType<typeof setTimeout> | undefined;

        const check = (): void => {
            clearTimeout(timer);

            const oldest = inFlight.oldestWrite();

            if (oldest === null) {
                setWaiting(false);

                return;
            }

            const due = oldest + stillSendingAfter - Date.now();

            setWaiting(due <= 0);

            if (due > 0) {
                timer = setTimeout(check, due);
            }
        };

        check();

        const stopListening = inFlight.listen(check);

        return () => {
            clearTimeout(timer);
            stopListening();
        };
    }, [inFlight]);

    return waiting;
}
