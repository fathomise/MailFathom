// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Colleague, ColleagueStructure } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { OrganizationCard } from './OrganizationCard';
import type { ColleagueStructureInForce } from './useColleagueStructure';

function colleagueCalled(
    id: string,
    displayName: string,
    jobTitle: string | null = null,
    presence: Colleague['presence'] = 'Active',
): Colleague {
    return {
        id,
        book: 'Member',
        displayName,
        addresses: [],
        preferredAddress: null,
        phoneNumbers: [],
        jobTitle,
        organizationName: 'Nordwind',
        timeZone: null,
        workingHours: null,
        photo: null,
        presence,
        lastSeen: null,
        outOfOfficeUntil: null,
    };
}

const zofia = colleagueCalled('zofia', 'Zofia Nowak', 'Managing Director', 'Busy');
const anna = colleagueCalled('anna', 'Anna Kowalska');
const piotr = colleagueCalled('piotr', 'Piotr Zieliński', 'Head of Delivery');
const hanna = colleagueCalled('hanna', 'Hanna Wiśniewska');
const oliver = colleagueCalled('oliver', 'Oliver Brandt');

const structure: ColleagueStructure = {
    managers: [zofia, anna],
    directReports: [hanna, oliver],
    readerId: 'anna',
};

const read: ColleagueStructureInForce = { structure, reading: false, failure: null, readAgain: () => undefined };

function drawCard(reading: ColleagueStructureInForce = read, onOpen = vi.fn()): void {
    render(
        <LocalizationProvider>
            <OrganizationCard colleague={piotr} reading={reading} onOpen={onOpen} />
        </LocalizationProvider>,
    );
}

describe('OrganizationCard', () => {
    it('draws the managers from the top down to the colleague, and the colleague marked as the one open', () => {
        drawCard();

        const [chainList] = screen.getAllByRole('list');
        const chain = within(chainList ?? document.body);

        expect(chain.getAllByRole('listitem').map((row) => row.textContent)).toEqual([
            'ZNZofia NowakManaging Director · Busy',
            'AKAnna KowalskaActive',
            'PZPiotr ZielińskiHead of Delivery · Active',
        ]);
        expect(chain.getByText('Piotr Zieliński').closest('[aria-current="true"]')).not.toBeNull();
    });

    it('lists the direct reports under their own heading', () => {
        drawCard();

        expect(screen.getByRole('heading', { name: 'Direct reports' })).toBeDefined();
        expect(screen.getByRole('button', { name: /Hanna Wiśniewska/u })).toBeDefined();
        expect(screen.getByRole('button', { name: /Oliver Brandt/u })).toBeDefined();
    });

    it('says the colleague has no direct reports where nobody names them', () => {
        drawCard({ ...read, structure: { ...structure, directReports: [] } });

        expect(screen.getByText('No direct reports')).toBeDefined();
    });

    it('opens a member of the structure, and leads nowhere from the colleague already open or from the reader', () => {
        const onOpen = vi.fn();
        drawCard(read, onOpen);

        fireEvent.click(screen.getByRole('button', { name: /Zofia Nowak/u }));

        expect(onOpen).toHaveBeenCalledWith(zofia);
        expect(screen.queryByRole('button', { name: /Anna Kowalska/u })).toBeNull();
        expect(screen.queryByRole('button', { name: /Piotr Zieliński/u })).toBeNull();
    });

    it('says it is reading while the structure is on its way', () => {
        drawCard({ structure: null, reading: true, failure: null, readAgain: () => undefined });

        expect(screen.getByRole('status').textContent).toBe('Reading…');
    });

    it('says why the structure could not be read and offers to read it again', () => {
        const readAgain = vi.fn();
        drawCard({ structure: null, reading: false, failure: 'unavailable', readAgain });

        expect(within(screen.getByRole('alert')).getByText('The server did not answer.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(readAgain).toHaveBeenCalledOnce();
    });

    it('offers nothing to retry for a colleague who is no longer a member', () => {
        drawCard({ structure: null, reading: false, failure: 'missing', readAgain: () => undefined });

        expect(screen.getByRole('alert').textContent).toBe('This person is no longer a member of your organization.');
        expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();
    });
});
