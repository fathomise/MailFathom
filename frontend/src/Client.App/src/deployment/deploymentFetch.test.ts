// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { mostRetries } from '@mailfathom/client-backend';
import {
    deploymentFetchOver,
    unwatched,
    type Bounding,
    type Fetching,
    type Pausing,
    type RequestWatcher,
} from './deploymentFetch';
import { requestsInFlightOver } from './requestsInFlight';

// Everything this module cannot decide arrives as a function, so a test hands over the wire, the wait, and the spread
// rather than patching `fetch` or installing a clock: what is proven is the order of attempts and the waits between
// them, which a real network and a real timer would only make slower to watch.

const throttled = (headers: Record<string, string> = {}): Response => new Response(null, { status: 429, headers });
const answered = (): Response => new Response('{}', { status: 200 });
const unavailable = (): Response => new Response(null, { status: 503 });

/** A wire whose first attempts fail as a connection refused does, and whose next one answers. */
function wireRefusingFirst(refusals: number): { fetching: Fetching; attempts: string[] } {
    const attempts: string[] = [];

    return {
        fetching: (path) => {
            attempts.push(path);

            return attempts.length <= refusals
                ? Promise.reject(new TypeError('Failed to fetch'))
                : Promise.resolve(answered());
        },
        attempts,
    };
}

/** A wire answering each attempt with the next of the given answers, and recording every path it was handed. */
function wireAnswering(...answers: readonly (() => Response)[]): { fetching: Fetching; attempts: string[] } {
    const attempts: string[] = [];

    return {
        fetching: (path) => {
            const answer = answers[Math.min(attempts.length, answers.length - 1)] ?? answered;

            attempts.push(path);

            return Promise.resolve(answer());
        },
        attempts,
    };
}

/** A wait that is over at once, and remembers how long it was asked to be. */
function waitsRecorded(): { pausing: Pausing; waits: number[] } {
    const waits: number[] = [];

    return {
        pausing: (milliseconds) => {
            waits.push(milliseconds);

            return Promise.resolve();
        },
        waits,
    };
}

const statusOf = (response: Response): Promise<number> => Promise.resolve(response.status);

