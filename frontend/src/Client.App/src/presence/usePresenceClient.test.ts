// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { idleAfter, renewEvery, usePresenceClient } from './usePresenceClient';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Bearer presence' };

// The deployment as far as this hook reaches it: every request is recorded, and each is answered with the status the
// test decided — a renewal is `204` unless the test says the member belongs to no organization.
function deployment(status = 204): { readonly transport: MailFathomTransport; readonly requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve<ClientResponse>({ status, body: '', headers: {} });
        },
    };
}

function renewals(requests: readonly ClientRequest[]): readonly string[] {
    return requests
        .filter((request) => request.method === 'PUT')
        .map((request) => (JSON.parse(request.body ?? '{}') as { activity: string }).activity);
}

describe('usePresenceClient', () => {
    beforeEach(() => {
        vi.useFakeTimers();
        vi.setSystemTime(Date.parse('2026-10-31T09:00:00Z'));
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('says the client is open and in use as soon as there is a session, and again every minute', async () => {
        const { transport, requests } = deployment();

        renderHook(() => usePresenceClient(session, transport));

        expect(renewals(requests)).toEqual(['active']);

        await act(async () => {
            await vi.advanceTimersByTimeAsync(renewEvery * 2);
        });

        expect(renewals(requests)).toEqual(['active', 'active', 'active']);
    });

    it('renews under one identity it minted for itself, as a version 7 UUID', async () => {
        const { transport, requests } = deployment();

        renderHook(() => usePresenceClient(session, transport));

        await act(async () => {
            await vi.advanceTimersByTimeAsync(renewEvery);
        });

        const [first, second] = requests.map((request) => request.path);

        expect(first).toMatch(/\/api\/client\/presence\/clients\/[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-/u);
        expect(second).toBe(first);
    });

    it('counts itself idle once ten minutes pass with no input, and says so on the next renewal', async () => {
        const { transport, requests } = deployment();

        renderHook(() => usePresenceClient(session, transport));

        await act(async () => {
            await vi.advanceTimersByTimeAsync(idleAfter - renewEvery);
        });

        expect(renewals(requests).at(-1)).toBe('active');

        await act(async () => {
            await vi.advanceTimersByTimeAsync(renewEvery);
        });

        expect(renewals(requests).at(-1)).toBe('idle');
    });

    it('renews at once when input returns to an idle client, rather than waiting out the minute', async () => {
        const { transport, requests } = deployment();

        renderHook(() => usePresenceClient(session, transport));

        await act(async () => {
            await vi.advanceTimersByTimeAsync(idleAfter);
        });

        const before = renewals(requests).length;

        fireEvent.keyDown(document, { key: 'a' });

        expect(renewals(requests).slice(before)).toEqual(['active']);
    });

    it('does not renew on every key while the client is already in use', () => {
        const { transport, requests } = deployment();

        renderHook(() => usePresenceClient(session, transport));
        fireEvent.pointerMove(document);
        fireEvent.keyDown(document, { key: 'a' });

        expect(renewals(requests)).toEqual(['active']);
    });

    it('stops renewing for a member of no organization, whom nobody could see', async () => {
        const { transport, requests } = deployment(404);

        renderHook(() => usePresenceClient(session, transport));

        await act(async () => {
            await vi.advanceTimersByTimeAsync(renewEvery * 3);
        });

        expect(renewals(requests)).toEqual(['active']);
    });

    it('says the client has closed as the page goes, in a request the browser carries past the page', () => {
        const { transport, requests } = deployment();

        renderHook(() => usePresenceClient(session, transport));
        fireEvent(window, new Event('pagehide'));

        expect(requests.at(-1)).toMatchObject({ method: 'DELETE', outlivesThePage: true });
        expect(requests.at(-1)?.path).toBe(requests[0]?.path);
    });

    it('says the client has closed when its person signs out, and renews nothing afterwards', async () => {
        const { transport, requests } = deployment();
        const { result } = renderHook(() => usePresenceClient(session, transport));

        act(() => {
            result.current.close();
        });

        await act(async () => {
            await vi.advanceTimersByTimeAsync(renewEvery * 2);
        });

        expect(requests.map((request) => request.method)).toEqual(['PUT', 'DELETE']);
    });

    it('renews nothing where there is no session to renew with', async () => {
        const { transport, requests } = deployment();

        renderHook(() => usePresenceClient(null, transport));

        await act(async () => {
            await vi.advanceTimersByTimeAsync(renewEvery * 2);
        });

        expect(requests).toEqual([]);
    });
});
