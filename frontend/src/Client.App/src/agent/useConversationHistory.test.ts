// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import * as agent from '../../../../tests/fixtures/agent';
import type { SignalListener, SignalledChanges } from '../signals/signalledChanges';
import { useConversationHistory } from './useConversationHistory';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function answering(): { readonly transport: MailFathomTransport; readonly asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            return Promise.resolve(
                request.method === 'GET'
                    ? { status: 200, body: JSON.stringify(agent.agentHistory), headers: {} }
                    : { status: 204, body: '', headers: {} },
            );
        },
    };
}

function signalling(): { readonly changes: SignalledChanges; readonly say: SignalListener } {
    const listeners = new Set<SignalListener>();

    return {
        changes: {
            listen: (listener) => {
                listeners.add(listener);

                return () => listeners.delete(listener);
            },
            refresh: () => undefined,
        },
        say: (change) => {
            listeners.forEach((listener) => {
                listener(change);
            });
        },
    };
}

const listings = (asked: readonly ClientRequest[]) => asked.filter((request) => request.method === 'GET').length;

describe('useConversationHistory', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('reads the history again once after a burst of signals that a run moved in a conversation', async () => {
        vi.useFakeTimers({ shouldAdvanceTime: true });
        const { transport, asked } = answering();
        const { changes, say } = signalling();

        renderHook(() => useConversationHistory(session, transport, changes));
        await waitFor(() => {
            expect(listings(asked)).toBe(1);
        });

        act(() => {
            say({ kind: 'run.advanced', run: 'r', conversation: agent.composingConversationId, sequence: 4 });
            say({ kind: 'run.advanced', run: 'r', conversation: agent.composingConversationId, sequence: 5 });
            say({ kind: 'run.advanced', run: 'd', conversation: null, sequence: 6 });
        });
        await act(() => vi.advanceTimersByTimeAsync(1000));

        await waitFor(() => {
            expect(listings(asked)).toBe(2);
        });
    });

    it('lets go of one person’s history the moment the session ends', async () => {
        const { transport } = answering();
        const initialProps: { signedIn: ClientSession | null } = { signedIn: session };
        const { result, rerender } = renderHook(
            ({ signedIn }) => useConversationHistory(signedIn, transport, signalling().changes),
            { initialProps },
        );
        await waitFor(() => {
            expect(result.current.conversations).toHaveLength(3);
        });

        rerender({ signedIn: null });

        expect(result.current.conversations).toEqual([]);
    });

    it('reads a conversation as read at once, and tells the deployment so', async () => {
        const { transport, asked } = answering();
        const unread: MailFathomTransport = (request) =>
            request.method === 'GET'
                ? Promise.resolve({
                      status: 200,
                      headers: {},
                      body: JSON.stringify({
                          conversations: agent.agentHistory.conversations.map((listed) => ({
                              ...listed,
                              unread: true,
                          })),
                      }),
                  })
                : transport(request);

        const { result } = renderHook(() => useConversationHistory(session, unread, signalling().changes));
        await waitFor(() => {
            expect(result.current.conversations).toHaveLength(3);
        });

        act(() => {
            result.current.markRead(agent.answeredConversationId);
        });

        expect(result.current.conversations.map((listed) => listed.unread)).toEqual([true, false, true]);
        expect(asked.map((request) => [request.method, request.path])).toEqual([
            ['PUT', `https://mail.example.invalid/api/client/agent/conversations/${agent.answeredConversationId}/read`],
        ]);
    });
});
