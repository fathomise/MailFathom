// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadingZoneContext } from '../localization/useReadingZone';
import { AbsentRecipients } from './AbsentRecipients';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

function member(id: string, displayName: string, address: string, outOfOfficeUntil: string | null) {
    return {
        id,
        book: 'Member',
        displayName,
        addresses: [address],
        preferredAddress: address,
        phoneNumbers: [],
        jobTitle: null,
        organizationName: 'Nordwind',
        timeZone: null,
        photo: null,
        presence: 'Offline',
        lastSeen: null,
        outOfOfficeUntil,
    };
}

// Piotr is away and Zofia is in the office.
const directory = [
    member('member-piotr', 'Piotr Zieliński', 'piotr@nordwind.example', '2026-11-04T12:00:00Z'),
    member('member-zofia', 'Zofia Krawczyk', 'zofia@nordwind.example', null),
];

/**
 * A deployment whose reader keeps every colleague in their own book as well, so the directory's list holds nobody and
 * only a read by address finds a colleague — which is the case a search would miss.
 */
function deployment(): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            const url = new URL(request.path);

            if (!url.pathname.endsWith('/colleagues/by-address')) {
                return Promise.resolve({
                    status: 200,
                    body: JSON.stringify({ contacts: [], nextCursor: null }),
                    headers: {},
                });
            }

            const address = (url.searchParams.get('address') ?? '').toLowerCase();
            const colleague = directory.find((held) => held.addresses.includes(address));

            return Promise.resolve(
                colleague === undefined
                    ? { status: 404, body: '', headers: {} }
                    : { status: 200, body: JSON.stringify(colleague), headers: {} },
            );
        },
    };
}

function drawWarnings(addresses: readonly string[], transport: MailFathomTransport, readingZone = 'Europe/Warsaw') {
    return render(
        <LocalizationProvider>
            <ReadingZoneContext value={readingZone}>
                <AbsentRecipients addresses={addresses} session={session} transport={transport} />
            </ReadingZoneContext>
        </LocalizationProvider>,
    );
}

describe('AbsentRecipients', () => {
    it('warns once for each recipient who is a colleague out of office, and for nobody else', async () => {
        const { transport } = deployment();

        drawWarnings(['Piotr@nordwind.example', 'zofia@nordwind.example', 'someone@contoso.example'], transport);

        await waitFor(() => {
            expect(screen.getAllByRole('status').map((warning) => warning.textContent)).toStrictEqual([
                'Piotr Zieliński is out of office until 11/04.',
            ]);
        });
    });

    // That instant is already the next day far east of UTC, so the day said is the reader's rather than the server's.
    it('says the last day in the reader’s own zone', async () => {
        drawWarnings(['piotr@nordwind.example'], deployment().transport, 'Pacific/Kiritimati');

        expect((await screen.findByRole('status')).textContent).toBe('Piotr Zieliński is out of office until 11/05.');

        cleanup();
        drawWarnings(['piotr@nordwind.example'], deployment().transport, 'America/Los_Angeles');

        expect((await screen.findByRole('status')).textContent).toBe('Piotr Zieliński is out of office until 11/04.');
    });

    it('asks the directory about an address once, however often the recipients change around it', async () => {
        const { transport, asked } = deployment();
        const { rerender } = drawWarnings(['piotr@nordwind.example'], transport);

        await screen.findByRole('status');

        rerender(
            <LocalizationProvider>
                <ReadingZoneContext value="Europe/Warsaw">
                    <AbsentRecipients
                        addresses={['piotr@nordwind.example', 'zofia@nordwind.example']}
                        session={session}
                        transport={transport}
                    />
                </ReadingZoneContext>
            </LocalizationProvider>,
        );

        await waitFor(() => {
            expect(asked).toHaveLength(2);
        });
        expect(screen.getAllByRole('status')).toHaveLength(1);
    });

    it('says nothing where the directory does not answer', async () => {
        const asked: ClientRequest[] = [];
        const failing: MailFathomTransport = (request) => {
            asked.push(request);

            return Promise.resolve({ status: 503, body: '', headers: {} });
        };

        drawWarnings(['piotr@nordwind.example'], failing);

        await waitFor(() => {
            expect(asked).toHaveLength(1);
        });
        expect(screen.queryByRole('status')).toBeNull();
    });
});
