// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { createElement, type ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import type { ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { SignalledChangesContext, type SignalListener } from '../signals/signalledChanges';
import { useTasksWhere, useTasksWhereAny } from './useTasksWhere';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function taskCalled(id: string): unknown {
    return {
        id,
        title: id,
        dueOn: null,
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        direction: 'Owed',
        estimatedMinutes: null,
        plan: null,
        anchor: null,
        counterpart: null,
        answer: null,
    };
}

function pageOf(...ids: readonly string[]): ClientResponse {
    return { status: 200, headers: {}, body: JSON.stringify({ tasks: ids.map(taskCalled), nextCursor: null }) };
}

describe('useTasksWhere', () => {
    it('reads the tasks the filter names and reads them again when the tasks changed elsewhere', async () => {
        let asked = 0;
        const transport: MailFathomTransport = (request) => {
            asked += 1;

            expect(request.path).toContain('eventId=review');

            return Promise.resolve(pageOf(`prepare-${String(asked)}`));
        };
        const listeners = new Set<SignalListener>();
        const wrapper = ({ children }: { readonly children: ReactNode }) =>
            createElement(
                SignalledChangesContext,
                {
                    value: {
                        listen: (listener) => {
                            listeners.add(listener);

                            return () => {
                                listeners.delete(listener);
                            };
                        },
                        refresh: () => undefined,
                    },
                },
                children,
            );

        const { result } = renderHook(() => useTasksWhere(session, transport, { eventId: 'review' }), { wrapper });

        await waitFor(() => {
            expect(result.current.tasks.map((task) => task.id)).toStrictEqual(['prepare-1']);
        });

        act(() => {
            for (const listener of listeners) {
                listener({ kind: 'tasks.changed' });
            }
        });

        await waitFor(() => {
            expect(result.current.tasks.map((task) => task.id)).toStrictEqual(['prepare-2']);
        });
        expect(asked).toBe(2);
    });

    it('draws nothing of a filter since replaced, even where its answer arrives last', async () => {
        const answers = new Map<string, (answer: ClientResponse) => void>();
        const transport: MailFathomTransport = (request) =>
            new Promise((resolve) => {
                answers.set(request.path.includes('eventId=first') ? 'first' : 'second', resolve);
            });

        const { result, rerender } = renderHook(({ eventId }) => useTasksWhere(session, transport, { eventId }), {
            initialProps: { eventId: 'first' },
        });

        rerender({ eventId: 'second' });

        await waitFor(() => {
            expect(answers.size).toBe(2);
        });

        act(() => {
            answers.get('second')?.(pageOf('of-the-second'));
        });
        await waitFor(() => {
            expect(result.current.tasks.map((task) => task.id)).toStrictEqual(['of-the-second']);
        });

        act(() => {
            answers.get('first')?.(pageOf('of-the-first'));
        });

        expect(result.current.tasks.map((task) => task.id)).toStrictEqual(['of-the-second']);
    });

    it('reads each filter of several and holds a task two of them name once', async () => {
        const transport: MailFathomTransport = (request) =>
            Promise.resolve(
                request.path.includes('counterpart=ann%40example.invalid')
                    ? pageOf('shared', 'first-only')
                    : pageOf('second-only', 'shared'),
            );

        const { result } = renderHook(() =>
            useTasksWhereAny(session, transport, [
                { counterpart: 'ann@example.invalid' },
                { counterpart: 'a.n@example.invalid' },
            ]),
        );

        await waitFor(() => {
            expect(result.current.tasks.map((task) => task.id)).toStrictEqual(['shared', 'first-only', 'second-only']);
        });
    });

    it('keeps what the other walks answered when one of several fails on a later read, and says it failed', async () => {
        let reads = 0;
        const transport: MailFathomTransport = (request) => {
            reads += 1;

            if (request.path.includes('counterpart=ann%40example.invalid')) {
                return Promise.resolve(pageOf(reads > 2 ? 'first-again' : 'first'));
            }

            return Promise.resolve(reads > 2 ? { status: 503, headers: {}, body: '' } : pageOf('second'));
        };
        const listeners = new Set<SignalListener>();
        const wrapper = ({ children }: { readonly children: ReactNode }) =>
            createElement(
                SignalledChangesContext,
                {
                    value: {
                        listen: (listener) => {
                            listeners.add(listener);

                            return () => {
                                listeners.delete(listener);
                            };
                        },
                        refresh: () => undefined,
                    },
                },
                children,
            );

        const { result } = renderHook(
            () =>
                useTasksWhereAny(session, transport, [
                    { counterpart: 'ann@example.invalid' },
                    { counterpart: 'a.n@example.invalid' },
                ]),
            { wrapper },
        );

        await waitFor(() => {
            expect(result.current.tasks.map((task) => task.id)).toStrictEqual(['first', 'second']);
        });

        act(() => {
            for (const listener of listeners) {
                listener({ kind: 'tasks.changed' });
            }
        });

        await waitFor(() => {
            expect(result.current.failure).toBe('unavailable');
        });
        expect(result.current.tasks.map((task) => task.id)).toStrictEqual(['first-again', 'first', 'second']);
    });
});
