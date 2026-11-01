// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { newsletterId, newsletterMessage } from '../../../../tests/fixtures/messages';
import { LocalizationProvider } from '../localization/Localization';
import { ReadingZoneContext } from '../localization/useReadingZone';
import { ReceiptDetails, type AskedReceipts } from './ReceiptDetails';

// What the harness's own control says, held as a value rather than written into the markup.
const opening = 'Explain';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function recorded(outcome: string, reportedAt: string, over: Record<string, unknown> = {}) {
    return { outcome, reportedAt, source: 'Report', status: null, reportingServer: null, ...over };
}

/** One message the account sent, with a recipient for every line the details can draw about one. */
const sent = {
    ...newsletterMessage,
    receipts: {
        deliveryRequested: true,
        readRequested: true,
        recipients: [
            {
                address: 'r.lis@northwind.example',
                delivery: recorded('Failed', '2026-08-25T08:15:00+00:00', {
                    status: '5.1.1',
                    reportingServer: 'mx2.northwind.example',
                }),
                read: null,
            },
            {
                address: 't.bak@fabrikam.example',
                delivery: recorded('Delayed', '2026-08-25T08:15:00+00:00'),
                read: null,
            },
            {
                address: 'a.kowalska@contoso.example',
                delivery: recorded('Delivered', '2026-08-26T14:21:00+00:00', {
                    reportingServer: 'mx1.contoso.example',
                }),
                read: recorded('Displayed', '2026-08-26T14:48:00+00:00'),
            },
            {
                address: 'j.wrona@lawoffice.example',
                delivery: recorded('Delivered', '2026-08-26T14:22:00+00:00'),
                read: null,
            },
            { address: 'e.sikora@example.invalid', delivery: null, read: null },
            {
                address: 'p.zielinski@company.example',
                delivery: recorded('Delivered', '2026-08-26T11:02:00+00:00', { source: 'Organization' }),
                read: recorded('Displayed', '2026-08-26T11:40:00+00:00', { source: 'Organization' }),
            },
        ],
    },
};

const answered: MailFathomTransport = () =>
    Promise.resolve<ClientResponse>({ status: 200, body: JSON.stringify(sent), headers: {} });

const fromRow: AskedReceipts = { storedEmailId: newsletterId, message: null, anchor: null };

function Explaining() {
    const dialog = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    dialog.current?.showModal();
                }}
            >
                {opening}
            </button>

            <ReceiptDetails asked={fromRow} session={session} transport={answered} dialog={dialog} />
        </>
    );
}

// The details are read a week after the reports, in Warsaw, so each line below is the literal spelling that zone
// produces. Only the clock's date is fixed: the read the details make is awaited on timers, which stay real.
beforeEach(() => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-09-07T10:00:00+00:00'));
});

afterEach(() => {
    vi.useRealTimers();
});

/** What the details say about one recipient, line by line, once they have read the message. */
async function linesAbout(address: string): Promise<string> {
    render(
        <LocalizationProvider>
            <ReadingZoneContext value="Europe/Warsaw">
                <Explaining />
            </ReadingZoneContext>
        </LocalizationProvider>,
    );
    fireEvent.click(screen.getByRole('button', { name: opening }));

    const recipient = (await screen.findByText(address)).closest('li');

    if (recipient === null) {
        throw new Error(`The details drew no row for ${address}.`);
    }

    return [...recipient.querySelectorAll('p')].map((line) => line.textContent).join('\n');
}

describe('ReceiptDetails', () => {
    it('gives a failure the status code its report carried, then who reported it and when', async () => {
        expect(await linesAbout('r.lis@northwind.example')).toBe(
            ['The server reported status 5.1.1.', 'Reported 8/25/26, 10:15 AM · mx2.northwind.example'].join('\n'),
        );
    });

    it('says when a delay was reported where its report named no status and no server', async () => {
        expect(await linesAbout('t.bak@fabrikam.example')).toBe(
            ['Reported 8/25/26, 10:15 AM', 'No read receipt back yet.'].join('\n'),
        );
    });

    it('says when a recipient displayed the message and where it was delivered', async () => {
        expect(await linesAbout('a.kowalska@contoso.example')).toBe(
            [
                'Displayed 8/26/26, 4:48 PM · their read receipt',
                'Delivered 8/26/26, 4:21 PM · mx1.contoso.example',
            ].join('\n'),
        );
    });

    it('says a read receipt asked for has not come back for a recipient it was delivered to', async () => {
        expect(await linesAbout('j.wrona@lawoffice.example')).toBe(
            ['Delivered 8/26/26, 4:22 PM', 'No read receipt back yet.'].join('\n'),
        );
    });

    it('says each thing asked for that has not come back for a recipient nothing came back for', async () => {
        expect(await linesAbout('e.sikora@example.invalid')).toBe(
            ['No delivery notification back yet.', 'No read receipt back yet.'].join('\n'),
        );
    });

    it('says a colleague was marked by MailFathom itself, with nothing owed back', async () => {
        expect(await linesAbout('p.zielinski@company.example')).toBe(
            [
                'Opened 8/26/26, 1:40 PM · in MailFathom',
                'Delivered 8/26/26, 1:02 PM · to their mailbox in your organization',
            ].join('\n'),
        );
    });
});
