// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, MailFathomTransport } from '@mailfathom/client-backend';
import { useOpeningReport } from './useOpeningReport';

const session = { baseAddress: 'https://mail.example.invalid', authorization: 'Bearer mfs_abcdef.ghijkl' };

function recording(asked: ClientRequest[], status = 204): MailFathomTransport {
    return (request) => {
        asked.push(request);

        return Promise.resolve({ status, body: '', headers: {} });
    };
}

function returnToView(): void {
    document.dispatchEvent(new Event('visibilitychange'));
}

function seen(visibility: DocumentVisibilityState): void {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: visibility });
}

afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(document, 'visibilityState');
});

describe('useOpeningReport', () => {
    it('reports an opening when a signed-in client starts, and not again on returning to view the same day', async () => {
        const asked: ClientRequest[] = [];

        renderHook(() => {
            useOpeningReport(session, recording(asked));
        });
        await waitFor(() => {
            expect(asked.map((request) => request.path)).toEqual([
                'https://mail.example.invalid/api/client/session/opening',
            ]);
        });

        returnToView();

        expect(asked).toHaveLength(1);
    });

    it('reports again on returning to view on a later day', async () => {
        vi.useFakeTimers({ toFake: ['Date'] });
        vi.setSystemTime(new Date('2026-11-01T22:00:00Z'));
        const asked: ClientRequest[] = [];

        renderHook(() => {
            useOpeningReport(session, recording(asked));
        });
        vi.setSystemTime(new Date('2026-11-02T07:00:00Z'));
        returnToView();

        await waitFor(() => {
            expect(asked).toHaveLength(2);
        });
    });

    it('says it again on the next return to view when the deployment never heard the first', async () => {
        const asked: ClientRequest[] = [];

        renderHook(() => {
            useOpeningReport(session, recording(asked, 503));
        });
        await waitFor(() => {
            expect(asked).toHaveLength(1);
        });

        returnToView();

        await waitFor(() => {
            expect(asked).toHaveLength(2);
        });
    });

    it('reports nothing while the client is out of view, and reports once it comes back', async () => {
        const asked: ClientRequest[] = [];
        seen('hidden');

        renderHook(() => {
            useOpeningReport(session, recording(asked));
        });

        expect(asked).toEqual([]);

        seen('visible');
        returnToView();

        await waitFor(() => {
            expect(asked).toHaveLength(1);
        });
    });

    it('reports nothing while nobody is signed in', () => {
        const asked: ClientRequest[] = [];

        renderHook(() => {
            useOpeningReport(null, recording(asked));
        });

        expect(asked).toEqual([]);
    });
});
