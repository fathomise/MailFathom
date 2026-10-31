// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { ClientResponse, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { useColleagueStructure } from './useColleagueStructure';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function structureReporting(displayName: string): string {
    return JSON.stringify({
        managers: [],
        directReports: [
            {
                id: `${displayName}-id`,
                book: 'Member',
                displayName,
                addresses: [],
                preferredAddress: null,
                phoneNumbers: [],
                jobTitle: null,
                organizationName: 'Nordwind',
                timeZone: null,
                photo: null,
            },
        ],
        readerId: 'reader',
    });
}

function Structured({
    transport,
    colleagueId,
}: {
    readonly transport: MailFathomTransport;
    readonly colleagueId: string;
}) {
    const held = useColleagueStructure(session, transport, colleagueId);

    return (
        <div>
            <p>{held.reading ? 'reading' : 'settled'}</p>
            <ul>
                {(held.structure?.directReports ?? []).map((report) => (
                    <li key={report.id}>{report.displayName}</li>
                ))}
            </ul>
        </div>
    );
}

describe('useColleagueStructure', () => {
    it('draws the colleague opened second when the answer for the one left behind arrives after it', async () => {
        const pending = new Map<string, (response: ClientResponse) => void>();
        const transport: MailFathomTransport = ({ path }) =>
            new Promise((resolve) => {
                pending.set(path.includes('/anna/') ? 'anna' : 'bartek', resolve);
            });

        const drawn = render(<Structured transport={transport} colleagueId="anna" />);
        drawn.rerender(<Structured transport={transport} colleagueId="bartek" />);

        await act(async () => {
            pending.get('bartek')?.({ status: 200, body: structureReporting('Hanna Mazur'), headers: {} });
            await Promise.resolve();
        });
        expect(await screen.findByText('Hanna Mazur')).toBeDefined();

        await act(async () => {
            pending.get('anna')?.({ status: 200, body: structureReporting('Oliver Brandt'), headers: {} });
            await Promise.resolve();
        });

        expect(screen.getByText('Hanna Mazur')).toBeDefined();
        expect(screen.queryByText('Oliver Brandt')).toBeNull();
        expect(screen.getByText('settled')).toBeDefined();
    });
});
