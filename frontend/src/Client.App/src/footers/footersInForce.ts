// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext, useEffect, useState } from 'react';
import {
    readMailFooters,
    type ClientSession,
    type MailDocument,
    type MailFathomTransport,
} from '@mailfathom/client-backend';

// The footer each of the person's accounts sends with, which the service appends when a message leaves. It is shown
// where a message is about to go out — under the words in the composer, under an Agent's draft — and never written
// into what is being written, so nothing here is kept beside a draft or anywhere a reload would find it.
//
// **A footer that could not be read is a footer not shown, and nothing else.** The message is written and sent exactly
// as it would be with one drawn: the service appends the footer whether or not the client managed to show it, so a
// failed read has nothing to warn about that the person could act on, and writing is never held for it.

/** Each account's footer, by the account's identifier; an account sending without one has no entry. */
export type FootersInForce = ReadonlyMap<string, MailDocument>;

const noFooters: FootersInForce = new Map();

/** The footers the frame read for the session, which the composer and an Agent's draft draw by. */
export const FootersInForceContext = createContext<FootersInForce>(noFooters);

/**
 * Reads the footers in force once for the session it is given.
 *
 * @param session Who is asking, or `null` where nobody is — in which case nothing is read and no footer is drawn.
 * @param transport How the read goes out.
 */
export function useFootersInForce(session: ClientSession | null, transport: MailFathomTransport): FootersInForce {
    // The session is held beside what it answered, so an answer for a credential this no longer is stops being drawn
    // rather than having to be cleared.
    const [held, setHeld] = useState<{ readonly session: ClientSession; readonly footers: FootersInForce } | null>(
        null,
    );

    useEffect(() => {
        if (session === null) {
            return;
        }

        let listening = true;

        void readMailFooters(session, transport).then((answer) => {
            if (listening && answer.outcome === 'read') {
                setHeld({
                    session,
                    footers: new Map(
                        answer.value.flatMap(({ account, document }) =>
                            document === null ? [] : [[account, document] as const],
                        ),
                    ),
                });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport]);

    return held !== null && held.session === session ? held.footers : noFooters;
}

/** The footer an account sends with, as the surface above handed it down, or `null` for none. */
export function useFooterOf(account: string | null): MailDocument | null {
    const footers = useContext(FootersInForceContext);

    return account === null ? null : (footers.get(account) ?? null);
}
