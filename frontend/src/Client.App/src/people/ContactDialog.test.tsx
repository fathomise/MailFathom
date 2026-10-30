// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ContactDialog, type ContactDraft, type ContactStatement } from './ContactDialog';

// What opens the dialog in the test, named here rather than written into the markup: the localization rule
// holds over every `.tsx` under the packages, and a label a test invented is not a catalogue entry.
const asks = 'Ask';

function Asking({
    amending,
    onSave,
}: {
    readonly amending: ContactDraft | null;
    readonly onSave: (statement: ContactStatement) => void;
}) {
    const asked = useRef<HTMLDialogElement>(null);

    return (
        <>
            <button
                type="button"
                onClick={() => {
                    asked.current?.showModal();
                }}
            >
                {asks}
            </button>

            <ContactDialog asked={asked} amending={amending} onSave={onSave} />
        </>
    );
}

function drawForm(amending: ContactDraft | null = null): { readonly saved: ReturnType<typeof vi.fn> } {
    const saved = vi.fn();

    render(
        <LocalizationProvider>
            <Asking amending={amending} onSave={saved} />
        </LocalizationProvider>,
    );

    fireEvent.click(screen.getByRole('button', { name: asks }));

    return { saved };
}

function typeInto(field: HTMLElement, text: string): void {
    fireEvent.change(field, { target: { value: text } });
}

function addressRows(): HTMLInputElement[] {
    return screen.getAllByLabelText<HTMLInputElement>('Email address');
}

function nth<T>(items: readonly T[], at: number): T {
    const item = items[at];

    if (item === undefined) {
        throw new Error(`Nothing is drawn at position ${String(at)}.`);
    }

    return item;
}

const anna: ContactDraft = {
    displayName: 'Anna Kowalska',
    rows: { addresses: ['anna@contoso.example', 'a.kowalska@fabrikam.example'], preferred: 0 },
};

describe('ContactDialog', () => {
    // A name and addresses are what a contact record holds on this deployment; a company and a role are not fields of
    // it, so a form offering them would be asking for values no request could carry.
    it('asks for a name and the addresses a record this client writes actually holds', () => {
        drawForm();

        expect(screen.getByLabelText('Name')).toBeDefined();
        expect(screen.getByRole('group', { name: 'Email addresses' })).toBeDefined();
        expect(addressRows()).toHaveLength(1);
        expect(screen.queryByLabelText('Company')).toBeNull();
    });

    it('records what was typed once the question has been answered', () => {
        const { saved } = drawForm();

        typeInto(screen.getByLabelText('Name'), '  Anna Kowalska  ');
        typeInto(nth(addressRows(), 0), ' anna@contoso.example ');
        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        expect(saved).toHaveBeenCalledWith({
            displayName: 'Anna Kowalska',
            addresses: ['anna@contoso.example'],
            preferredAddress: 'anna@contoso.example',
        });
    });

    it('records every address added, with the one chosen as the default and an empty row dropped', () => {
        const { saved } = drawForm();

        typeInto(screen.getByLabelText('Name'), 'Anna Kowalska');
        typeInto(nth(addressRows(), 0), 'anna@contoso.example');
        fireEvent.click(screen.getByRole('button', { name: 'Add an email address' }));
        typeInto(nth(addressRows(), 1), 'anna@home.example');
        fireEvent.click(screen.getByRole('button', { name: 'Add an email address' }));
        fireEvent.click(nth(screen.getAllByRole('radio'), 1));

        expect(screen.getByRole('radio', { name: 'Default address' })).toBe(nth(screen.getAllByRole('radio'), 1));

        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        expect(saved).toHaveBeenCalledWith({
            displayName: 'Anna Kowalska',
            addresses: ['anna@home.example', 'anna@contoso.example'],
            preferredAddress: 'anna@home.example',
        });
    });

    it('marks exactly one row the default, and offers removing a row only while there is more than one', () => {
        drawForm(anna);

        expect(screen.getAllByRole('radio', { checked: true })).toHaveLength(1);
        expect(screen.getAllByText('Default')).toHaveLength(1);
        expect(screen.getAllByRole('button', { name: 'Remove address' })).toHaveLength(2);

        fireEvent.click(nth(screen.getAllByRole('button', { name: 'Remove address' }), 0));

        expect(addressRows().map((row) => row.value)).toEqual(['a.kowalska@fabrikam.example']);
        expect(screen.getByRole('radio', { name: 'Default address' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Remove address' })).toBeNull();
    });

    it('records nothing at all where the question was left rather than answered', () => {
        const { saved } = drawForm();

        typeInto(screen.getByLabelText('Name'), 'Anna Kowalska');
        typeInto(nth(addressRows(), 0), 'anna@contoso.example');
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(saved).not.toHaveBeenCalled();
    });

    // Said only once there is something to judge, because a complaint under a field nobody has finished typing in is
    // the client complaining about a state it put them in.
    it('says an address is not one only once something has been typed into the field', () => {
        const { saved } = drawForm();

        typeInto(screen.getByLabelText('Name'), 'Anna Kowalska');
        expect(screen.queryByRole('alert')).toBeNull();

        typeInto(nth(addressRows(), 0), 'anna');
        expect(screen.getByRole('alert').textContent).toBe('That does not look like an email address.');

        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));
        expect(saved).not.toHaveBeenCalled();
    });

    it('empties the form on the way out, so the next question is a blank one', () => {
        const { saved } = drawForm();

        typeInto(screen.getByLabelText('Name'), 'Anna Kowalska');
        typeInto(nth(addressRows(), 0), 'anna@contoso.example');
        fireEvent.click(screen.getByRole('button', { name: 'Add an email address' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        expect(saved).toHaveBeenCalledTimes(1);

        fireEvent.click(screen.getByRole('button', { name: asks }));

        expect(screen.getByLabelText<HTMLInputElement>('Name').value).toBe('');
        expect(addressRows().map((row) => row.value)).toEqual(['']);
    });

    it('opens a correction on what the record holds and saves nothing until something in it changes', () => {
        const { saved } = drawForm(anna);

        expect(screen.getByRole('heading', { name: 'Edit contact' })).toBeDefined();
        expect(screen.getByLabelText<HTMLInputElement>('Name').value).toBe('Anna Kowalska');
        expect(addressRows().map((row) => row.value)).toEqual(anna.rows.addresses);
        expect(screen.getByRole('button', { name: 'Save changes' }).hasAttribute('disabled')).toBe(true);

        fireEvent.click(screen.getByRole('radio', { name: 'Make this the default address' }));
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        expect(saved).toHaveBeenCalledWith({
            displayName: 'Anna Kowalska',
            addresses: ['a.kowalska@fabrikam.example', 'anna@contoso.example'],
            preferredAddress: 'a.kowalska@fabrikam.example',
        });
    });
});
