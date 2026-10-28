// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { RequestsInFlight } from '../deployment/requestsInFlight';

// When this client asks its server whether it is still there, and what it concludes from the answers.
//
// A dead link and a slow server look the same from inside a request: nothing has come back. Bounding the request would
// tell them apart only by cutting the slow ones too — a reply an AI provider is drafting, a folder act against a mail
// server, an attachment moving at the speed of the link — and no constant short enough to catch a dead link is long
// enough for the ceiling an operator configured. So what is bounded is a separate question: the probe, which asks
// something cheap and gives up on it after `livenessBound`.
//
// It asks at two moments. When the signal channel drops, because SignalR's keep-alive has just said the link carried
// nothing for thirty seconds. And every {@link probeAfter} while a request has been in flight that long, which covers a
// server with no channel at all. One probe nobody answered says little — a link saturated by an upload can hold a probe
// up on a connection that is alive — so it takes {@link missesBeforeLost} in a row before the server is called lost.
//
// While the server is lost this asks nothing. Reaching for it again is the frame's own schedule in `useConnection.ts`,
// whose reads are probes too, and two schedules for one question are what `reconnection.ts` exists to rule out.

/** How long a request waits before the server is asked whether it is still there, and how far apart two probes are. */
export const probeAfter = 20_000;

/** How many probes in a row must go unanswered before the server is lost. */
export const missesBeforeLost = 2;

/** What one probe came to: any answer at all says the server is there, and only silence says it may not be. */
export type ProbeOutcome = 'answered' | 'missed';

/** The schedule, as the frame drives it. */
export interface LivenessProbe {
    /** The signal channel dropped, so the server is asked at once. */
    readonly channelDropped: () => void;

    /** Whether the server is lost, during which nothing is asked here. Coming back starts the count again. */
    readonly lost: (isLost: boolean) => void;

    /** Stops asking, for good. */
    readonly stop: () => void;
}

/**
 * Asks the server whether it is still there on the schedule above.
 *
 * @param inFlight The requests whose waiting is what prompts a probe.
 * @param ask Puts one probe on the wire and says whether anything answered it.
 * @param gaveUp Told once the probes went unanswered {@link missesBeforeLost} times in a row, with when the first of
 * them went out.
 */
export function probeLiveness(
    inFlight: RequestsInFlight,
    ask: () => Promise<ProbeOutcome>,
    gaveUp: (since: number) => void,
): LivenessProbe {
    let stopped = false;
    let isLost = false;
    let asking = false;
    let missed = 0;
    let firstMissedAt: number | null = null;
    let lastAskedAt: number | null = null;
    let timer: ReturnType<typeof setTimeout> | undefined;

    const dueAt = (): number | null => {
        // A probe that went unanswered is followed by the next one a fixed interval after it, whatever is in flight: the
        // link is in question now, and a server with nothing pending would otherwise never be asked a second time.
        if (missed > 0 && lastAskedAt !== null) {
            return lastAskedAt + probeAfter;
        }

        const oldest = inFlight.oldest();

        if (oldest === null) {
            return null;
        }

        return Math.max(oldest + probeAfter, lastAskedAt === null ? 0 : lastAskedAt + probeAfter);
    };

    const schedule = (): void => {
        clearTimeout(timer);
        timer = undefined;

        if (stopped || isLost || asking) {
            return;
        }

        const due = dueAt();

        if (due === null) {
            return;
        }

        timer = setTimeout(
            () => {
                timer = undefined;

                // Asked again at the moment rather than trusted from when the timer was armed: the request that was
                // waiting may have been answered since, and one that has not waited long enough is no reason to ask.
                const oldest = inFlight.oldest();

                if (missed === 0 && (oldest === null || Date.now() - oldest < probeAfter)) {
                    schedule();

                    return;
                }

                void probe();
            },
            Math.max(0, due - Date.now()),
        );
    };

    const probe = async (): Promise<void> => {
        if (stopped || isLost || asking) {
            return;
        }

        clearTimeout(timer);
        timer = undefined;
        asking = true;
        lastAskedAt = Date.now();

        const askedAt = lastAskedAt;
        const outcome = await ask();

        asking = false;

        if (stopped || isLost) {
            return;
        }

        if (outcome === 'answered') {
            missed = 0;
            firstMissedAt = null;
        } else {
            missed += 1;
            firstMissedAt ??= askedAt;
        }

        if (missed >= missesBeforeLost) {
            const since = firstMissedAt ?? askedAt;

            missed = 0;
            firstMissedAt = null;
            isLost = true;
            gaveUp(since);

            return;
        }

        schedule();
    };

    const stopListening = inFlight.listen(schedule);

    return {
        channelDropped: () => {
            void probe();
        },
        lost: (nowLost) => {
            if (nowLost === isLost) {
                return;
            }

            isLost = nowLost;
            missed = 0;
            firstMissedAt = null;
            schedule();
        },
        stop: () => {
            stopped = true;
            clearTimeout(timer);
            stopListening();
        },
    };
}
