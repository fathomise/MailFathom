// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { HttpTransportType, HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import {
    livenessBound,
    signalMethod,
    type MailFathomSignalChannel,
    type SignalStreamSchedule,
} from '@mailfathom/client-backend';

// The socket `Client.Backend` asks its caller for. That package declares no DOM, so the SignalR client is named in this
// directory and nowhere else — the same boundary `deployment/sendToDeployment.ts` holds for `fetch`, and for the same
// reason: what a connection *is* belongs to the application, and when it opens, what it presents, what a payload has
// to be, and how long it waits before opening again belong to the package.
//
// Nothing here reads a payload. Whatever the deployment sent is handed over as it arrived, because the one place a
// value from the wire is checked is the package that publishes the type it becomes.

/**
 * Opens one connection to the deployment's signal hub.
 *
 * **WebSockets alone, and no negotiation.** SignalR would otherwise open with an HTTP request that chooses a transport,
 * which is a second request carrying the same ticket — and a ticket opens exactly one connection, so the negotiation
 * would spend it and the socket that followed would be refused. Skipping it is permitted precisely because one
 * transport is named, and a deployment behind a proxy that will not pass the upgrade therefore fails to connect rather
 * than falling back to long polling: the client refreshes on its own interval, which is what it does anyway.
 *
 * Logging is off because the deployment already records what it refused, and a client that logged its own connection
 * attempts would write the address it presented a ticket to into a browser console.
 *
 * **Opening is bounded; an open connection needs no bound of its own.** SignalR's keep-alive closes a connection that
 * has heard nothing for thirty seconds, with the defaults both this client and the hub keep. Opening one has no such
 * end, so a socket handshake on a link that died would wait for as long as the browser does — and could still be
 * waiting after the network came back, on a connection nothing will ever answer. So a start not done within
 * `livenessBound` is stopped, and refused like any connection that did not open.
 */
export const openSignalChannel: MailFathomSignalChannel = async (opening) => {
    const connection = new HubConnectionBuilder()
        .withUrl(opening.url, { transport: HttpTransportType.WebSockets, skipNegotiation: true })
        .configureLogging(LogLevel.None)
        .build();

    connection.on(signalMethod, (payload: unknown) => {
        opening.arrived(payload);
    });

    connection.onclose(() => {
        opening.dropped();
    });

    await startedWithin(
        () => connection.start(),
        () => connection.stop(),
        livenessBound,
    );

    return { close: () => connection.stop() };
};

/**
 * Starts something and gives up on it if it has not started within the bound, stopping it as it does.
 *
 * @param start What is started.
 * @param stop What abandons a start still under way.
 * @param milliseconds How long the start may take.
 * @throws What the start threw, or a `TimeoutError` once the bound passed — after which whatever the start does is let
 * go of, since nobody is waiting on it any more.
 */
export async function startedWithin(
    start: () => Promise<void>,
    stop: () => Promise<void>,
    milliseconds: number,
): Promise<void> {
    let timer: ReturnType<typeof setTimeout> | undefined;

    const overstayed = new Promise<never>((_resolve, reject) => {
        timer = setTimeout(() => {
            void stop().catch(() => undefined);
            reject(new DOMException('The connection did not open within its bound.', 'TimeoutError'));
        }, milliseconds);
    });

    const starting = start();

    // A start that settles after the bound has nobody to tell, so its own outcome is caught rather than left unhandled.
    starting.catch(() => undefined);

    try {
        await Promise.race([starting, overstayed]);
    } finally {
        clearTimeout(timer);
    }
}

/**
 * How the stream waits, which is the browser's own timer and the browser's own source of a spread.
 *
 * A wait also ends the moment the browser gives a reason to think the deployment is reachable again — its network
 * coming back, or the window coming back to the front — so a client somebody returns to tries at once rather than
 * sitting out the rest of a thirty-second wait it began while nobody was looking.
 */
export const browserSchedule: SignalStreamSchedule = {
    wait: (milliseconds) =>
        new Promise((resolve) => {
            function over(): void {
                window.clearTimeout(waiting);
                window.removeEventListener('online', over);
                document.removeEventListener('visibilitychange', returned);
                resolve();
            }

            function returned(): void {
                if (document.visibilityState === 'visible') {
                    over();
                }
            }

            const waiting = window.setTimeout(over, milliseconds);
            window.addEventListener('online', over);
            document.addEventListener('visibilitychange', returned);
        }),
    draw: () => Math.random(),
};
