// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { renderHook, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import type { CalendarSpan } from './calendarSpan';
import { useCalendarWindow } from './useCalendarWindow';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function eventCalled(id: string, origin: string): unknown {
    return {
        id,
        title: id,
        start: '2026-09-24T09:00:00+02:00',
        end: null,
        isAllDay: false,
        reminders: [],
        remindsAt: [],
        origin,
        sourceMessage: null,
        recordedAt: '2026-09-01T09:00:00+02:00',
        amendedAt: '2026-09-01T09:00:00+02:00',
    };
}

const thisWeek: CalendarSpan = { from: new Date(2026, 8, 21), until: new Date(2026, 8, 28) };
const nextWeek: CalendarSpan = { from: new Date(2026, 8, 28), until: new Date(2026, 9, 5) };

describe('useCalendarWindow', () => {
    // The grid keeps drawing what it held while the next span is read, but a proposal stands beside the grid under the
    // new span's heading and can be accepted from there, so one read for another span is not offered as this one's.
    it('holds the calendar over a span being read, and none of the proposals from the span before', async () => {
        const first: ClientResponse = {
            status: 200,
            headers: {},
            body: JSON.stringify({ events: [eventCalled('review', 'Asserted'), eventCalled('lunch', 'Proposed')] }),
        };
        const transport: MailFathomTransport = (request) =>
            request.path.includes(encodeURIComponent(thisWeek.from.toISOString()))
                ? Promise.resolve(first)
                : new Promise<ClientResponse>(() => undefined);

        const { result, rerender } = renderHook(({ span }) => useCalendarWindow(session, transport, span), {
            initialProps: { span: thisWeek },
        });

        await waitFor(() => {
            expect(result.current.proposals).toHaveLength(1);
        });

        rerender({ span: nextWeek });

        expect(result.current.reading).toBe(true);
        expect(result.current.events.map((event) => event.id)).toStrictEqual(['review']);
        expect(result.current.proposals).toStrictEqual([]);
    });
});
