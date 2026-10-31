// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    closePresenceClient,
    renewPresenceClient,
    type ClientSession,
    type MailFathomTransport,
    type PresenceActivity,
} from '@mailfathom/client-backend';
import { newIdentifier } from '../agent/newIdentifier';

// This running client telling the deployment it is open, and whether somebody is using it — which is what keeps the
// member present to their colleagues. ADR 0045 is the record.
//
// **It renews every minute**, and a row the deployment holds for it expires three minutes after the last renewal, so a
// client that crashed, lost its network, or was put to sleep stops counting with nothing having to happen.
//
// **Whether somebody is using it is measured here**, from the input the document receives: a pointer, a key, a touch,
// or a wheel. Ten minutes with none of them is idle, which the next renewal says. Input after that renews at once, so
// coming back to the keyboard turns the member active within a second rather than within a minute. A hidden window
// receives no input, so a tab left in the background turns idle on the same rule with nothing treating visibility
// separately.
//
// **It says when it closes**: on `pagehide`, and when its person signs out, a request the browser carries past the
// page. That is the ordinary case and nothing rests on it — the expiry is what covers a request that never arrived.
//
// A member of no organization is answered `404`, which stops the renewing for that session: nobody could see their
// presence and nothing is recorded about them.

/** How often an open client renews, which is a third of how long the deployment keeps a row nobody renewed. */
export const renewEvery = 60_000;

/** How long a client goes with no input before it counts itself idle. */
export const idleAfter = 600_000;

// What a person does to a document, as against what the document does on its own.
const inputs = ['pointerdown', 'pointermove', 'keydown', 'touchstart', 'wheel'] as const;

/** What the frame does with this client's presence beyond keeping it open: closing it as the person signs out. */
export interface PresenceClient {
    readonly close: () => void;
}

/**
 * Keeps this running client open to the deployment for as long as there is a session to keep it open with.
 *
 * @param session Who is present and where, or `null` where nothing is renewed — a credential that may not write the
 * profile, a member of no organization, and a machine with no network all pass `null`.
 * @param transport How a request reaches the deployment.
 */
export function usePresenceClient(session: ClientSession | null, transport: MailFathomTransport): PresenceClient {
    // One identity for the life of the running client, minted when it starts, as every identifier MailFathom mints is.
    const [client] = useState(() => newIdentifier());
    const closing = useRef<() => void>(() => undefined);

    useEffect(() => {
        if (session === null) {
            return;
        }

        let stopped = false;
        let lastInput = Date.now();
        let reported: PresenceActivity | null = null;

        const renew = (): void => {
            if (stopped) {
                return;
            }

            reported = Date.now() - lastInput >= idleAfter ? 'idle' : 'active';

            void renewPresenceClient(session, transport, client, reported).then((answer) => {
                if (answer.outcome === 'failed' && answer.failure.reason === 'missing') {
                    stop();
                }
            });
        };

        const noticed = (): void => {
            lastInput = Date.now();

            if (reported === 'idle') {
                renew();
            }
        };

        const left = (): void => {
            if (!stopped) {
                void closePresenceClient(session, transport, client);
            }
        };

        // A page restored from the back-forward cache said it had closed as it went, so it says it is open again.
        const returned = (event: PageTransitionEvent): void => {
            if (event.persisted) {
                renew();
            }
        };

        const timer = setInterval(renew, renewEvery);

        for (const input of inputs) {
            document.addEventListener(input, noticed, { passive: true, capture: true });
        }

        window.addEventListener('pagehide', left);
        window.addEventListener('pageshow', returned);

        function stop(): void {
            stopped = true;
            clearInterval(timer);

            for (const input of inputs) {
                document.removeEventListener(input, noticed, { capture: true });
            }

            window.removeEventListener('pagehide', left);
            window.removeEventListener('pageshow', returned);
        }

        closing.current = () => {
            left();
            stop();
        };

        renew();

        return () => {
            closing.current = () => undefined;
            stop();
        };
    }, [session, transport, client]);

    const close = useCallback(() => {
        closing.current();
    }, []);

    return { close };
}
