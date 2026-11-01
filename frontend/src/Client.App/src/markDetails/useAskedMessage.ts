// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState } from 'react';
import {
    readMailMessage,
    type ClientFailure,
    type ClientSession,
    type MailFathomTransport,
    type MailMessage,
} from '@mailfathom/client-backend';

// The message a mark's details are drawn from: in hand where the surface that asked had already read it, and one read
// of it otherwise — made when the details open, since a row publishes the few facts its mark is chosen from and none of
// the evidence behind them.

export interface AskedMessageRead {
    readonly message: MailMessage | null;
    readonly failure: ClientFailure | null;
    readonly readAgain: () => void;
}

/** What one answer was to, so an answer for a message nobody is asking about any more is not drawn as this one. */
interface MessageAnswer {
    readonly attempt: number;
    readonly message: MailMessage | null;
    readonly failure: ClientFailure | null;
}

export function useAskedMessage(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailId: string,
    inHand: MailMessage | null,
): AskedMessageRead {
    const [attempt, setAttempt] = useState(0);
    const [answer, setAnswer] = useState<MessageAnswer | null>(null);

    useEffect(() => {
        if (inHand !== null) {
            return;
        }

        let listening = true;

        void readMailMessage(session, transport, storedEmailId).then((result) => {
            if (listening) {
                setAnswer({
                    attempt,
                    message: result.outcome === 'read' ? result.value : null,
                    failure: result.outcome === 'failed' ? result.failure : null,
                });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, storedEmailId, inHand, attempt]);

    const standing = answer?.attempt === attempt ? answer : null;

    return {
        message: inHand ?? standing?.message ?? null,
        failure: inHand === null ? (standing?.failure ?? null) : null,
        readAgain: () => {
            setAttempt(attempt + 1);
        },
    };
}
