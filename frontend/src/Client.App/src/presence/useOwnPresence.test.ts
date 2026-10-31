// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import type { SignalListener, SignalledChanges } from '../signals/signalledChanges';
import { useOwnPresence } from './useOwnPresence';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Bearer presence' };

// The presence route answering whatever the test last put in front of it, and counting how often it was asked.
function deployment(answer: { status: number; body?: unknown }): {
    readonly transport: MailFathomTransport;
    readonly reads: () => number;
    readonly answer: (next: { status: number; body?: unknown }) => void;
} {
    let current = answer;
    const requests: ClientRequest[] = [];

    return {
        transport: (request) => {
            requests.push(request);

            return Promise.resolve<ClientResponse>({
                status: current.status,
                body: current.body === undefined ? '' : JSON.stringify(current.body),
                headers: {},
            });
        },
        reads: () => requests.filter((request) => request.method === 'GET').length,
        answer: (next) => {
            current = next;
        },
    };
}

// A channel a test speaks into, as the frame's would.
function channel(): { readonly signalled: SignalledChanges; readonly say: SignalListener } {
    const listeners = new Set<SignalListener>();

    return {
        signalled: {
            listen: (listener) => {
                listeners.add(listener);

                return () => {
                    listeners.delete(listener);
                };
            },
            refresh: () => undefined,
        },
        say: (change) => {
            for (const listener of listeners) {
                listener(change);
            }
        },
    };
}

const busy = { state: 'Busy', chosenState: 'Busy', choiceEndsAt: null };

describe('useOwnPresence', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('draws no presence for a reader in no organization', async () => {
        const { transport, reads } = deployment({ status: 404 });
        const { result } = renderHook(() => useOwnPresence(session, transport, channel().signalled));

        await waitFor(() => {
            expect(reads()).toBe(1);
        });
        expect(result.current).toBeNull();
    });

    it('reads the member’s presence again when a presence change is signalled, whoever it names', async () => {
        const { transport, answer } = deployment({ status: 200, body: busy });
        const { signalled, say } = channel();
        const { result } = renderHook(() => useOwnPresence(session, transport, signalled));

        await waitFor(() => {
            expect(result.current?.presence.state).toBe('Busy');
        });

        answer({ status: 200, body: { state: 'Away', chosenState: null, choiceEndsAt: null } });
        act(() => {
            say({ kind: 'presence.changed', members: ['someone-else'] });
        });

        await waitFor(() => {
            expect(result.current?.presence.state).toBe('Away');
        });
    });

    it('reads it again at the instant the chosen while runs out, which no request marks', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });
        vi.setSystemTime(Date.parse('2026-10-31T09:00:00Z'));

        const { transport, answer } = deployment({
            status: 200,
            body: { state: 'Busy', chosenState: 'Busy', choiceDuration: '1h', choiceEndsAt: '2026-10-31T10:00:00Z' },
        });
        const { result } = renderHook(() => useOwnPresence(session, transport, channel().signalled));

        await waitFor(() => {
            expect(result.current?.presence.choiceEndsAt).toBe('2026-10-31T10:00:00Z');
        });

        answer({ status: 200, body: { state: 'Active', chosenState: null, choiceEndsAt: null } });

        await act(async () => {
            await vi.advanceTimersByTimeAsync(3_600_000);
        });

        await waitFor(() => {
            expect(result.current?.presence.state).toBe('Active');
        });
    });
});
