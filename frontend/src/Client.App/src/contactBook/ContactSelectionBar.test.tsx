// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { Colleague, Contact } from '@mailfathom/client-backend';
import { ComposingContext, type Composing } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { ToastContext } from '../toasts/useToasts';
import { ContactSelectionBar } from './ContactSelectionBar';
import type { ContactBookName, Person } from './useContactBook';

function personCalled(id: string, displayName: string): Contact {
    return {
        id,
        displayName,
        addresses: [`${id}@contoso.example`],
        preferredAddress: `${id}@contoso.example`,
        note: null,
        phoneNumbers: [],
        origin: 'Asserted',
        book: 'Own',
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
        photo: null,
        organization: null,
    };
}

const picked = [personCalled('anna', 'Anna Kowalska'), personCalled('bartek', 'Bartek Nowak')];

function colleagueCalled(id: string, displayName: string): Colleague {
    return {
        id,
        book: 'Member',
        displayName,
        addresses: [`${id}@nordwind.example`],
        preferredAddress: `${id}@nordwind.example`,
        phoneNumbers: [],
        jobTitle: null,
        organizationName: 'Nordwind',
        timeZone: null,
        workingHours: null,
        photo: null,
        presence: 'Busy',
        lastSeen: null,
        outOfOfficeUntil: null,
        usesPhoneApp: false,
    };
}

function composingWhere(offered: boolean, compose = vi.fn()): Composing {
    return { offered, drafts: false, opening: null, compose, close: vi.fn() };
}

function drawBar({
    book = 'own',
    selected = picked,
    erasable = true,
    composing = composingWhere(true),
    onClear = vi.fn(),
    onSelectAll = vi.fn(),
    onAskErasure = vi.fn(),
}: {
    book?: ContactBookName;
    selected?: readonly Person[];
    erasable?: boolean;
    composing?: Composing;
    onClear?: () => void;
    onSelectAll?: () => void;
    onAskErasure?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <ToastContext value={{ raise: vi.fn(), raiseOperation: () => () => undefined }}>
                <ComposingContext value={composing}>
                    <ContactSelectionBar
                        book={book}
                        selected={selected}
                        erasable={erasable}
                        onClear={onClear}
                        onSelectAll={onSelectAll}
                        onAskErasure={onAskErasure}
                    />
                </ComposingContext>
            </ToastContext>
        </LocalizationProvider>,
    );
}

describe('ContactSelectionBar', () => {
    it('says how many people are picked out, where the toolbar would otherwise stand', () => {
        drawBar();

        expect(screen.getByRole('toolbar', { name: 'Selected contacts' })).toBeDefined();
        expect(screen.getByText('2 selected')).toBeDefined();
    });

    it('opens one message addressed to everybody picked out rather than one message each', () => {
        const compose = vi.fn();
        drawBar({ composing: composingWhere(true, compose) });

        fireEvent.click(screen.getByRole('button', { name: 'Write' }));

        expect(compose).toHaveBeenCalledWith({
            kind: 'new',
            to: ['anna@contoso.example', 'bartek@contoso.example'],
        });
    });

    it('leaves the selection rather than acting on it when the way out is pressed', () => {
        const onClear = vi.fn();
        const onAskErasure = vi.fn();
        drawBar({ onClear, onAskErasure });

        fireEvent.click(screen.getByRole('button', { name: 'Clear the selection' }));

        expect(onClear).toHaveBeenCalledTimes(1);
        expect(onAskErasure).not.toHaveBeenCalled();
    });

    // Laid out as the mail list's bar: the way out and the count first, and taking the whole book in at the far end.
    it('draws the way out first and picking out everybody last', () => {
        const onSelectAll = vi.fn();
        drawBar({ onSelectAll });

        const controls = screen.getAllByRole('button');

        expect(controls[0]).toBe(screen.getByRole('button', { name: 'Clear the selection' }));
        expect(controls.at(-1)).toBe(screen.getByRole('button', { name: 'Select all' }));

        fireEvent.click(screen.getByRole('button', { name: 'Select all' }));

        expect(onSelectAll).toHaveBeenCalledTimes(1);
    });

    it.each([
        ['own', 'Delete', 'Remove'],
        ['collected', 'Remove', 'Delete'],
    ] as const)('offers writing and the %s book’s own way of taking people out of it', (book, offered, absent) => {
        const onAskErasure = vi.fn();
        drawBar({ book, onAskErasure });

        fireEvent.click(screen.getByRole('button', { name: offered }));

        expect(onAskErasure).toHaveBeenCalledTimes(1);
        expect(screen.getByRole('button', { name: 'Write' })).toBeDefined();
        expect(screen.queryByRole('button', { name: absent })).toBeNull();
        expect(screen.queryByRole('button', { name: /export/i })).toBeNull();
    });

    // Nobody here may change a colleague, so copying their addresses stands where taking them out would.
    it('offers writing and copying their addresses under Colleagues, and nothing that takes anybody out', () => {
        const writeText = vi.fn(() => Promise.resolve());
        Object.defineProperty(window.navigator, 'clipboard', { configurable: true, value: { writeText } });
        drawBar({
            book: 'colleagues',
            selected: [colleagueCalled('piotr', 'Piotr Zieliński'), colleagueCalled('marta', 'Marta Nowak')],
        });

        fireEvent.click(screen.getByRole('button', { name: 'Copy addresses' }));

        expect(writeText).toHaveBeenCalledWith('piotr@nordwind.example, marta@nordwind.example');
        expect(screen.getByRole('button', { name: 'Write' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();
        expect(screen.queryByRole('button', { name: 'Remove' })).toBeNull();
        Reflect.deleteProperty(window.navigator, 'clipboard');
    });

    it('offers no deletion while every contact picked out is one the organization keeps', () => {
        drawBar({ selected: picked.map((contact) => ({ ...contact, book: 'Organization' as const })) });

        expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Write' })).toBeDefined();
    });

    it('offers no deletion where the credential may not change the book', () => {
        drawBar({ erasable: false });

        expect(screen.queryByRole('button', { name: 'Delete' })).toBeNull();
    });
});
