// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useSyncExternalStore } from 'react';
import {
    observeClientEvents,
    parseClientSignal,
    type MailFathomSignalChannel,
    type ObservedClientEvent,
    type SignalStreamSchedule,
} from '@mailfathom/client-backend';
import type { RequestWatch, RequestWatcher } from '../deployment/deploymentFetch';
import { measureWebVitals } from './webVitals';

// What the developer information surface draws about what this client did, gathered from the moment the page loaded.
//
// It is fed from three places and nothing else: every client event, through the one observer `Client.Backend` lets the
// application state; every request at the one place the application puts it on the wire; and the signal channel and its
// schedule, which are the only places that know the run a signal named and how long the stream is waiting. Each is a
// wrapper the composition root applies once, so no screen records anything for it.
//
// **It does nothing while the surface is closed.** Watching begins when the surface opens, with an empty record, and
// ends when it closes, which lets the record go: every wrapper below asks whether anybody is watching before it does
// anything at all, so a closed surface costs one comparison per request. What happened before it was opened is not
// shown, except the Web Vitals, which the platform buffers on its own, and whether the signal hub stands, which the
// channel notes as a connection opens or ends.
//
// **It never leaves the page.** Everything here is in memory and bounded, nothing is stored and nothing is exported, so
// it is not a second telemetry pipeline and the person's telemetry switch is not what governs it. It also holds no
// mail: a path is kept without its query string, which is where a search phrase travels, a signal is kept by its kind
// alone unless it names a run, and a payload, a body, and a header other than the trace context are never read.

/** How many of each kind of entry the record keeps, the oldest giving way first. */
export const keptEntries = 50;

/** Where the signal hub stands, as the last occurrence about it said. */
export type SignalStanding = 'notYet' | 'opened' | 'dropped' | 'refused' | 'unreachable';

/** The stream's account of itself. */
export interface SignalRecord {
    readonly standing: SignalStanding;

    /** When the standing last changed, as an epoch instant in milliseconds. */
    readonly since: number | null;

    /** How many attempts in a row have been refused, which a connection standing resets. */
    readonly refusals: number;

    /** When the stream next tries, while it is waiting to. */
    readonly nextAttemptAt: number | null;

    /** How many of each kind of signal arrived. */
    readonly received: Readonly<Record<string, number>>;
    readonly lastReceivedAt: number | null;

    /** How many payloads the client did not act on, which is a deployment speaking a vocabulary it does not have. */
    readonly refusedPayloads: number;

    /** The last run a signal said had moved on. */
    readonly lastRun: {
        readonly run: string | null;
        readonly conversation: string | null;
        readonly sequence: number;
    } | null;
}

/**
 * Where one request has got to in the transport.
 *
 * The first three are a request that has not reached the deployment yet, or may not have: waiting for one of the places
 * the client keeps on the wire, sent and not yet answered, and waiting out a retry. The last three are over.
 */
export type RequestPhase = 'queued' | 'sent' | 'waitingToRetry' | 'reading' | 'read' | 'failed' | 'abandoned';

/** Whether a request in that phase is still on its way rather than over. */
export function isPending(phase: RequestPhase): boolean {
    return phase === 'queued' || phase === 'sent' || phase === 'waitingToRetry' || phase === 'reading';
}

/** One request, from when it was asked for until it is over. */
export interface WireRequest {
    readonly key: number;
    readonly method: string;

    /** The path alone: the origin is the deployment's address, and the query string is never kept. */
    readonly path: string;
    readonly phase: RequestPhase;

    /** Which attempt is the latest within the transport's own retries, counting from one, or zero before the first. */
    readonly attempts: number;
    readonly askedAt: number;
    readonly endedAt: number | null;

    /** When the next attempt goes out, while the request is waiting out a retry. */
    readonly retryAt: number | null;

    /** The status of the latest answer, where one arrived. */
    readonly status: number | null;

    /** What the latest answer said its body measures, where it said. */
    readonly declaredBytes: number | null;
    readonly traceId: string | null;
}

/** What an operation reported about a request it made: the template it asked by and how the answer was read. */
export interface OperationOutcome {
    readonly request: string;

    /** The failure reason it read the answer as, or `null` where it acted on the answer. */
    readonly failure: string | null;
}

/** One occurrence in the order it happened. */
export interface NoticedEvent {
    readonly key: number;
    readonly event: string;
    readonly at: number;
    readonly attributes: Readonly<Record<string, string | number>>;
}

/** One Web Vital as the page last measured it. */
export interface VitalReading {
    readonly value: number;
    readonly at: number;
}

export type VitalName = 'lcp' | 'cls' | 'inp' | 'fcp' | 'ttfb';

export interface DeveloperRecord {
    readonly signals: SignalRecord;
    readonly requests: readonly WireRequest[];

    /** What each request an operation reported came to, by the trace it was made under. */
    readonly outcomes: Readonly<Record<string, OperationOutcome>>;

