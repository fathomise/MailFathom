// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { retryDelay } from '@mailfathom/client-backend';

// The one place in this client a request to the deployment is put on the wire. The four modules in this directory that
// call `fetch` each read a different kind of answer, and they share what happens before any answer is read: how many
// requests this client has in flight at once, and what it does about a failure that passes on its own.
//
// The first is this client keeping to the deployment's own limit rather than discovering it. The client endpoint allows
// one user a handful of requests at once and refuses the rest outright, and every space this client draws reads what it
// shows the moment it is mounted — so a client that put all of them on the wire together would be refused for most of
// them on every sign-in, and each refusal would reach a screen as a deployment that did not answer. The second is the
// one policy `transientFailures.ts` in `Client.Backend` states, applied here so no adapter and no screen keeps its own.

/**
 * How many requests this client has on the wire at once.
 *
 * Six is what a browser already allows itself on one connection to an origin, and it sits below the eight a
 * deployment allows one user by default, so a second window signed in as the same person still has room beside it.
 * A deployment configured narrower than that is met by the retry policy rather than by a smaller number here: this
 * client cannot know what a deployment was configured with, and does not need to.
 */
export const mostRequestsInFlight = 6;

/** Puts one request on the wire, as `fetch` does. */
export type Fetching = (path: string, init: RequestInit) => Promise<Response>;

/** Waits the given number of milliseconds, and rejects as soon as the signal abandons the wait. */
export type Pausing = (milliseconds: number, abandoned: AbortSignal | null | undefined) => Promise<void>;

/**
 * Puts a request on the wire within the client's share of the deployment, and hands the answer to whoever reads it.
 *
 * @param path Where the request goes.
 * @param init The request itself; its body is sent again unchanged when the request is retried, which is what every
 * body this client sends — a string, a `Blob`, or nothing — allows.
 * @param answered Reads the answer that is final. The request keeps its place among those in flight until this
 * settles, because the deployment counts a request until its answer has been sent in full.
 * @param retriedByCaller Whether the caller sends this request again on a schedule of its own, in which case it is put
 * on the wire once — see `ClientRequest.retriedByCaller`.
 * @returns What `answered` returned.
 * @throws What `fetch` throws — a network failure that outlasted the retries, or an abandoned request — which every
 * caller already reads as nothing having answered.
 */
export type DeploymentFetch = <TAnswer>(
    path: string,
    init: RequestInit,
    answered: (response: Response) => Promise<TAnswer>,
    retriedByCaller?: boolean,
) => Promise<TAnswer>;

/**
 * What one request says about itself as it moves through the transport: waiting for a place, on the wire, waiting out a
 * retry, and how it ended. The developer information surface is what listens, and it is told nothing about a body.
 */
export interface RequestWatch {
    /** The request took a place and went on the wire, as the attempt counted from zero. */
    readonly sent: (made: number) => void;

    /** An answer arrived, which is read next unless it is one the transport retries. */
    readonly answered: (response: Response) => void;

    /** The transport is waiting that long before the next attempt. */
    readonly retrying: (milliseconds: number) => void;

    /** The request is over: its answer was read, it failed, or whoever made it abandoned it. */
    readonly ended: (outcome: 'read' | 'failed' | 'abandoned') => void;
}

/** Begins watching one request, at the moment it is asked for and before it has a place on the wire. */
export type RequestWatcher = (path: string, init: RequestInit) => RequestWatch;

/** What a request reports to while nothing is listening, which is nowhere. */
export const unwatched: RequestWatch = {
    sent: () => undefined,
    answered: () => undefined,
    retrying: () => undefined,
    ended: () => undefined,
};

let watchingTheWire: RequestWatcher = () => unwatched;

/** Names what is told where each request the application makes has got to, which the composition root does once. */
export function watchRequestsWith(watcher: RequestWatcher): void {
    watchingTheWire = watcher;
}

/**
 * Composes the one way this client reaches its deployment out of the three things it cannot decide for itself.
 *
 * @param fetching How one request goes on the wire.
 * @param pausing How a wait before a retry is spent.
 * @param drawing Where the spread between retries comes from, a value in `[0, 1)`.
 * @param most How many requests may be in flight at once.
 * @param watching What is told where each request has got to.
 */
