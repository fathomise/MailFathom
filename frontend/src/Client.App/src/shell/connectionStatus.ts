// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type { ClientFailureReason, SignalStanding } from '@mailfathom/client-backend';

// Whether this client is talking to its server, said once for the whole client rather than by every screen in its own
// words. It is one value derived from four kinds of evidence — the network, the session read, the signal channel, and
// the account list — and the derivation is a function of that evidence and an instant, so every threshold in it is
// proven by naming an instant rather than by waiting one out.

/** How long a channel may be opening, or reopening, before it counts as not standing. A blink is never a delay. */
export const channelGrace = 10_000;

/** How long an account's mail server must have been unreachable, without a break, before it delays the client. */
export const mailServerGrace = 60_000;

/** The four things the status can say, in the order a client meets them. */
export type ConnectionLevel = 'connecting' | 'online' | 'delayed' | 'disconnected';

/** Why the server is out of reach: this machine has no network, or the server did not answer it. */
export type DisconnectionCause = 'offline' | 'unanswered';

/** One reason what the client shows is not current although the server answers. */
export type ConnectionDelay =
    | { readonly kind: 'channel'; readonly since: number }
    | { readonly kind: 'mailServer'; readonly account: string; readonly since: number };

/** How the signal channel stands, as far as the status reads it. */
export interface ChannelEvidence {
    /** The last step the stream reported, or `null` while its first opening is still under way. */
    readonly standing: SignalStanding | null;

    /** When the channel began to be held, or last moved between standing and not standing. */
    readonly since: number;

    /** Whether it has stood once for this sign-in, which is what ends the first opening's grace early. */
    readonly hasStood: boolean;
}

/** Everything the status is derived from. */
export interface ConnectionEvidence {
    /** Whether this machine has a network, with the instant it was lost where it has none. */
    readonly offlineSince: number | null;

    /** When the server last stopped answering, or `null` while it answers. */
    readonly unansweredSince: number | null;

    /** Whether the session read has answered once for this sign-in. */
    readonly sessionAnswered: boolean;

    /** The signal channel, or `null` where this client holds none — its credential hears nothing over one. */
    readonly channel: ChannelEvidence | null;

    /** Each account whose mail server is unreachable, by the name it is known by, and since when this client saw it. */
    readonly unreachableAccounts: readonly { readonly account: string; readonly since: number }[];
}

/** What the status says, derived. */
export type ConnectionReading =
    | { readonly level: 'connecting' }
    | { readonly level: 'online' }
    | { readonly level: 'delayed'; readonly delays: readonly ConnectionDelay[] }
    | { readonly level: 'disconnected'; readonly cause: DisconnectionCause; readonly since: number };

/**
 * What the evidence says at that instant.
 *
 * Red outranks everything, because nothing else can be known while the server does not answer: the account list is not
 * read, and the channel is not opened. Connecting comes next, so a client starting up is never amber while its first
 * channel is still opening. A channel counts against the status once it has not stood for {@link channelGrace} and an
 * account once its mail server has been unreachable for {@link mailServerGrace}; amber lists every cause that holds.
 */
export function readConnection(evidence: ConnectionEvidence, now: number): ConnectionReading {
    if (evidence.offlineSince !== null) {
        return { level: 'disconnected', cause: 'offline', since: evidence.offlineSince };
    }

    if (evidence.unansweredSince !== null) {
        return { level: 'disconnected', cause: 'unanswered', since: evidence.unansweredSince };
    }

    const channel = evidence.channel;

    const firstOpening = channel !== null && !channel.hasStood && now - channel.since < channelGrace;

    if (!evidence.sessionAnswered || firstOpening) {
        return { level: 'connecting' };
    }

    const delays: ConnectionDelay[] = [];

    if (channel !== null && channel.standing !== 'opened' && now - channel.since >= channelGrace) {
        delays.push({ kind: 'channel', since: channel.since });
    }

    for (const { account, since } of evidence.unreachableAccounts) {
        if (now - since >= mailServerGrace) {
            delays.push({ kind: 'mailServer', account, since });
        }
    }

    return delays.length === 0 ? { level: 'online' } : { level: 'delayed', delays };
}

/**
 * How long until the reading could change with no new evidence, or `null` where nothing is waiting to cross a threshold.
 *
 * It is what a timer waits out, so the status turns amber the moment a grace period ends rather than at whatever
 * moment something else happened to render the frame.
 */
export function nextChangeIn(evidence: ConnectionEvidence, now: number): number | null {
    const ends: number[] = [];
    const channel = evidence.channel;

    if (channel !== null && channel.standing !== 'opened') {
        ends.push(channel.since + channelGrace);
    }

    for (const { since } of evidence.unreachableAccounts) {
        ends.push(since + mailServerGrace);
    }

    const ahead = ends.filter((end) => end > now);

    return ahead.length === 0 ? null : Math.min(...ahead) - now;
}

/** The status as every surface reads it. */
export type ConnectionStatus = ConnectionReading & {
    /**
     * How many automatic attempts at the session read have been made since the server last answered it, or `null` where
     * the session read is not what went unanswered — a ticket nothing answered is retried on the stream's own schedule.
     */
    readonly attempts: number | null;

    /** Reads everything again, which is what *Retry* and *Refresh* both ask for. */
    readonly retry: () => void;

    /**
     * Whether the details under the status are open. One answer for every place the status is drawn, because the rail's
     * alarm opens the menu onto them and the place it opens has to agree with the one that asked.
     */
    readonly detailsOpen: boolean;

    /** Opens or closes the details. */
    readonly showDetails: (open: boolean) => void;

    /**
     * Whether the server is out of reach at this moment, asked when a failure arrives rather than read off the status a
     * screen rendered with.
     *
     * A request abandoned because the server went quiet fails in the instant after the status turned red, inside a
     * callback that closed over the status as it stood when the request went out — green. Asked here instead, it
     * answers what is true now, which is what decides that the failure is the status's to say and raises no toast of
     * its own.
     */
    readonly disconnectedNow: () => boolean;
};

/** What a surface reads where nothing has said otherwise, which is a client that has not yet heard anything. */
const notYetHeard: ConnectionStatus = {
    level: 'connecting',
    attempts: null,
    retry: () => undefined,
    detailsOpen: false,
    showDetails: () => undefined,
    disconnectedNow: () => false,
};

export const ConnectionStatusContext = createContext<ConnectionStatus>(notYetHeard);

/** The status of this client's connection to its server. */
export function useConnectionStatus(): ConnectionStatus {
    return useContext(ConnectionStatusContext);
}

/** Whether the server is out of reach, which is what the surfaces that say *no connection* ask. */
export function disconnected(status: ConnectionReading): boolean {
    return status.level === 'disconnected';
}

/**
 * Whether a failure is one the status already says, so it raises nothing of its own: an unavailable read or write while
 * the server is out of reach. A screen still draws its own state in place; what it does not do is add a second voice
 * to the one the status already is.
 */
export function saidByTheStatus(disconnectedNow: () => boolean, reason: ClientFailureReason): boolean {
    return reason === 'unavailable' && disconnectedNow();
}
