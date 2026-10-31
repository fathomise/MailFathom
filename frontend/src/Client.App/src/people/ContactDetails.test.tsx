// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Contact } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { LinkOpenerContext, type OpenLink } from '../shellOperations/linkOpener';
import { ContactDetails } from './ContactDetails';

const anna: Contact = {
    id: 'anna',
    displayName: 'Anna Kowalska',
    addresses: ['anna@contoso.example', 'a.kowalska@contoso.example'],
    preferredAddress: 'anna@contoso.example',
    note: null,
    phoneNumbers: [
        { number: '+1 202 555 0143', label: 'Mobile' },
        { number: '+1 (202) 555-01.78', label: 'Work' },
    ],
    origin: 'Asserted',
    recordedAt: '2026-08-31T09:41:00+00:00',
    amendedAt: '2026-08-31T09:41:00+00:00',
    photo: null,
    organization: null,
};

function drawDetails(contact: Contact, openLink: OpenLink = () => Promise.resolve()): void {
    render(
        <LocalizationProvider>
            <LinkOpenerContext value={openLink}>
                <ContactDetails contact={contact} />
            </LinkOpenerContext>
        </LocalizationProvider>,
    );
}

describe('ContactDetails', () => {
    it('marks the preferred address where the person uses more than one', () => {
        drawDetails(anna);

        expect(screen.getByText('anna@contoso.example').parentElement?.textContent).toContain('Preferred');
        expect(screen.getByText('a.kowalska@contoso.example').parentElement?.textContent).not.toContain('Preferred');
    });

    it('offers every number as a link a phone can call, keeping only the digits and the plus', () => {
        drawDetails(anna);

        expect(screen.getByRole('link', { name: '+1 202 555 0143' }).getAttribute('href')).toBe('tel:+12025550143');
        expect(screen.getByRole('link', { name: '+1 (202) 555-01.78' }).getAttribute('href')).toBe('tel:+12025550178');
        expect(screen.getByText('Work')).toBeDefined();
    });

    // A WebView that navigates has replaced the application, so the press asks the head rather than following the
    // anchor.
    it('opens a number through the operation for leaving the application rather than by navigating', () => {
        const openLink = vi.fn<OpenLink>(() => Promise.resolve());
        drawDetails(anna, openLink);

        const pressed = fireEvent.click(screen.getByRole('link', { name: '+1 202 555 0143' }));

        expect(pressed).toBe(false);
        expect(openLink).toHaveBeenCalledWith({
            target: 'tel:+12025550143',
            picture: false,
            words: '+1 202 555 0143',
            warnings: [],
        });
    });

    it('says a number could not be opened where the head refused it', async () => {
        drawDetails(anna, () => Promise.reject(new Error('Nothing on this machine dials.')));

        fireEvent.click(screen.getByRole('link', { name: '+1 202 555 0143' }));

        expect((await screen.findByRole('status')).textContent).toBe('This link could not be opened.');
    });

    it('says a contact somebody wrote down holds no number yet', () => {
        drawDetails({ ...anna, phoneNumbers: [] });

        expect(screen.getByText('No phone numbers')).toBeDefined();
    });

    // Nothing collected from mail headers carries a number, so the absence is not a gap worth reporting.
    it('says nothing about phones for a contact a mailbox collected', () => {
        drawDetails({ ...anna, origin: 'Collected', phoneNumbers: [] });

        expect(screen.queryByText('No phone numbers')).toBeNull();
    });
});