export function deploymentFetchOver(
    fetching: Fetching,
    pausing: Pausing,
    drawing: () => number,
    most: number = mostRequestsInFlight,
    watching: RequestWatcher = () => unwatched,
): DeploymentFetch {
    let inFlight = 0;
    const waiting: (() => void)[] = [];

    // A place is handed straight to the request waiting longest rather than given back and taken again, so a request
    // arriving in between cannot overtake one that has been waiting.
    const release = (): void => {
        const next = waiting.shift();

        if (next === undefined) {
            inFlight -= 1;
        } else {
            next();
        }
    };

    const acquire = (abandoned: AbortSignal | null | undefined): Promise<void> => {
        if (inFlight < most) {
            inFlight += 1;

            return Promise.resolve();
        }

        return new Promise((resolve, reject) => {
            if (abandoned?.aborted === true) {
                reject(abandonment());

                return;
            }

            // A request abandoned while it waits gives up its turn rather than keeping it: a turn nobody takes would be
            // a place in flight nobody ever releases.
            const gaveUp = (): void => {
                waiting.splice(waiting.indexOf(granted), 1);
                reject(abandonment());
            };

            const granted = (): void => {
                abandoned?.removeEventListener('abort', gaveUp);
                resolve();
            };

            abandoned?.addEventListener('abort', gaveUp, { once: true });
            waiting.push(granted);
        });
    };

    return async (path, init, answered, retriedByCaller = false) => {
        const method = init.method ?? 'GET';
        const watch = watching(path, init);

        try {
            for (let made = 0; ; made += 1) {
                const delayAfter = (answer: Response | null): number | null =>
                    retriedByCaller
                        ? null
                        : retryDelay(
                              method,
                              answer === null
                                  ? null
                                  : { status: answer.status, headers: Object.fromEntries(answer.headers) },
                              made,
                              drawing(),
                          );

                await acquire(init.signal);
                watch.sent(made);

                let response: Response;

                try {
                    response = await fetching(path, init);
                } catch (failure) {
                    release();

                    // An abandoned request is the caller's act rather than a failure, so it ends the attempts at once.
                    const delay = init.signal?.aborted === true ? null : delayAfter(null);

                    if (delay === null) {
                        throw failure;
                    }

                    watch.retrying(delay);
                    await pausing(delay, init.signal);

                    continue;
                }

                watch.answered(response);

                const delay = delayAfter(response);

                if (delay === null) {
                    try {
                        const answer = await answered(response);

                        watch.ended('read');

                        return answer;
                    } finally {
                        release();
                    }
                }

                // The place is given up for the wait, so what the deployment is refusing this request for is not also
                // what holds back every other request this client has to make.
                release();
                void response.body?.cancel();
                watch.retrying(delay);
                await pausing(delay, init.signal);
            }
        } catch (failure) {
            watch.ended(init.signal?.aborted === true ? 'abandoned' : 'failed');

            throw failure;
        }
    };
}

/** What a request abandoned before it reached the wire rejects with, which is what an abandoned `fetch` rejects with. */
function abandonment(): DOMException {
    return new DOMException('The request was abandoned before it was answered.', 'AbortError');
}

const pauseFor: Pausing = (milliseconds, abandoned) =>
    new Promise((resolve, reject) => {
        if (abandoned?.aborted === true) {
            reject(abandonment());

            return;
        }

        const stopped = (): void => {
            clearTimeout(waited);
            reject(abandonment());
        };

        const waited = setTimeout(() => {
            abandoned?.removeEventListener('abort', stopped);
            resolve();
        }, milliseconds);

        abandoned?.addEventListener('abort', stopped, { once: true });
    });

// `fetch` is reached when a request is made rather than captured when this module loads, so what a test watches on the
// global is what this calls.
export const fetchFromDeployment: DeploymentFetch = deploymentFetchOver(
    (path, init) => fetch(path, init),
    pauseFor,
    Math.random,
    mostRequestsInFlight,
    (path, init) => watchingTheWire(path, init),
);
