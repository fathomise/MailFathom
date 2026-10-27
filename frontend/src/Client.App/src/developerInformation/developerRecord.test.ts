// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { writeClientEvent, type SignalChannelOpening } from '@mailfathom/client-backend';
import {
    developerRecord,
    keptEntries,
    pathOf,
    previewOf,
    startWatching,
    stopWatching,
    traceIdOf,
    visibleHeaders,
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

    it('lets go of what it held once developer mode is forgotten', () => {
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

    it('keeps the headers it went out with and the ones its answer carried, withholding every credential', () => {
        startWatching();

        const watch = watchedByDeveloperRecord('https://mail.example/api/client/accounts', {
            headers: { Accept: 'application/json', Authorization: 'Bearer secret-token', ...traced },
        });

        watch.answered(
            new Response(null, {
                status: 200,
                headers: { 'Content-Type': 'application/json', 'Set-Cookie': 'session=abc' },
            }),
        );

        expect(developerRecord().requests[0]).toMatchObject({
            sentHeaders: [
                { name: 'accept', value: 'application/json' },
                { name: 'authorization', value: null },
                { name: 'traceparent', value: traced.traceparent },
            ],
            receivedHeaders: [
                { name: 'content-type', value: 'application/json' },
                { name: 'set-cookie', value: null },
            ],
        });
        expect(JSON.stringify(developerRecord().requests)).not.toMatch(/secret-token|session=abc/u);
    });

    it.each([
        ['a credential', 'Authorization', 'Basic QWxhZGRpbjpvcGVu'],
        ['a proxy credential', 'Proxy-Authorization', 'Basic QWxhZGRpbjpvcGVu'],
        ['a cookie', 'Cookie', 'session=abc'],
        ['a header named for a token', 'X-Access-Token', 'abc'],
        ['a header named for a key', 'X-Api-Key', 'abc'],
        ['a header named for a session', 'X-Session-Id', 'abc'],
        ['the file name of an attachment', 'Content-Disposition', 'attachment; filename="contract.pdf"'],
    ])('withholds the value of %s and keeps its name', (_, name, value) => {
        expect(visibleHeaders({ [name]: value })).toEqual([{ name: name.toLowerCase(), value: null }]);
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

    it('holds the bound when every request is still on its way, letting the oldest go', () => {
        startWatching();

        for (let made = 0; made <= keptEntries; made += 1) {
            watchedByDeveloperRecord(`https://mail.example/api/client/waiting/${String(made)}`, {});
        }

        const paths = developerRecord().requests.map((request) => request.path);

        expect(paths).toHaveLength(keptEntries);
        expect(paths[0]).toBe('/api/client/waiting/1');
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

    it('keeps every signal the hub delivered with what it carried, up to the bound', async () => {
        startWatching();

        let opening: SignalChannelOpening | undefined;
        const channel = watchedSignalChannel((handed) => {
            opening = handed;

            return Promise.resolve({ close: () => Promise.resolve() });
        });

        await channel({ url: 'https://mail.example/signals', ticket: 't', arrived: vi.fn(), dropped: vi.fn() });

        for (let delivered = 0; delivered <= keptEntries; delivered += 1) {
            opening?.arrived({ kind: 'folders.changed', account: `account-${String(delivered)}` });
        }

        opening?.arrived({ kind: 'telepathy', thought: 'something nobody should read' });

        const { deliveries } = developerRecord();

        expect(deliveries).toHaveLength(keptEntries);
        expect(deliveries[0]?.preview).toEqual({ kind: 'folders.changed', account: 'account-2' });
        expect(deliveries.at(-1)).toMatchObject({ kind: 'telepathy', preview: null });
        expect(JSON.stringify(deliveries)).not.toContain('something nobody should read');
    });

    it('shows a notification signal without the two lines it is read by', () => {
        const preview = previewOf({
            kind: 'notification.raised',
            notificationKind: 'Mail',
            headline: 'Anna Kowalska',
            secondLine: 'The contract is attached',
            unreadCount: 3,
        });

        expect(preview).toEqual({ kind: 'notification.raised', notificationKind: 'Mail', unreadCount: 3 });
    });

    it('says the hub stands to a record begun after it connected', async () => {
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
