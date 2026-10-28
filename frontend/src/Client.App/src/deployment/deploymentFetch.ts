// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { livenessBound, retryDelay } from '@mailfathom/client-backend';
import { requestsInFlight, requestsInFlightOver, type RequestsInFlight } from './requestsInFlight';

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

/** A signal abandoned once that many milliseconds have passed, and the way to let the timer go sooner. */
export type Bounding = (milliseconds: number) => { readonly signal: AbortSignal; readonly release: () => void };

/** How one request is put on the wire, beyond what the request itself says. */
export interface RequestHandling {
    /**
     * Whether the caller sends this request again on a schedule of its own, in which case it is put on the wire once —
     * see `ClientRequest.retriedByCaller`.
     */
    readonly retriedByCaller?: boolean;

    /**
     * Whether it asks if the server is still there — see `ClientRequest.probes`. It takes no place among
     * {@link mostRequestsInFlight}, is put on the wire once, is given up after `livenessBound`, and is not one of the
     * requests a probe asks about.
     */
    readonly probes?: boolean;
}

/**
 * Puts a request on the wire within the client's share of the deployment, and hands the answer to whoever reads it.
 *
 * @param path Where the request goes.
 * @param init The request itself; its body is sent again unchanged when the request is retried, which is what every
 * body this client sends — a string, a `Blob`, or nothing — allows.
 * @param answered Reads the answer that is final, and calls `moved` as its body arrives, which is what keeps a long
 * download alive however the probe concludes. The request keeps its place among those in flight until this settles,
 * because the deployment counts a request until its answer has been sent in full.
 * @param handling How it is put on the wire beyond what it says itself.
 * @returns What `answered` returned.
 * @throws What `fetch` throws — a network failure that outlasted the retries, or an abandoned request — which every
 * caller already reads as nothing having answered. A request a probe gave up on is one of those: whoever made it reads
 * it exactly as a link that failed under it, which is what it was.
 */
export type DeploymentFetch = <TAnswer>(
    path: string,
    init: RequestInit,
    answered: (response: Response, moved: () => void) => Promise<TAnswer>,
    handling?: RequestHandling,
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
 * @param entering Where every request but a probe is entered, which is what a probe that went unanswered abandons.
 * @param bounding How a probe's bound is kept.
 */
export function deploymentFetchOver(
    fetching: Fetching,
    pausing: Pausing,
    drawing: () => number,
    most: number = mostRequestsInFlight,
    watching: RequestWatcher = () => unwatched,
    entering: RequestsInFlight = requestsInFlightOver(() => Date.now()),
    bounding: Bounding = boundBy,
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

    return async (path, init, answered, handling = {}) => {
        const method = init.method ?? 'GET';
        const watch = watching(path, init);
        const probes = handling.probes === true;
        const retriedByCaller = probes || handling.retriedByCaller === true;

        // A probe is bounded and takes no place, so six requests a dead link left hanging cannot hold back the question
        // whether the link is dead. Every other request is entered where that question can be asked about it, and is
        // abandoned through the same signal its caller holds — a wait before a retry included, so no retry is put on a
        // dead link a second after the probe gave up on it.
        const bound = probes ? bounding(livenessBound) : null;
        const entered = probes ? null : entering.enter(method.toUpperCase() !== 'GET', init.body instanceof Blob);
        const signal = eitherOf(init.signal, bound?.signal ?? entered?.signal);
        const sending: RequestInit = { ...init, signal };
        const moved = (): void => {
            entered?.moved();
        };

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

                if (!probes) {
                    await acquire(signal);
                }

                const releasing = (): void => {
                    if (!probes) {
                        release();
                    }
                };

                watch.sent(made);

                let response: Response;

                try {
                    response = await fetching(path, sending);
                } catch (failure) {
                    releasing();

                    // An abandoned request is the caller's act rather than a failure, so it ends the attempts at once.
                    const delay = signal?.aborted === true ? null : delayAfter(null);

                    if (delay === null) {
                        throw failure;
                    }

                    watch.retrying(delay);
                    await pausing(delay, signal);

                    continue;
                }

                watch.answered(response);

                const delay = delayAfter(response);

                if (delay === null) {
                    // The answer that will be read has begun, so from here its bytes are what say it is alive. One
                    // the transport is about to wait out says nothing of the kind.
                    entered?.answered();

                    try {
                        const answer = await answered(response, moved);

                        watch.ended('read');

                        return answer;
                    } finally {
                        releasing();
                    }
                }

                // The place is given up for the wait, so what the deployment is refusing this request for is not also
                // what holds back every other request this client has to make.
                releasing();
                void response.body?.cancel();
                watch.retrying(delay);
                await pausing(delay, signal);
            }
        } catch (failure) {
            watch.ended(signal?.aborted === true ? 'abandoned' : 'failed');

            throw failure;
        } finally {
            entered?.left();
            bound?.release();
        }
    };
}

/** The signal that is abandoned when either of the two is, or the one there is. */
function eitherOf(first: AbortSignal | null | undefined, second: AbortSignal | null | undefined): AbortSignal | null {
    const signals = [first, second].filter((signal): signal is AbortSignal => signal !== null && signal !== undefined);

    if (signals.length < 2) {
        return signals[0] ?? null;
    }

    return AbortSignal.any(signals);
}

/** A bound kept by the browser's own timer. */
const boundBy: Bounding = (milliseconds) => {
    const bounding = new AbortController();
    const timer = setTimeout(() => {
        bounding.abort(new DOMException('The probe was not answered within its bound.', 'TimeoutError'));
    }, milliseconds);

    return {
        signal: bounding.signal,
        release: () => {
            clearTimeout(timer);
        },
    };
};

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
    requestsInFlight,
);
