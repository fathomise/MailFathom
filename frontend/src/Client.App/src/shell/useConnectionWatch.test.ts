// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MailAccount, MailSynchronizationState } from '@mailfathom/client-backend';
import type { SignalledChanges } from '../signals/signalledChanges';
import type { Connection } from './useConnection';
import { useConnectionWatch } from './useConnectionWatch';

// The thresholds themselves are proven against named instants in `connectionStatus.test.ts`. What is left for the hook
// is where each instant comes from — when this client first saw a thing — and what it asks for when the server answers
// again, so these run on a fake clock and move it.

const signedIn = 'https://mail.example.invalid\nkarolina';

function account(synchronizationState: MailSynchronizationState): MailAccount {
    return {
        id: 'work',
        displayName: 'Work',
        synchronizationState,
        lastSynchronizedAt: '2026-08-31T09:41:00+00:00',
        behind: false,
        filesJunk: false,
    };
}

function connected(change: Partial<Connection> = {}, accounts: readonly MailAccount[] = []): Connection {
    return {
        session: {
            outcome: 'read',
            value: {
                version: '0.8.7',
                permissions: ['mailfathom.mail.read'],
                telemetryLevel: 'info',
                revision: null,
                organization: null,
            },
        },
        accounts: { outcome: 'read', value: { synchronizationEnabled: true, accounts } },
        online: true,
        attempts: 0,
        unansweredSince: null,
        accountsReadAt: Date.now(),
        reread: () => undefined,
        refresh: () => undefined,
        ...change,
    };
}

function changes(): SignalledChanges & { readonly refreshed: () => number } {
    let refreshed = 0;

    return {
        listen: () => () => undefined,
        refresh: () => {
            refreshed += 1;
        },
        refreshed: () => refreshed,
    };
}

function pass(milliseconds: number): void {
    act(() => {
        vi.advanceTimersByTime(milliseconds);
    });
}

beforeEach(() => {
    vi.useFakeTimers();
});

afterEach(() => {
    vi.useRealTimers();
});

describe('useConnectionWatch', () => {
    it('is connecting at start-up until the first channel opening has had its ten seconds', () => {
        const watched = changes();
        const view = renderHook(() => useConnectionWatch(signedIn, connected(), true, watched));

        act(() => {
            view.result.current.channelStood('refused');
        });

        expect(view.result.current.status.level).toBe('connecting');

        pass(10_000);

        expect(view.result.current.status.level).toBe('delayed');
    });

    it('is online as soon as the first channel opening stands', () => {
        const view = renderHook(() => useConnectionWatch(signedIn, connected(), true, changes()));

        act(() => {
            view.result.current.channelStood('opened');
        });

        expect(view.result.current.status.level).toBe('online');
    });

    it('measures an unreachable mail server from when this client first read it so', () => {
        const watched = changes();
        const view = renderHook(({ connection }) => useConnectionWatch(signedIn, connection, false, watched), {
            initialProps: { connection: connected({}, [account('Synchronized')]) },
        });

        pass(120_000);
        view.rerender({ connection: connected({}, [account('Unreachable')]) });
        pass(59_000);

        expect(view.result.current.status.level).toBe('online');

        pass(2_000);

        expect(view.result.current.status).toMatchObject({
            level: 'delayed',
            delays: [{ kind: 'mailServer', account: 'Work' }],
        });

        view.rerender({ connection: connected({}, [account('Synchronized')]) });

        expect(view.result.current.status.level).toBe('online');
    });

    it('reads every screen again once a server that went unanswered answers', () => {
        const watched = changes();
        const view = renderHook(({ connection }) => useConnectionWatch(signedIn, connection, false, watched), {
            initialProps: { connection: connected() },
        });

        view.rerender({ connection: connected({ unansweredSince: Date.now() }) });

        expect(view.result.current.status.level).toBe('disconnected');
        expect(watched.refreshed()).toBe(0);

        view.rerender({ connection: connected() });

        expect(view.result.current.status.level).toBe('online');
        expect(watched.refreshed()).toBe(1);
    });

    it('closes the details on Retry and reads everything again', () => {
        const watched = changes();
        const view = renderHook(() =>
            useConnectionWatch(signedIn, connected({ unansweredSince: Date.now() }), false, watched),
        );

        act(() => {
            view.result.current.status.showDetails(true);
        });

        expect(view.result.current.status.detailsOpen).toBe(true);

        act(() => {
            view.result.current.status.retry();
        });

        expect(view.result.current.status.detailsOpen).toBe(false);
        expect(watched.refreshed()).toBe(1);
    });
});