    /** How many times each route template was answered and how many times it failed, whatever the trace. */
    readonly byTemplate: Readonly<Record<string, { readonly read: number; readonly failed: number }>>;
    readonly events: readonly NoticedEvent[];
    readonly renderFailures: Readonly<Record<string, number>>;
    readonly vitals: Readonly<Partial<Record<VitalName, VitalReading>>>;
}

const nothingYet: DeveloperRecord = {
    signals: {
        standing: 'notYet',
        since: null,
        refusals: 0,
        nextAttemptAt: null,
        received: {},
        lastReceivedAt: null,
        refusedPayloads: 0,
        lastRun: null,
    },
    requests: [],
    outcomes: {},
    byTemplate: {},
    events: [],
    renderFailures: {},
    vitals: {},
};

let current = nothingYet;
let nextKey = 0;
let watching = false;

// The one thing kept while nobody watches: whether a connection to the hub stands, and since when. It is written only
// as a connection opens or ends — never on a timer and never per signal — and it is what lets a surface opened long
// after the hub connected say that it did, rather than that nothing has happened since the surface opened.
let hub: { readonly standing: 'opened' | 'dropped'; readonly since: number } | null = null;
let stopMeasuring = (): void => undefined;
const listeners = new Set<() => void>();

/** Begins watching, from an empty record, which the surface does as it opens. */
export function startWatching(): void {
    current =
        hub === null
            ? nothingYet
            : { ...nothingYet, signals: { ...nothingYet.signals, standing: hub.standing, since: hub.since } };
    watching = true;
    observeClientEvents(noticeForDeveloper);
    stopMeasuring = measureWebVitals(noticeVital);
}

/** Stops watching and lets the record go, which the surface does as it closes. */
export function stopWatching(): void {
    watching = false;
    observeClientEvents(null);
    stopMeasuring();
    current = nothingYet;
}

/** What a request is told to report to while nothing is listening, which is nowhere. */
export const notWatched: RequestWatch = {
    sent: () => undefined,
    answered: () => undefined,
    retrying: () => undefined,
    ended: () => undefined,
};

function revise(change: (record: DeveloperRecord) => DeveloperRecord): void {
    if (!watching) {
        return;
    }

    current = change(current);

    for (const listener of listeners) {
        listener();
    }
}

function subscribe(listener: () => void): () => void {
    listeners.add(listener);

    return () => {
        listeners.delete(listener);
    };
}

/** What the record holds now. */
export function developerRecord(): DeveloperRecord {
    return current;
}

/** What the record holds, read again whenever it changes. */
export function useDeveloperRecord(): DeveloperRecord {
    return useSyncExternalStore(subscribe, developerRecord);
}

function kept<TEntry>(entries: readonly TEntry[], added: TEntry): readonly TEntry[] {
    return [...entries, added].slice(-keptEntries);
}

function keptKeys<TValue>(
    entries: Readonly<Record<string, TValue>>,
    key: string,
    value: TValue,
): Record<string, TValue> {
    return Object.fromEntries([...Object.entries(entries), [key, value] as const].slice(-keptEntries));
}

const standingAfter: Partial<Record<string, SignalStanding>> = {
    signals_opened: 'opened',
    signals_dropped: 'dropped',
    signals_refused: 'refused',
    signals_unreachable: 'unreachable',
};

/** Takes one occurrence into the record, which is the observer the composition root hands `Client.Backend`. */
export function noticeForDeveloper(occurrence: ObservedClientEvent): void {
    const attributes = occurrence.attributes ?? {};

    revise((record) => {
        let { signals, outcomes, byTemplate, renderFailures } = record;
        const standing = standingAfter[occurrence.event];

        if (standing !== undefined) {
            const attempt = attributes['mailfathom.client.attempt'];

            signals = {
                ...signals,
                standing,
                since: occurrence.at,
                refusals: typeof attempt === 'number' ? attempt : standing === 'opened' ? 0 : signals.refusals,
                nextAttemptAt: standing === 'opened' ? null : signals.nextAttemptAt,
            };
        }

        if (occurrence.event === 'signal_received') {
            const kind = String(attributes['mailfathom.client.signal'] ?? 'unknown');

            signals = {
                ...signals,
                received: { ...signals.received, [kind]: (signals.received[kind] ?? 0) + 1 },
                lastReceivedAt: occurrence.at,
            };
        }

        if (occurrence.event === 'signal_refused') {
            signals = { ...signals, refusedPayloads: signals.refusedPayloads + 1 };
        }

        if (occurrence.event === 'request_completed' || occurrence.event === 'request_failed') {
            const request = String(attributes['mailfathom.client.request'] ?? 'unknown');
            const named = attributes['mailfathom.client.failure'];
            const failure = named === undefined ? null : String(named);
            const counted = byTemplate[request] ?? { read: 0, failed: 0 };

            byTemplate = {
                ...byTemplate,
                [request]:
                    failure === null
                        ? { ...counted, read: counted.read + 1 }
                        : { ...counted, failed: counted.failed + 1 },
            };

            if (occurrence.traceId !== undefined) {
                outcomes = keptKeys(outcomes, occurrence.traceId, { request, failure });
            }
        }

        if (occurrence.event === 'render_failed') {
            const region = String(attributes['mailfathom.client.region'] ?? 'unknown');

            renderFailures = { ...renderFailures, [region]: (renderFailures[region] ?? 0) + 1 };
        }

        return {
            ...record,
            signals,
            outcomes,
            byTemplate,
            renderFailures,
            events: kept(record.events, {
                key: nextKey++,
                event: occurrence.event,
                at: occurrence.at,
                attributes,
            }),
        };
    });
}

