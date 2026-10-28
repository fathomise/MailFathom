// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import type { SignalledChanges } from '../signals/signalledChanges';
import type { ChannelStep } from '../signals/useSignals';
import { nextChangeIn, readConnection, type ConnectionEvidence, type ConnectionStatus } from './connectionStatus';
import type { Connection } from './useConnection';

/**
 * The channel as the stream last reported it, for the sign-in it was held for.
 *
 * `since` is `null` between a channel being held and the stream reporting that it has begun opening, which is the
 * same commit: the stream opens in an effect of that render.
 */
interface HeldChannel {
    readonly heldFor: string | null;
    readonly stoodFor: string | null;
    readonly standing: ChannelStep | null;
    readonly since: number | null;
    readonly hasStood: boolean;
}

/** When each account was first read as unreachable, by its identifier, for as long as it has stayed so. */
type FirstUnreachable = ReadonlyMap<string, number>;

/**
 * How long from now until the evidence could read differently, against the real clock.
 *
 * Read back rather than parsed: what it is handed was written by the hook below out of values it built.
 */
function waitForNextChange(evidenceNamed: string): number | null {
    return nextChangeIn(JSON.parse(evidenceNamed) as ConnectionEvidence, Date.now());
}

/**
 * Gathers what the connection status is derived from, and derives it.
 *
 * Every instant is taken where the thing it dates happened — the stream reporting a step, a read answering or going
 * unanswered, the network leaving — and never while rendering, so a grace period runs from the moment the client
 * learned of something rather than from whenever a render happened to read the clock. The one clock this hook holds
 * is when it last looked, which a timer moves at the instant a grace period ends.
 *
 * @param signedIn What names this sign-in, or `null` where nobody is signed in. A new one starts every count again.
 * @param connection What the session read last answered, and whether it went unanswered.
 * @param channelHeld Whether a signal channel is held at all; a credential that hears nothing over one holds none.
 * @param changes How every screen is read again.
 * @returns The status, and what the signal stream reports each of its steps to.
 */