describe('deploymentFetchOver', () => {
    it('puts a throttled request on the wire again and hands over the answer that followed', async () => {
        const wire = wireAnswering(throttled, answered);
        const wait = waitsRecorded();
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, wait.pausing, () => 0.5);

        const status = await fetchFromDeployment('/api/client/contacts', {}, statusOf);

        expect(status).toBe(200);
        expect(wire.attempts).toEqual(['/api/client/contacts', '/api/client/contacts']);
        expect(wait.waits).toEqual([250]);
    });

    it('hands over the refusal once the retries are spent', async () => {
        const wire = wireAnswering(throttled);
        const wait = waitsRecorded();
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, wait.pausing, () => 0.5);

        const status = await fetchFromDeployment('/api/client/contacts', {}, statusOf);

        expect(status).toBe(429);
        expect(wire.attempts).toHaveLength(mostRetries + 1);
        expect(wait.waits).toEqual([250, 500, 1_000]);
    });

    it('waits out the Retry-After the deployment named before asking again', async () => {
        const wire = wireAnswering(() => throttled({ 'Retry-After': '3' }), answered);
        const wait = waitsRecorded();
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, wait.pausing, () => 0);

        await fetchFromDeployment('/api/client/preferences', {}, statusOf);

        expect(wait.waits).toEqual([3_000]);
    });

    it('puts a read nothing answered on the wire again and hands over the answer that followed', async () => {
        const wire = wireRefusingFirst(1);
        const wait = waitsRecorded();
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, wait.pausing, () => 0.5);

        const status = await fetchFromDeployment('/api/client/contacts', { method: 'GET' }, statusOf);

        expect(status).toBe(200);
        expect(wire.attempts).toHaveLength(2);
        expect(wait.waits).toEqual([250]);
    });

    it('rejects with the failure once a read nothing answered has spent its retries', async () => {
        const wire = wireRefusingFirst(Number.POSITIVE_INFINITY);
        const wait = waitsRecorded();
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, wait.pausing, () => 0.5);

        await expect(fetchFromDeployment('/api/client/contacts', {}, statusOf)).rejects.toThrow('Failed to fetch');
        expect(wire.attempts).toHaveLength(mostRetries + 1);
        expect(wait.waits).toEqual([250, 500, 1_000]);
    });

    it('hands over a deployment not answering for now once the retries are spent', async () => {
        const wire = wireAnswering(unavailable);
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, waitsRecorded().pausing, () => 0.5);

        const status = await fetchFromDeployment('/api/client/contacts', {}, statusOf);

        expect(status).toBe(503);
        expect(wire.attempts).toHaveLength(mostRetries + 1);
    });

    it('never puts a write on the wire again after a failure that proves nothing about it', async () => {
        const refused = wireRefusingFirst(1);
        const gateway = wireAnswering(() => new Response(null, { status: 504 }), answered);
        const wait = waitsRecorded();

        await expect(
            deploymentFetchOver(refused.fetching, wait.pausing, () => 0.5)('/send', { method: 'POST' }, statusOf),
        ).rejects.toThrow('Failed to fetch');
        const status = await deploymentFetchOver(gateway.fetching, wait.pausing, () => 0.5)(
            '/send',
            { method: 'POST' },
            statusOf,
        );

        expect(status).toBe(504);
        expect(refused.attempts).toHaveLength(1);
        expect(gateway.attempts).toHaveLength(1);
        expect(wait.waits).toEqual([]);
    });

    it('puts a request its caller retries on the wire once', async () => {
        const wire = wireAnswering(throttled, answered);
        const wait = waitsRecorded();
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, wait.pausing, () => 0.5);

        const status = await fetchFromDeployment('/api/client/session', {}, statusOf, { retriedByCaller: true });

        expect(status).toBe(429);
        expect(wire.attempts).toHaveLength(1);
        expect(wait.waits).toEqual([]);
    });

    it('ends the attempts at once when the request is abandoned during a wait between them', async () => {
        const wire = wireAnswering(unavailable, answered);
        const abandoning = new AbortController();
        const pausing: Pausing = (_milliseconds, abandoned) =>
            new Promise((_resolve, reject) => {
                abandoned?.addEventListener('abort', () => {
                    reject(new DOMException('abandoned', 'AbortError'));
                });
                abandoning.abort();
            });
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, pausing, () => 0.5);

        await expect(
            fetchFromDeployment('/api/client/contacts', { signal: abandoning.signal }, statusOf),
        ).rejects.toMatchObject({ name: 'AbortError' });
        expect(wire.attempts).toHaveLength(1);
    });

    it('never retries a request abandoned while it was on the wire', async () => {
        const abandoning = new AbortController();
        const attempts: string[] = [];
        const fetching: Fetching = (path) => {
            attempts.push(path);
            abandoning.abort();

            return Promise.reject(new DOMException('abandoned', 'AbortError'));
        };
        const wait = waitsRecorded();
        const fetchFromDeployment = deploymentFetchOver(fetching, wait.pausing, () => 0.5);

        await expect(
            fetchFromDeployment('/api/client/contacts', { signal: abandoning.signal }, statusOf),
        ).rejects.toMatchObject({ name: 'AbortError' });
        expect(attempts).toHaveLength(1);
        expect(wait.waits).toEqual([]);
    });

    it('holds a request back until one of those in flight has been answered in full', async () => {
        const opened: string[] = [];
        const finishes: (() => void)[] = [];
        const fetching: Fetching = (path) => {
            opened.push(path);

            return Promise.resolve(answered());
        };
        const fetchFromDeployment = deploymentFetchOver(fetching, waitsRecorded().pausing, () => 0.5, 2);

        // Each answer is read until the test says it is finished, which is what keeps its place taken.
        const readUntilReleased = (): Promise<void> =>
            new Promise((finished) => {
                finishes.push(finished);
            });

        const first = fetchFromDeployment('/first', {}, readUntilReleased);
        const second = fetchFromDeployment('/second', {}, readUntilReleased);
        const third = fetchFromDeployment('/third', {}, readUntilReleased);

        // The third was queued the moment it was asked for, so once the first two are being read nothing but a
        // release can put it on the wire.
        await waitFor(() => {
            expect(finishes).toHaveLength(2);
        });
        expect(opened).toEqual(['/first', '/second']);

        finishes[0]?.();
        await first;

        await waitFor(() => {
            expect(finishes).toHaveLength(3);
        });
        expect(opened).toEqual(['/first', '/second', '/third']);

        finishes[1]?.();
        finishes[2]?.();
        await Promise.all([second, third]);
    });

    it('gives up the turn of a request abandoned while it waited, so the next one in line takes it', async () => {
        const opened: string[] = [];
        const finishes: (() => void)[] = [];
        const fetching: Fetching = (path) => {
            opened.push(path);

            return Promise.resolve(answered());
        };
        const fetchFromDeployment = deploymentFetchOver(fetching, waitsRecorded().pausing, () => 0.5, 1);
        const readUntilReleased = (): Promise<void> =>
            new Promise((finished) => {
                finishes.push(finished);
            });
        const abandoning = new AbortController();

        const first = fetchFromDeployment('/first', {}, readUntilReleased);
        const abandoned = fetchFromDeployment('/abandoned', { signal: abandoning.signal }, readUntilReleased);
        const last = fetchFromDeployment('/last', {}, readUntilReleased);

        await waitFor(() => {
            expect(finishes).toHaveLength(1);
        });

        abandoning.abort();
        await expect(abandoned).rejects.toMatchObject({ name: 'AbortError' });

        finishes[0]?.();
        await first;

        await waitFor(() => {
            expect(finishes).toHaveLength(2);
        });
        expect(opened).toEqual(['/first', '/last']);

        finishes[1]?.();
        await last;
    });

    it('tells its watcher where each request has got to, retries included', async () => {
        const wire = wireAnswering(throttled, answered);
        const wait = waitsRecorded();
        const told: string[] = [];
        const watching: RequestWatcher = (path) => {
            told.push(`asked ${path}`);

            return {
                sent: (made) => {
                    told.push(`sent ${String(made)}`);
                },
                answered: (response) => {
                    told.push(`answered ${String(response.status)}`);
                },
                retrying: (milliseconds) => {
                    told.push(`retrying ${String(milliseconds)}`);
                },
                ended: (outcome) => {
                    told.push(`ended ${outcome}`);
                },
            };
        };

        await deploymentFetchOver(wire.fetching, wait.pausing, () => 0.5, 6, watching)('/contacts', {}, statusOf);

        expect(told).toEqual([
            'asked /contacts',
            'sent 0',
            'answered 429',
            'retrying 250',
            'sent 1',
            'answered 200',
            'ended read',
        ]);
    });

    it('tells its watcher a request failed once nothing answered it', async () => {
        const wire = wireRefusingFirst(mostRetries + 1);
        const wait = waitsRecorded();
        const ended: string[] = [];

        await expect(
            deploymentFetchOver(
                wire.fetching,
                wait.pausing,
                () => 0.5,
                6,
                () => ({
                    sent: () => undefined,
                    answered: () => undefined,
                    retrying: () => undefined,
                    ended: (outcome) => {
                        ended.push(outcome);
                    },
                }),
            )('/contacts', { method: 'GET' }, statusOf),
        ).rejects.toThrow();

        expect(ended).toEqual(['failed']);
    });

    it('puts a probe on the wire though every place is taken by a request that hangs', async () => {
        const opened: string[] = [];
        const fetching: Fetching = (path) => {
            opened.push(path);

            return path === '/hung' ? new Promise<Response>(() => undefined) : Promise.resolve(answered());
        };
        const fetchFromDeployment = deploymentFetchOver(fetching, waitsRecorded().pausing, () => 0.5, 1);

        void fetchFromDeployment('/hung', {}, statusOf).catch(() => undefined);
        await waitFor(() => {
            expect(opened).toEqual(['/hung']);
        });
        const probed = await fetchFromDeployment('/session', {}, statusOf, { probes: true });

        expect(probed).toBe(200);
        expect(opened).toEqual(['/hung', '/session']);
    });

    it('puts a probe on the wire once, whatever the limiter asked of it', async () => {
        const wire = wireAnswering(() => throttled({ 'Retry-After': '3' }), answered);
        const wait = waitsRecorded();
        const fetchFromDeployment = deploymentFetchOver(wire.fetching, wait.pausing, () => 0.5);

        const status = await fetchFromDeployment('/session', {}, statusOf, { probes: true });

        expect(status).toBe(429);
        expect(wire.attempts).toHaveLength(1);
        expect(wait.waits).toEqual([]);
    });

    it('gives a probe up once its bound has passed with nothing answered', async () => {
        const bounds: number[] = [];
        let overstay = (): void => undefined;
        const bounding: Bounding = (milliseconds) => {
            const bound = new AbortController();

            bounds.push(milliseconds);
            overstay = () => {
                bound.abort();
            };

            return { signal: bound.signal, release: () => undefined };
        };
        const fetching: Fetching = (_path, init) =>
            new Promise((_resolve, reject) => {
                init.signal?.addEventListener('abort', () => {
                    reject(new DOMException('bounded', 'AbortError'));
                });
            });
        const fetchFromDeployment = deploymentFetchOver(
            fetching,
            waitsRecorded().pausing,
            () => 0.5,
            6,
            () => unwatched,
            requestsInFlightOver(() => 0),
            bounding,
        );

        const probing = fetchFromDeployment('/session', {}, statusOf, { probes: true });
        overstay();

        await expect(probing).rejects.toMatchObject({ name: 'AbortError' });
        expect(bounds).toEqual([15_000]);
    });

    it('enters every request but a probe where a probe can ask about it, and lets it go once it is over', async () => {
        const inFlight = requestsInFlightOver(() => 7);
        const seen: (number | null)[] = [];
        const fetchFromDeployment = deploymentFetchOver(
            wireAnswering(answered).fetching,
            waitsRecorded().pausing,
            () => 0.5,
            6,
            () => unwatched,
            inFlight,
        );
        const noting = (): Promise<number> => {
            seen.push(inFlight.oldestWrite());

            return Promise.resolve(200);
        };

        await fetchFromDeployment('/mutations/flags', { method: 'POST' }, noting);
        await fetchFromDeployment('/session', {}, noting, { probes: true });

        expect(seen).toEqual([7, null]);
        expect(inFlight.oldest()).toBeNull();
    });

    it('enters a POST that only asks something as a read, so its waiting is never said as a write', async () => {
        const inFlight = requestsInFlightOver(() => 3);
        const writesSeen: (number | null)[] = [];
        const fetchFromDeployment = deploymentFetchOver(
            wireAnswering(answered).fetching,
            waitsRecorded().pausing,
            () => 0.5,
            6,
            () => unwatched,
            inFlight,
        );

        await fetchFromDeployment(
            '/mail/replies/draft',
            { method: 'POST' },
            () => {
                writesSeen.push(inFlight.oldestWrite());

                return Promise.resolve(200);
            },
            { changesNothing: true },
        );

        expect(writesSeen).toEqual([null]);
    });

    it('ends a request a probe abandoned while it waited out a retry, with no attempt after it', async () => {
        const inFlight = requestsInFlightOver(() => 0);
        const wire = wireAnswering(unavailable, answered);
        const pausing: Pausing = (_milliseconds, abandoned) =>
            new Promise((_resolve, reject) => {
                abandoned?.addEventListener('abort', () => {
                    reject(new DOMException('abandoned', 'AbortError'));
                });
                inFlight.abandonStill();
            });
        const fetchFromDeployment = deploymentFetchOver(
            wire.fetching,
            pausing,
            () => 0.5,
            6,
            () => unwatched,
            inFlight,
        );

        await expect(fetchFromDeployment('/contacts', {}, statusOf)).rejects.toMatchObject({ name: 'AbortError' });
        expect(wire.attempts).toHaveLength(1);
    });
});
