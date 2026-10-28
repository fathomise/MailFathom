// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The requests this client has asked its deployment for and has not yet heard the end of, as the liveness probe reads
// them. It is the one thing a dead link and a slow server share — a request with no answer — and what tells them apart
// is asked elsewhere: `shell/livenessProbe.ts` asks the server whether it is still there, and only once it has twice not
// answered is anything here abandoned.
//
// So nothing here bounds a request. A reply drafted by an AI provider, a folder act against a mail server, and an
// attachment moving at the speed of the link all legitimately take minutes, and the server ends a client request at a
// ceiling its operator chose and this client cannot see. What is held is only when each request began, whether it
// writes, and when its body last moved — enough to say which requests have been waiting long enough to ask about, and
// which of them are still visibly alive and are left to end on their own terms.

/**
 * How recently a request's body must have moved for it to count as alive whatever the probe concluded.
 *
 * The probe's own bound, because the question is the same: an answer whose bytes arrived within the last fifteen
 * seconds is a connection that carried something within the time a probe was given to cross it.
 */
export const movingWithin = 15_000;

/** One request as the transport enters it: what abandons it, and what it reports as it goes. */
export interface EnteredRequest {
    /** Abandoned when a probe that went unanswered twice gives up on every request that is not moving. */
    readonly signal: AbortSignal;

    /** The answer has begun to arrive, so from here its bytes are what say it is alive. */
    readonly answered: () => void;

    /** Some of the body arrived. */
    readonly moved: () => void;

    /** The request is over, however it ended. */
    readonly left: () => void;
}

/** What the probe reads off the requests in flight, and the one thing it does to them. */
export interface RequestsInFlight {
    /**
     * Enters a request as it is asked for.
     *
     * @param writes Whether it asks the deployment to change something, which is what *Still sending…* is said about.
     * @param uploads Whether its body is a file on its way out. `fetch` reports nothing about a body being sent, so such
     * a request counts as moving until its answer begins rather than being abandoned under somebody still uploading.
     */
    readonly enter: (writes: boolean, uploads: boolean) => EnteredRequest;

    /** When the longest-waiting request began, or `null` while none is in flight. */
    readonly oldest: () => number | null;

    /** When the longest-waiting write began, or `null` while none is in flight. */
    readonly oldestWrite: () => number | null;

    /** Abandons every request whose body has not moved within {@link movingWithin}. */
    readonly abandonStill: () => void;

    /**
     * Listens for a request entering or leaving.
     *
     * @returns How to stop listening, which is what an effect returns.
     */
    readonly listen: (changed: () => void) => () => void;
}

interface Held {
    readonly writes: boolean;
    readonly uploads: boolean;
    readonly since: number;
    readonly abandon: () => void;
    answeredAt: number | null;
    movedAt: number | null;
}

/**
 * Holds the requests in flight against the given clock.
 *
 * @param now The clock, which a test names rather than waits out.
 */
export function requestsInFlightOver(now: () => number): RequestsInFlight {
    const held = new Set<Held>();
    const listeners = new Set<() => void>();

    const told = (): void => {
        for (const listener of [...listeners]) {
            listener();
        }
    };

    const moving = (request: Held, at: number): boolean => {
        if (request.uploads && request.answeredAt === null) {
            return true;
        }

        return request.movedAt !== null && at - request.movedAt < movingWithin;
    };

    const earliest = (requests: Iterable<Held>): number | null => {
        let first: number | null = null;

        for (const request of requests) {
            first = first === null ? request.since : Math.min(first, request.since);
        }

        return first;
    };

    return {
        enter: (writes, uploads) => {
            const abandoning = new AbortController();
            const request: Held = {
                writes,
                uploads,
                since: now(),
                abandon: () => {
                    abandoning.abort(new DOMException('The server did not answer a liveness probe.', 'AbortError'));
                },
                answeredAt: null,
                movedAt: null,
            };

            held.add(request);
            told();

            return {
                signal: abandoning.signal,
                answered: () => {
                    request.answeredAt = now();
                    request.movedAt = request.answeredAt;
                },
                moved: () => {
                    request.movedAt = now();
                },
                left: () => {
                    if (held.delete(request)) {
                        told();
                    }
                },
            };
        },
        oldest: () => earliest(held),
        oldestWrite: () => earliest([...held].filter((request) => request.writes)),
        abandonStill: () => {
            const at = now();

            for (const request of [...held]) {
                if (!moving(request, at)) {
                    request.abandon();
                }
            }
        },
        listen: (changed) => {
            listeners.add(changed);

            return () => {
                listeners.delete(changed);
            };
        },
    };
}

/** The requests this client has in flight, which every request the application makes is entered in. */
export const requestsInFlight: RequestsInFlight = requestsInFlightOver(() => Date.now());
