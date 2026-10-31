// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
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

// Piotr is away and his address is also contained in Apiotr's, which a search by his address finds beside him; Zofia is
// in the office.
const directory = [
    member('member-apiotr', 'Adam Piotrowski', 'apiotr@nordwind.example', '2026-11-20T12:00:00Z'),
    member('member-piotr', 'Piotr Zieliński', 'piotr@nordwind.example', '2026-11-04T12:00:00Z'),
    member('member-zofia', 'Zofia Krawczyk', 'zofia@nordwind.example', null),
];

/** A directory searched by any part of an address, as the deployment searches it. */
function deployment(): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            const search = new URL(request.path).searchParams.get('search') ?? '';
            const contacts = directory.filter((colleague) => colleague.addresses.some((held) => held.includes(search)));

            return Promise.resolve({ status: 200, body: JSON.stringify({ contacts, nextCursor: null }), headers: {} });
        },
    };
}

function drawWarnings(addresses: readonly string[], transport: MailFathomTransport) {
    return render(
        <LocalizationProvider>
            <AbsentRecipients addresses={addresses} session={session} transport={transport} />
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

    it('asks the directory about an address once, however often the recipients change around it', async () => {
        const { transport, asked } = deployment();
        const { rerender } = drawWarnings(['piotr@nordwind.example'], transport);

        await screen.findByRole('status');

        rerender(
            <LocalizationProvider>
                <AbsentRecipients
                    addresses={['piotr@nordwind.example', 'zofia@nordwind.example']}
                    session={session}
                    transport={transport}
                />
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
