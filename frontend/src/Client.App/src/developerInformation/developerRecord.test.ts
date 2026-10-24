// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { writeClientEvent, type SignalChannelOpening } from '@mailfathom/client-backend';
import {
    developerRecord,
    keptEntries,
    pathOf,
    startWatching,
    stopWatching,
    traceIdOf,
    watchedByDeveloperRecord,
    watchedSignalChannel,
    watchedSignalSchedule,
} from './developerRecord';

const traced = { traceparent: '00-4bf92f3577b34da6a3ce929d0e0e4736-0123456789abcdef-01' };

afterEach(() => {
    stopWatching();
    vi.useRealTimers();
});

describe('what the record holds while nothing is watching', () => {
    it('takes in nothing at all', () => {
        writeClientEvent({ event: 'signals_opened' });
        watchedByDeveloperRecord('https://mail.example/api/client/accounts', {}).sent(0);

        expect(developerRecord().events).toEqual([]);
        expect(developerRecord().requests).toEqual([]);
    });

    it('lets go of what it held once the surface closes', () => {
        startWatching();
        writeClientEvent({ event: 'signals_opened' });

        stopWatching();

        expect(developerRecord().events).toEqual([]);
        expect(developerRecord().signals.standing).toBe('notYet');
    });
});

describe('a request, as the transport reports it', () => {
    it('is kept by its path alone, without the query string a search phrase travels in', () => {
        expect(pathOf('https://mail.example/api/client/search?q=invoice%20from%20Anna')).toBe('/api/client/search');
    });

    it('is joined to the trace its context names, and to none where the context names an empty one', () => {
        expect(traceIdOf(traced)).toBe('4bf92f3577b34da6a3ce929d0e0e4736');
        expect(traceIdOf({ traceparent: '00-00000000000000000000000000000000-0123456789abcdef-00' })).toBeNull();
        expect(traceIdOf(undefined)).toBeNull();
    });

    it('is followed from waiting for a place to its answer being read', () => {
        startWatching();

        const watch = watchedByDeveloperRecord('https://mail.example/api/client/accounts?window=50', {
            method: 'GET',
            headers: traced,
        });
        const phase = (): string | undefined => developerRecord().requests[0]?.phase;

        expect(phase()).toBe('queued');
        watch.sent(0);
        expect(phase()).toBe('sent');
        watch.answered(new Response(null, { status: 429 }));
        watch.retrying(250);
        expect(phase()).toBe('waitingToRetry');
        watch.sent(1);
        watch.answered(new Response('{}', { status: 200, headers: { 'Content-Length': '2' } }));
        watch.ended('read');

        expect(developerRecord().requests).toEqual([
            expect.objectContaining({
                method: 'GET',
                path: '/api/client/accounts',
                phase: 'read',
                attempts: 2,
                status: 200,
                declaredBytes: 2,
                traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
            }),
        ]);
    });

    it('keeps a request still on its way when the bound gives way, and lets the oldest finished one go', () => {
        startWatching();

        const waiting = watchedByDeveloperRecord('https://mail.example/api/client/waiting', {});

        for (let made = 0; made < keptEntries; made += 1) {
            watchedByDeveloperRecord(`https://mail.example/api/client/done/${String(made)}`, {}).ended('read');
        }

        const paths = developerRecord().requests.map((request) => request.path);

        expect(paths).toHaveLength(keptEntries);
        expect(paths).toContain('/api/client/waiting');
        expect(paths).not.toContain('/api/client/done/0');

        waiting.ended('abandoned');
    });
});

describe('the signal hub, as the events and the stream report it', () => {
    it('stands where the last occurrence about it said, with the refusals counted', () => {
        startWatching();

        writeClientEvent({ event: 'signals_refused', attributes: { 'mailfathom.client.attempt': 2 } });
        expect(developerRecord().signals).toMatchObject({ standing: 'refused', refusals: 2 });

        writeClientEvent({ event: 'signals_opened' });
        writeClientEvent({ event: 'signal_received', attributes: { 'mailfathom.client.signal': 'mail.arrived' } });
        writeClientEvent({ event: 'signal_received', attributes: { 'mailfathom.client.signal': 'mail.arrived' } });

        expect(developerRecord().signals).toMatchObject({
            standing: 'opened',
            refusals: 0,
            received: { 'mail.arrived': 2 },
        });
    });

    it('keeps the run a signal moved on, and nothing from a signal that names mail', async () => {
        startWatching();

        let opening: SignalChannelOpening | undefined;
        const channel = watchedSignalChannel((handed) => {
            opening = handed;

            return Promise.resolve({ close: () => Promise.resolve() });
        });
        const arrived = vi.fn();

        await channel({ url: 'https://mail.example/signals?access_token=t', ticket: 't', arrived, dropped: vi.fn() });
        opening?.arrived({ kind: 'run.advanced', run: 'run-7', conversation: 'conversation-3', sequence: 4 });

        expect(developerRecord().signals.lastRun).toEqual({
            run: 'run-7',
            conversation: 'conversation-3',
            sequence: 4,
        });
        expect(arrived).toHaveBeenCalledTimes(1);
    });

    it('says the hub stands to a surface opened after it connected', async () => {
        const channel = watchedSignalChannel(() => Promise.resolve({ close: () => Promise.resolve() }));
        const handle = await channel({
            url: 'https://mail.example/signals',
            ticket: 't',
            arrived: vi.fn(),
            dropped: vi.fn(),
        });

        startWatching();
        expect(developerRecord().signals.standing).toBe('opened');

        stopWatching();
        await handle.close();
        startWatching();
        expect(developerRecord().signals.standing).toBe('dropped');
    });

    it('knows when the stream next tries for as long as it is waiting to', async () => {
        vi.useFakeTimers();
        vi.setSystemTime(1_000);
        startWatching();

        let release = (): void => undefined;
        const schedule = watchedSignalSchedule({
            wait: () =>
                new Promise((resolve) => {
                    release = resolve;
                }),
            draw: () => 0.5,
        });

        const waited = schedule.wait(4_000);

        expect(developerRecord().signals.nextAttemptAt).toBe(5_000);

        release();
        await waited;

        expect(developerRecord().signals.nextAttemptAt).toBeNull();
    });
});

describe('a failed render', () => {
    it('is counted for the region that failed', () => {
        startWatching();

        writeClientEvent({ event: 'render_failed', attributes: { 'mailfathom.client.region': 'reading_pane' } });

        expect(developerRecord().renderFailures).toEqual({ reading_pane: 1 });
    });
});
