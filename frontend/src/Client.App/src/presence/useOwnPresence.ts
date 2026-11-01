// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    choosePresence,
    chooseWorkMode,
    readOwnPresence,
    writeStatusMessage,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type OwnPresence,
    type PresenceDuration,
    type PresenceState,
    type WorkMode,
} from '@mailfathom/client-backend';
import type { SignalledChanges } from '../signals/signalledChanges';

// The signed-in member's own presence, as the rail's dot, the user menu's row, and the status menu draw it.
//
// It is read when the session starts and again whenever it may have moved: a presence change was signalled, a refresh
// was asked for, or the while the member chose, their status message, or a work mode said for today ran out — which no
// request marks, so the client that knows the instant reads again at it. A presence change reaches only the member's
// own clients, so it is always about this reader: the state they chose on another device, a client they closed, an
// out-of-office period, a status message, or a place of work they saved.
//
// A caller in no organization is answered `404`, and is drawn no presence at all, which is what the design draws for a
// reader outside one.

/** The member's presence and the ways of changing it, for a reader who is a member of an organization. */
export interface OwnPresenceInForce {
    readonly presence: OwnPresence;

    /** Chooses a state for a while, or until changed with `null`, answering `null` where the deployment took it and why it did not otherwise. */
    readonly choose: (state: PresenceState, duration: PresenceDuration | null) => Promise<ClientFailureReason | null>;

    /** Writes the status message, clearing after a while or never with `null`; nothing but spaces clears it. */
    readonly writeMessage: (text: string, clears: PresenceDuration | null) => Promise<ClientFailureReason | null>;

    /** Says where the member works from, for today alone or until changed. */
    readonly chooseWorkMode: (mode: WorkMode, today: boolean) => Promise<ClientFailureReason | null>;
}

// The soonest a read at the end of a while is asked again. A clock ahead of the deployment's reaches the end first and
// is answered with the same end still to come; each such answer arms the wait again, and this keeps a skewed clock from
// asking as fast as the platform can fire.
const shortestWaitForTheEnd = 10_000;

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

    // Each while runs out at an instant no request marks, and this client knows it, so it waits for the soonest. The
    // wait is armed again by every answer rather than by every new end, so a read that came back with the same end
    // still to come waits for it again.
    useEffect(() => {
        const held = standing?.presence;
        const ends = [held?.choiceEndsAt, held?.statusMessage?.clearsAt, held?.workMode?.endsAt]
            .filter((end): end is string => typeof end === 'string')
            .map((end) => Date.parse(end));

        if (ends.length === 0) {
            return;
        }

        const timer = setTimeout(
            () => {
                readAgain();
            },
            Math.max(Math.min(...ends) - Date.now(), shortestWaitForTheEnd),
        );

        return () => {
            clearTimeout(timer);
        };
    }, [standing, readAgain]);

    // Every write answers with the presence it left, which replaces what was drawn unless a later read or write has
    // been asked since.
    const write = useCallback(
        async (
            request: (asking: ClientSession) => Promise<ClientResult<OwnPresence>>,
        ): Promise<ClientFailureReason | null> => {
            if (session === null) {
                return 'unauthenticated';
            }

            const asked = ++latest.current;
            const answer = await request(session);

            if (answer.outcome === 'failed') {
                return answer.failure.reason;
            }

            if (asked === latest.current) {
                setHeld({ session, presence: answer.value });
            }

            return null;
        },
        [session],
    );

    const choose = useCallback(
        (state: PresenceState, duration: PresenceDuration | null) =>
            write((asking) => choosePresence(asking, transport, state, duration)),
        [write, transport],
    );

    const writeMessage = useCallback(
        (text: string, clears: PresenceDuration | null) =>
            write((asking) => writeStatusMessage(asking, transport, text, clears)),
        [write, transport],
    );

    const chooseMode = useCallback(
        (mode: WorkMode, today: boolean) => write((asking) => chooseWorkMode(asking, transport, mode, today)),
        [write, transport],
    );

    const presence = standing?.presence ?? null;

    return presence === null ? null : { presence, choose, writeMessage, chooseWorkMode: chooseMode };
}