/** The path a request went to, without the origin and without the query string. */
export function pathOf(address: string): string {
    return new URL(address, window.location.href).pathname;
}

const traceParent = /^[\da-f]{2}-([\da-f]{32})-[\da-f]{16}-[\da-f]{2}$/;

/** The trace a request carries in its W3C trace context, or `null` where it carries none that names one. */
export function traceIdOf(headers: HeadersInit | undefined): string | null {
    const traceId = traceParent.exec(new Headers(headers).get('traceparent') ?? '')?.[1];

    return traceId === undefined || /^0+$/.test(traceId) ? null : traceId;
}

function declaredBytesOf(response: Response): number | null {
    const declared = Number.parseInt(response.headers.get('content-length') ?? '', 10);

    return Number.isNaN(declared) ? null : declared;
}

// A request still on its way is never the one given up to the bound, because the next thing it reports would then
// describe nothing; past the bound the oldest request that is over goes first.
function keptRequests(requests: readonly WireRequest[], added: WireRequest): readonly WireRequest[] {
    const next = [...requests, added];
    const over = next.findIndex((request) => !isPending(request.phase));

    return next.length > keptEntries && over !== -1 ? next.toSpliced(over, 1) : next;
}

/** Puts each request into the record from when it is asked for until it is over, which is the transport's watcher. */
export const watchedByDeveloperRecord: RequestWatcher = (path, init) => {
    if (!watching) {
        return notWatched;
    }

    const key = nextKey++;
    const update = (change: Partial<WireRequest>): void => {
        revise((record) => ({
            ...record,
            requests: record.requests.map((request) => (request.key === key ? { ...request, ...change } : request)),
        }));
    };

    revise((record) => ({
        ...record,
        requests: keptRequests(record.requests, {
            key,
            method: init.method ?? 'GET',
            path: pathOf(path),
            phase: 'queued',
            attempts: 0,
            askedAt: Date.now(),
            endedAt: null,
            retryAt: null,
            status: null,
            declaredBytes: null,
            traceId: traceIdOf(init.headers),
        }),
    }));

    return {
        sent: (made) => {
            update({ phase: 'sent', attempts: made + 1, retryAt: null });
        },
        answered: (response) => {
            update({ phase: 'reading', status: response.status, declaredBytes: declaredBytesOf(response) });
        },
        retrying: (milliseconds) => {
            update({ phase: 'waitingToRetry', retryAt: Date.now() + milliseconds });
        },
        ended: (outcome) => {
            update({ phase: outcome, endedAt: Date.now(), retryAt: null });
        },
    };
};

/**
 * Puts the run a signal names into the record, which is the one thing about a signal the events do not carry, and
 * keeps whether a connection stands for a surface opened later.
 */
export function watchedSignalChannel(channel: MailFathomSignalChannel): MailFathomSignalChannel {
    return async (opening) => {
        const handle = await channel({
            ...opening,
            dropped: () => {
                hub = { standing: 'dropped', since: Date.now() };
                opening.dropped();
            },
            arrived: (payload) => {
                const signal = watching ? parseClientSignal(payload) : null;

                if (signal?.kind === 'run.advanced') {
                    revise((record) => ({
                        ...record,
                        signals: {
                            ...record.signals,
                            lastRun: { run: signal.run, conversation: signal.conversation, sequence: signal.sequence },
                        },
                    }));
                }

                opening.arrived(payload);
            },
        });

        hub = { standing: 'opened', since: Date.now() };

        return {
            close: async () => {
                hub = { standing: 'dropped', since: Date.now() };
                await handle.close();
            },
        };
    };
}

/** Puts when the stream next tries into the record, which only the wait itself knows. */
export function watchedSignalSchedule(schedule: SignalStreamSchedule): SignalStreamSchedule {
    return {
        draw: schedule.draw,
        wait: async (milliseconds) => {
            revise((record) => ({
                ...record,
                signals: { ...record.signals, nextAttemptAt: Date.now() + milliseconds },
            }));

            await schedule.wait(milliseconds);

            revise((record) => ({ ...record, signals: { ...record.signals, nextAttemptAt: null } }));
        },
    };
}

function noticeVital(name: VitalName, value: number): void {
    revise((record) => ({ ...record, vitals: { ...record.vitals, [name]: { value, at: Date.now() } } }));
}
