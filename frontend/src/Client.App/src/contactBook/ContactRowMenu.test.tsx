// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Colleague, Contact } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { ToastContext, type Toast } from '../toasts/useToasts';
import { ContactRowMenu } from './ContactRowMenu';
import type { Person } from './useContactBook';

const anna: Contact = {
    id: 'anna',
    displayName: 'Anna Kowalska',
    addresses: ['anna@contoso.example'],
    preferredAddress: 'anna@contoso.example',
    note: null,
    phoneNumbers: [],
    origin: 'Asserted',
    book: 'Own',
    recordedAt: '2026-08-31T09:41:00+00:00',
    amendedAt: '2026-08-31T09:41:00+00:00',
    photo: null,
    organization: null,
};

const piotr: Colleague = {
    id: 'piotr',
    book: 'Member',
    displayName: 'Piotr Zieliński',
    addresses: ['p.zielinski@nordwind.example'],
    preferredAddress: 'p.zielinski@nordwind.example',
    phoneNumbers: [],
    jobTitle: 'Head of Delivery',
    organizationName: 'Nordwind',
    timeZone: null,
    workingHours: null,
    photo: null,
    presence: 'Busy',
    lastSeen: null,
    outOfOfficeUntil: null,
    usesPhoneApp: false,
    statusMessage: null,
    workMode: null,
};

const declaredClipboard = Object.getOwnPropertyDescriptor(window.navigator, 'clipboard');

afterEach(() => {
    if (declaredClipboard === undefined) {
        Reflect.deleteProperty(window.navigator, 'clipboard');
    } else {
        Object.defineProperty(window.navigator, 'clipboard', declaredClipboard);
    }
});

function composingWhere(offered: boolean, compose = vi.fn()): Composing {
    return { offered, drafts: false, opening: null, compose, close: vi.fn() };
}

function menuUnder({
    contact = anna,
    composing = composingWhere(true),
    erasable = true,
    onOpen = vi.fn(),
    onSelect = vi.fn(),
    onAskErasure = vi.fn(),
    raise = vi.fn(),
}: {
    contact?: Person;
    composing?: Composing;
    erasable?: boolean;
    onOpen?: () => void;
    onSelect?: () => void;
    onAskErasure?: (contact: Contact) => void;
    raise?: (toast: Toast) => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ToastContext value={{ raise, raiseOperation: () => () => undefined }}>
                <ComposingContext value={composing}>
                    <ContactRowMenu
                        contact={contact}
                        at={{ x: 20, y: 30 }}
                        erasable={erasable}
                        onSelect={onSelect}
                        onOpen={onOpen}
                        onAskErasure={onAskErasure}
                        onClose={vi.fn()}
                    />
                </ComposingContext>
            </ToastContext>
        </LocalizationProvider>,
    );
}

function drawn(): (string | null)[] {
    return screen.getAllByRole('menuitem').map((item) => item.textContent);
}

describe('ContactRowMenu', () => {
    it('draws the acts this deployment publishes a route for, in the order the design draws them', () => {
        menuUnder();

        expect(drawn()).toStrictEqual(['Select contacts', 'Write a message', 'Open contact', 'Delete contact']);
    });

    it('leaves writing out where the credential may not write a message at all', () => {
        menuUnder({ composing: composingWhere(false) });

        expect(drawn()).toStrictEqual(['Select contacts', 'Open contact', 'Delete contact']);
    });

    it('leaves deleting out where the credential may not change the book', () => {
        menuUnder({ erasable: false });

        expect(drawn()).toStrictEqual(['Select contacts', 'Write a message', 'Open contact']);
    });

    // Said as the person's page says it: a collected person is removed from the collected book rather than deleted.
    it('offers removing a collected person from the collected book rather than deleting them', () => {
        menuUnder({ contact: { ...anna, origin: 'Collected', book: 'Collected' } });

        expect(drawn()).toStrictEqual(['Select contacts', 'Write a message', 'Open contact', 'Remove from collected']);
    });

    // Nobody here may change a colleague or a contact the organization keeps, so their address stands where deleting
    // them would.
    it("offers a colleague's address to copy and nothing that would change them", () => {
        menuUnder({ contact: piotr });

        expect(drawn()).toStrictEqual(['Select contacts', 'Write an email', 'Open contact', 'Copy address']);
    });

    // The organization's book is read-only to a member, so its contact is offered no deletion however much the
    // credential could write to the reader's own book.
    it('offers no deletion of a contact the organization keeps, only its address', () => {
        menuUnder({ contact: { ...anna, book: 'Organization', organization: '0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f' } });

        expect(drawn()).toStrictEqual(['Select contacts', 'Write a message', 'Open contact', 'Copy address']);
    });

    it('offers neither writing nor copying for a colleague who holds no address', () => {
        menuUnder({ contact: { ...piotr, addresses: [], preferredAddress: null } });

        expect(drawn()).toStrictEqual(['Select contacts', 'Open contact']);
    });

    it("puts the colleague's address on the clipboard and says it did", async () => {
        const writeText = vi.fn(() => Promise.resolve());
        Object.defineProperty(window.navigator, 'clipboard', { configurable: true, value: { writeText } });
        const raise = vi.fn();
        menuUnder({ contact: piotr, raise });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Copy address' }));

        expect(writeText).toHaveBeenCalledWith('p.zielinski@nordwind.example');
        await waitFor(() => {
            expect(raise).toHaveBeenCalledWith(
                { kind: 'success', title: 'Address copied', body: 'p.zielinski@nordwind.example' },
                expect.any(Number),
            );
        });
    });

    it('says a copy did not land where the runtime offers no clipboard', async () => {
        Reflect.deleteProperty(window.navigator, 'clipboard');
        const raise = vi.fn();
        menuUnder({ contact: piotr, raise });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Copy address' }));

        await waitFor(() => {
            expect(raise).toHaveBeenCalledWith(
                { kind: 'error', title: 'That could not be copied to the clipboard.' },
                expect.any(Number),
            );
        });
    });

    it('opens a message addressed to the person the menu is about', () => {
        const compose = vi.fn();
        menuUnder({ composing: composingWhere(true, compose) });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Write a message' }));

        expect(compose).toHaveBeenCalledWith({ kind: 'new', to: ['anna@contoso.example'] });
    });

    it('raises the question an erasure stands behind rather than erasing on the press', () => {
        const onAskErasure = vi.fn();
        menuUnder({ onAskErasure });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete contact' }));

        expect(onAskErasure).toHaveBeenCalledTimes(1);
    });
});
