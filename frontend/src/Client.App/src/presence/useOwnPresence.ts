// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    choosePresence,
    readOwnPresence,
    type ClientSession,
    type MailFathomTransport,
    type OwnPresence,
    type PresenceDuration,
    type PresenceState,
} from '@mailfathom/client-backend';
import type { SignalledChanges } from '../signals/signalledChanges';

// The signed-in member's own presence, as the rail's dot, the user menu's row, and the status menu draw it.
//
// It is read when the session starts and again whenever it may have moved: a presence change was signalled, a refresh
// was asked for, or the while the member chose ran out — which no request marks, so the client that knows the instant
// reads again at it. The signal names members by their user identity and nothing tells this client its own, so every
// presence change reads the member's own presence again: it is one small read, and guessing which change was theirs
// would be the client deciding something the deployment knows.
//
// A caller in no organization is answered `404`, and is drawn no presence at all, which is what the design draws for a
// reader outside one.

/** The member's presence and the one way of changing it, for a reader who is a member of an organization. */
export interface OwnPresenceInForce {
    readonly presence: OwnPresence;

    /** Chooses a state for a while, or until changed with `null`, answering whether the deployment took it. */
    readonly choose: (state: PresenceState, duration: PresenceDuration | null) => Promise<boolean>;
}

// A timer the platform can hold: anything past this many milliseconds fires at once, so a later end is waited for in
// steps rather than read early.
const longestTimer = 2_147_483_647;

interface Held {
    readonly session: ClientSession;
    readonly presence: OwnPresence | null;
}

/**
 * Holds the signed-in member's own presence.
 *
 * @param session Who is asking and where, or `null` where nothing is read — a credential that may not read the profile,
 * a reader in no organization, and a machine with no network all pass `null`.
 * @param transport How a request reaches the deployment.
 * @param signalled What the deployment says changed, which is the frame's to hand down.
 * @returns The presence, or `null` where there is none to draw.
 */
export function useOwnPresence(
    session: ClientSession | null,
    transport: MailFathomTransport,
    signalled: SignalledChanges,
): OwnPresenceInForce | null {
    const [held, setHeld] = useState<Held | null>(null);
    const latest = useRef(0);

    const readAgain = useCallback((): void => {
        if (session === null) {
            return;
        }

        const asked = ++latest.current;

        void readOwnPresence(session, transport).then((answer) => {
            // A failure that is not `missing` keeps what was drawn: presence is a line beside the name rather than a
            // screen, and the next read puts it right.
            if (asked === latest.current && (answer.outcome === 'read' || answer.failure.reason === 'missing')) {
                setHeld({ session, presence: answer.outcome === 'read' ? answer.value : null });
            }
        });
    }, [session, transport]);

    useEffect(readAgain, [readAgain]);

    useEffect(
        () =>
            signalled.listen((change) => {
                if (change.kind === 'presence.changed' || change.kind === 'refresh') {
                    readAgain();
                }
            }),
        [signalled, readAgain],
    );

    const standing = held?.session === session ? held : null;
    const endsAt = standing?.presence?.choiceEndsAt ?? null;

    // The while runs out at an instant no request marks, and this client knows it.
    useEffect(() => {
        if (endsAt === null) {
            return;
        }

        const timer = setTimeout(
            () => {
                readAgain();
            },
            Math.min(Math.max(Date.parse(endsAt) - Date.now(), 0), longestTimer),
        );

        return () => {
            clearTimeout(timer);
        };
    }, [endsAt, readAgain]);

    const choose = useCallback(
        async (state: PresenceState, duration: PresenceDuration | null): Promise<boolean> => {
            if (session === null) {
                return false;
            }

            const asked = ++latest.current;
            const answer = await choosePresence(session, transport, state, duration);

            if (answer.outcome === 'failed') {
                return false;
            }

            if (asked === latest.current) {
                setHeld({ session, presence: answer.value });
            }

            return true;
        },
        [session, transport],
    );

    const presence = standing?.presence ?? null;

    return presence === null ? null : { presence, choose };
}
