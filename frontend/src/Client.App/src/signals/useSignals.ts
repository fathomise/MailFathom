// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useMemo, useRef } from 'react';
import {
    openSignalStream,
    type ClientSession,
    type MailFathomSignalChannel,
    type MailFathomTransport,
    type SignalStanding,
    type SignalStreamSchedule,
} from '@mailfathom/client-backend';
import type { Refresh, SignalledChange, SignalledChanges, SignalListener } from './signalledChanges';

/**
 * A step of keeping the channel open as the connection status hears it: the stream's own report, or `opening` when a
 * stream begins, which is when a first opening's grace starts.
 */
export type ChannelStep = SignalStanding | 'opening';

/** How long a visible client goes without reading again what it draws, counted from the last time it did. */
export const refreshInterval = 5 * 60_000;

/**
 * Holds a connection to the deployment's signal channel for as long as one credential does, and refreshes every screen
 * whenever what the connection says may not be everything that changed.
 *
 * The connection is what this effect synchronizes with, which is what an effect is for. It opens once a session
 * exists, and signing out or being pointed at another deployment closes it and opens nothing — so nothing about one
 * deployment reaches a client that has left it.
 *
 * **A channel that never opens is not a failure here.** Nothing here is rendered and nothing here fails: a deployment
 * serving no hub, a proxy that will not pass the upgrade, and a connection that dropped are one thing to a screen — a
 * client refreshing on its own interval. How the channel stands is handed to `onStood`, and the connection status is
 * where it is said.
 *
 * **What the connection could not say is caught up on instead.** Nothing buffers a statement for a connection that is
 * not there, so a gap in it is a gap in what the screens were told. A connection standing again after one stood for the
 * same session therefore refreshes every screen, and so does the interval: every five minutes while the window is
 * visible, counted from the last refresh rather than on a fixed clock. The interval is also what covers the one gap no
 * reconnect sees — a statement lost behind a connection that stayed open — and what a deployment serving no channel is
 * read on. Nothing runs while the window is hidden, and a window coming back after the interval ran out while it was
 * hidden refreshes at once.
 *
 * @param session Who is asking, or `null` where nobody is signed in, the credential may not read mail, or the machine
 * has no network.
 * @param transport How the ticket each connection is opened against is minted.
 * @param channel How a connection is opened, which is the composition root's to supply.
 * @param schedule How the stream waits before opening again.
 * @param onStood Told how the channel stands after every step of keeping it open, which is what the connection status
 * reads; nothing about it is drawn here.
 * @returns What a screen subscribes to, and the way to refresh every screen.
 */
export function useSignals(
    session: ClientSession | null,
    transport: MailFathomTransport,
    channel: MailFathomSignalChannel,
    schedule: SignalStreamSchedule,
    onStood: (step: ChannelStep) => void = () => undefined,
): SignalledChanges {
    // The listeners rather than the last statement, because a signal is an instant: a screen acts on one and there is
    // nothing left to render. A ref because nothing on the screen is drawn from it and a screen subscribing must not
    // reopen the connection.
    const listeners = useRef(new Set<SignalListener>());

    // What a refresh does, written by the effect that holds the interval a refresh starts over, and `null` whenever
    // that effect holds nothing — which is when there is nothing a refresh could read over.
    const refreshing = useRef<((change: Refresh) => void) | null>(null);

    // The session a connection last stood for. One standing again for the same session is a reopening whatever closed
    // the last — a drop, or the network going, which closes the stream and opens a new one for the same session — and
    // one standing for another is a first opening: somebody signing in, or a renewed token.
    const stoodFor = useRef<ClientSession | null>(null);

    // Read when the stream reports rather than captured by the effect, so a caller handing a new function each render
    // does not close the connection and open another.
    const onStoodNow = useRef(onStood);

    useEffect(() => {
        onStoodNow.current = onStood;
    }, [onStood]);

    const changes = useMemo<SignalledChanges>(
        () => ({
            listen: (listener) => {
                listeners.current.add(listener);

                return () => {
                    listeners.current.delete(listener);
                };
            },
            // Without a channel there is no interval to start over, but the screens are still read again: a
            // credential that hears nothing over one, and a client whose server stopped answering, are refreshed by
            // hand like any other.
            refresh: (asked) => {
                const change: Refresh = asked === undefined ? { kind: 'refresh' } : { kind: 'refresh', ...asked };

                if (refreshing.current !== null) {
                    refreshing.current(change);

                    return;
                }

                for (const listener of [...listeners.current]) {
                    listener(change);
                }
            },
        }),
        [],
    );

    useEffect(() => {
        if (session === null) {
            return;
        }

        function tell(change: SignalledChange): void {
            // A copy, because a screen that unmounts on what it was told would otherwise be removed from the set being
            // walked.
            for (const listener of [...listeners.current]) {
                listener(change);
            }
        }

        let waiting = 0;
        let due = false;

        function refresh(change: Refresh = { kind: 'refresh' }): void {
            countFromNow();
            tell(change);
        }

        function countFromNow(): void {
            window.clearTimeout(waiting);
            due = false;
            waiting = window.setTimeout(elapsed, refreshInterval);
        }

        // Due rather than run while the window is hidden, and nothing is counted until it is back: a hidden window is
        // somebody not looking, and reading for them is spending their deployment's time on a screen nobody sees.
        function elapsed(): void {
            if (document.visibilityState === 'visible') {
                refresh();
            } else {
                due = true;
            }
        }

        function returned(): void {
            if (due && document.visibilityState === 'visible') {
                refresh();
            }
        }

        refreshing.current = refresh;
        onStoodNow.current('opening');
        countFromNow();
        document.addEventListener('visibilitychange', returned);

        const stream = openSignalStream(
            session,
            transport,
            channel,
            tell,
            (standing) => {
                onStoodNow.current(standing);

                if (standing !== 'opened') {
                    return;
                }

                if (stoodFor.current === session) {
                    refresh();
                }

                stoodFor.current = session;
            },
            schedule,
        );

        return () => {
            refreshing.current = null;
            window.clearTimeout(waiting);
            document.removeEventListener('visibilitychange', returned);
            void stream.close();
        };
    }, [session, transport, channel, schedule]);

    return changes;
}