export function useConnectionWatch(
    signedIn: string | null,
    connection: Connection,
    channelHeld: boolean,
    changes: SignalledChanges,
): { readonly status: ConnectionStatus; readonly channelStood: (step: ChannelStep) => void } {
    const [clock, setClock] = useState(() => Date.now());
    const heldFor = channelHeld ? signedIn : null;
    const [channel, setChannel] = useState<HeldChannel>({
        heldFor: null,
        stoodFor: null,
        standing: null,
        since: null,
        hasStood: false,
    });

    // A channel held for a new sign-in starts at its first opening. One held again for a sign-in it has already stood
    // for — the network came back, so the stream was opened again — is not a start-up, and is not grey again.
    if (channel.heldFor !== heldFor) {
        setChannel({
            heldFor,
            stoodFor: channel.stoodFor,
            standing: null,
            since: null,
            hasStood: heldFor !== null && channel.stoodFor === heldFor,
        });
    }

    // When the network went, taken from the event that said so. A machine that started without one has no event, and
    // its status is dated from when this client first looked.
    const [offlineSince, setOfflineSince] = useState<number | null>(null);

    useEffect(() => {
        const went = (): void => {
            setOfflineSince(Date.now());
        };
        const came = (): void => {
            setOfflineSince(null);
        };

        window.addEventListener('offline', went);
        window.addEventListener('online', came);

        return () => {
            window.removeEventListener('offline', went);
            window.removeEventListener('online', came);
        };
    }, []);

    // The server not answering, whichever read said so: the session read, or a ticket nothing answered.
    const ticketUnanswered = heldFor !== null && channel.standing === 'unanswered' ? channel.since : null;
    const unansweredSince = connection.unansweredSince ?? ticketUnanswered;
    const unanswered = unansweredSince !== null;

    // Whether this is the change from not answering to answering again is kept beside it, since that is the moment
    // every screen is read again — and whether the frame's own read went unanswered in the silence, since its answer is
    // then what ended it and the frame has nothing to read twice.
    const frameQuiet = connection.unansweredSince !== null;
    const [quiet, setQuiet] = useState({ what: unanswered, answeredAgain: false, frameWasQuiet: frameQuiet });

    if (quiet.what !== unanswered) {
        setQuiet({
            what: unanswered,
            answeredAgain: !unanswered,
            frameWasQuiet: unanswered ? frameQuiet : quiet.frameWasQuiet,
        });
    } else if (unanswered && frameQuiet && !quiet.frameWasQuiet) {
        setQuiet({ ...quiet, frameWasQuiet: true });
    }

    const accounts = connection.accounts?.outcome === 'read' ? connection.accounts.value.accounts : [];
    const unreachable = accounts.filter((account) => account.synchronizationState === 'Unreachable');
    const [firstUnreachable, setFirstUnreachable] = useState<FirstUnreachable>(new Map());

    // An account that left the state and came back has started again; one still in it keeps when it was first seen,
    // which is when the accounts answered with it.
    const unreachableNamed = unreachable.map(({ id }) => id).join('\n');
    const unreachableHeld = [...firstUnreachable.keys()].join('\n');

    if (unreachableNamed !== unreachableHeld) {
        const readAt = connection.accountsReadAt ?? clock;

        setFirstUnreachable(new Map(unreachable.map(({ id }) => [id, firstUnreachable.get(id) ?? readAt])));
    }

    const evidence: ConnectionEvidence = {
        offlineSince: connection.online ? null : (offlineSince ?? clock),
        unansweredSince: connection.online ? unansweredSince : null,
        sessionAnswered: connection.session !== null,
        channel:
            heldFor === null
                ? null
                : {
                      standing: channel.standing === 'opening' ? null : channel.standing,
                      since: channel.since ?? clock,
                      hasStood: channel.hasStood,
                  },
        unreachableAccounts: unreachable.flatMap(({ id, displayName }) => {
            const since = firstUnreachable.get(id);

            return since === undefined ? [] : [{ account: displayName, since }];
        }),
    };

    // The clock moves at the instant a grace period ends, so amber arrives when it is due rather than at the next
    // unrelated render. The wait is measured against the real clock here, where reading it is what an effect is for,
    // and is measured again each time the clock moves, since a second grace period may still be running.
    const evidenceNamed = JSON.stringify(evidence);

    useEffect(() => {
        const waitFor = waitForNextChange(evidenceNamed);

        if (waitFor === null) {
            return;
        }

        const waiting = window.setTimeout(() => {
            setClock(Date.now());
        }, waitFor);

        return () => {
            window.clearTimeout(waiting);
        };
    }, [evidenceNamed, clock]);

    // The read made once a lost server answers again, which is every screen rather than the frame alone: what each of
    // them drew may have moved while nothing could say so. It waits on the server answering, never on the channel, so a
    // server that carries no channel at all is read again as readily as one that does.
    useEffect(() => {
        if (quiet.answeredAgain) {
            changes.refresh(quiet.frameWasQuiet ? { frameAnswered: true } : undefined);
        }
    }, [quiet, changes]);

    // Read when the stream reports rather than captured, so the callback stays one function for the stream's life.
    const { refresh: refreshFrame, channelDropped } = connection;
    const frameUnanswered = useRef({ unanswered: connection.unansweredSince !== null, refreshFrame, channelDropped });

    useEffect(() => {
        frameUnanswered.current = { unanswered: connection.unansweredSince !== null, refreshFrame, channelDropped };
    });

    const channelStood = useCallback((step: ChannelStep) => {
        const at = Date.now();

        setChannel((previous) => {
            if (step === 'opening') {
                return { ...previous, standing: previous.standing ?? 'opening', since: previous.since ?? at };
            }

            const falters = step !== 'opened';
            const faltering = previous.standing !== 'opened';

            // The first opening's grace runs from when it began, and a channel already not standing keeps the instant
            // it stopped: neither a second refusal nor a drop during the first opening starts it again.
            return {
                heldFor: previous.heldFor,
                stoodFor: falters ? previous.stoodFor : previous.heldFor,
                standing: step,
                since: falters && faltering && previous.since !== null ? previous.since : at,
                hasStood: previous.hasStood || !falters,
            };
        });

        // A ticket was answered, so a server whose last read went unanswered is there again: the frame is read, and
        // its answering is what reads every other screen.
        const frame = frameUnanswered.current;

        if (frame.unanswered && (step === 'opened' || step === 'refused')) {
            frame.refreshFrame();
        }

        // SignalR's keep-alive dropped a connection that heard nothing for thirty seconds, which is the first sign of a
        // link that died without the browser noticing: the server is asked at once whether it is still there.
        if (step === 'dropped') {
            frame.channelDropped();
        }
    }, []);

    // The details are about a delay or a lost server, so they are open only while there is one to say.
    const [detailsAsked, setDetailsAsked] = useState(false);
    const reading = readConnection(evidence, clock);
    const status: ConnectionStatus = {
        ...reading,
        attempts: connection.unansweredSince === null ? null : connection.attempts,
        retry: () => {
            setDetailsAsked(false);
            changes.refresh();
        },
        detailsOpen: detailsAsked && (reading.level === 'delayed' || reading.level === 'disconnected'),
        showDetails: setDetailsAsked,
    };

    return { status, channelStood };
}
