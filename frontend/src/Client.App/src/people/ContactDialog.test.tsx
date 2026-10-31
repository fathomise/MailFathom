// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ContactDialog, type ContactHeld, type ContactSave } from './ContactDialog';

// What opens the dialog in the test, named here rather than written into the markup: the localization rule
// holds over every `.tsx` under the packages, and a label a test invented is not a catalogue entry.
const asks = 'Ask';

// The eight octets a PNG opens with, which is all a chosen file needs to carry here: the form judges the kind the
// browser reported and the size, and the deployment is what reads the signature.
const pngOpening = new Uint8Array([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);

function Asking({
    amending,
    onSave,
}: {
    readonly amending: ContactHeld | null;
    readonly onSave: (save: ContactSave) => void;
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

function drawForm(amending: ContactHeld | null = null): { readonly saved: ReturnType<typeof vi.fn> } {
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

const anna: ContactHeld = {
    displayName: 'Anna Kowalska',
    rows: { addresses: ['anna@contoso.example', 'a.kowalska@fabrikam.example'], preferred: 0 },
    photo: null,
    phoneNumbers: [{ label: 'Work', number: '+1 202 555 0178' }],
};

describe('ContactDialog', () => {
    // A name, addresses, and phone numbers are what a contact record holds on this deployment; a company and a role
    // are not fields of it, so a form offering them would be asking for values no request could carry.
    it('asks for the things a record this client writes actually holds', () => {
        drawForm();

        expect(screen.getByLabelText('Name')).toBeDefined();
        expect(screen.getByRole('group', { name: 'Email addresses' })).toBeDefined();
        expect(addressRows()).toHaveLength(1);
        expect(screen.getByRole('group', { name: 'Phone numbers' }).textContent).toContain('No phone numbers yet');
        expect(screen.queryByLabelText('Company')).toBeNull();
    });

    it('records what was typed once the question has been answered', () => {
        const { saved } = drawForm();

        typeInto(screen.getByLabelText('Name'), '  Anna Kowalska  ');
        typeInto(nth(addressRows(), 0), ' anna@contoso.example ');
        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        expect(saved).toHaveBeenCalledWith({
            statement: {
                displayName: 'Anna Kowalska',
                addresses: ['anna@contoso.example'],
                preferredAddress: 'anna@contoso.example',
                phoneNumbers: [],
            },
            restated: true,
            photo: { kind: 'kept' },
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
            statement: {
                displayName: 'Anna Kowalska',
                addresses: ['anna@home.example', 'anna@contoso.example'],
                preferredAddress: 'anna@home.example',
                phoneNumbers: [],
            },
            restated: true,
            photo: { kind: 'kept' },
        });
    });

    it('adds a number as Mobile first and Work after, and drops a row left empty', () => {
        const { saved } = drawForm();

        typeInto(screen.getByLabelText('Name'), 'Anna Kowalska');
        typeInto(nth(addressRows(), 0), 'anna@contoso.example');
        fireEvent.click(screen.getByRole('button', { name: 'Add a phone number' }));
        typeInto(screen.getByLabelText('Phone number'), ' +1 202 555 0143 ');
        fireEvent.click(screen.getByRole('button', { name: 'Add a phone number' }));

        expect(screen.getAllByLabelText<HTMLSelectElement>('Label').map((label) => label.value)).toEqual([
            'Mobile',
            'Work',
        ]);

        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));

        expect(saved).toHaveBeenCalledWith({
            statement: {
                displayName: 'Anna Kowalska',
                addresses: ['anna@contoso.example'],
                preferredAddress: 'anna@contoso.example',
                phoneNumbers: [{ label: 'Mobile', number: '+1 202 555 0143' }],
            },
            restated: true,
            photo: { kind: 'kept' },
        });
    });

    it('takes a number away with its own control', () => {
        drawForm();

        fireEvent.click(screen.getByRole('button', { name: 'Add a phone number' }));
        fireEvent.click(screen.getByRole('button', { name: 'Remove number' }));

        expect(screen.queryByLabelText('Phone number')).toBeNull();
        expect(screen.getByText('No phone numbers yet')).toBeDefined();
    });

    it('puts focus in a number just added, on the row standing where one was removed, or on adding one', async () => {
        drawForm(anna);

        fireEvent.click(screen.getByRole('button', { name: 'Add a phone number' }));

        await waitFor(() => {
            expect(document.activeElement).toBe(nth(screen.getAllByLabelText('Phone number'), 1));
        });

        fireEvent.click(nth(screen.getAllByRole('button', { name: 'Remove number' }), 0));

        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByLabelText('Phone number'));
        });

        fireEvent.click(screen.getByRole('button', { name: 'Remove number' }));

        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Add a phone number' }));
        });
    });

    it('refuses to save a number that is not shaped like one', () => {
        const { saved } = drawForm();

        typeInto(screen.getByLabelText('Name'), 'Anna Kowalska');
        typeInto(nth(addressRows(), 0), 'anna@contoso.example');
        fireEvent.click(screen.getByRole('button', { name: 'Add a phone number' }));
        typeInto(screen.getByLabelText('Phone number'), 'call me');

        expect(screen.getByRole('alert').textContent).toContain('A phone number is digits');

        fireEvent.click(screen.getByRole('button', { name: 'Save contact' }));
        expect(saved).not.toHaveBeenCalled();
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

    it('puts focus on the row standing where a removed one was, or on the one above where it was the last', async () => {
        drawForm({
            displayName: 'Anna Kowalska',
            rows: {
                addresses: ['anna@contoso.example', 'a.kowalska@fabrikam.example', 'anna@home.example'],
                preferred: 0,
            },
            photo: null,
            phoneNumbers: [],
        });

        fireEvent.click(nth(screen.getAllByRole('button', { name: 'Remove address' }), 2));

        await waitFor(() => {
            expect(document.activeElement).toBe(nth(addressRows(), 1));
        });

        fireEvent.click(nth(screen.getAllByRole('button', { name: 'Remove address' }), 0));

        await waitFor(() => {
            expect(document.activeElement).toBe(nth(addressRows(), 0));
        });
        expect(nth(addressRows(), 0).value).toBe('a.kowalska@fabrikam.example');
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
            statement: {
                displayName: 'Anna Kowalska',
                addresses: ['a.kowalska@fabrikam.example', 'anna@contoso.example'],
                preferredAddress: 'a.kowalska@fabrikam.example',
                phoneNumbers: anna.phoneNumbers,
            },
            restated: true,
            photo: { kind: 'kept' },
        });
    });

    it('saves a correction that changes nothing but a number', () => {
        const { saved } = drawForm(anna);

        expect(screen.getByLabelText<HTMLInputElement>('Phone number').value).toBe('+1 202 555 0178');

        fireEvent.change(screen.getByLabelText('Label'), { target: { value: 'Home' } });
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        expect(saved).toHaveBeenCalledWith({
            statement: {
                displayName: 'Anna Kowalska',
                addresses: anna.rows.addresses,
                preferredAddress: 'anna@contoso.example',
                phoneNumbers: [{ label: 'Home', number: '+1 202 555 0178' }],
            },
            restated: true,
            photo: { kind: 'kept' },
        });
    });

    it('takes a photo chosen for somebody as a change to save on its own, under the kind it was found to be', async () => {
        const { saved } = drawForm(anna);
        const photo = new File([pngOpening], 'anna.png', { type: 'image/png' });

        fireEvent.change(screen.getByLabelText('Choose photo'), { target: { files: [photo] } });

        expect(await screen.findByRole('img', { name: 'Contact photo' })).toBeDefined();
        expect(screen.getByLabelText('Change photo')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        const [save] = saved.mock.lastCall as [ContactSave];

        expect(save.restated).toBe(false);
        expect(save.photo).toMatchObject({ kind: 'chosen', picture: photo, type: 'image/png' });
    });

    it('refuses a photo over the bound under the control it was chosen with, naming its size', () => {
        const { saved } = drawForm(anna);
        const photo = new File([new Uint8Array(1_572_864)], 'anna.png', { type: 'image/png' });

        fireEvent.change(screen.getByLabelText('Choose photo'), { target: { files: [photo] } });

        expect(screen.getByRole('alert').textContent).toBe('The file is 1.5 MB - the limit is 1 MB.');
        expect(screen.getByRole('button', { name: 'Save changes' }).hasAttribute('disabled')).toBe(true);
        expect(saved).not.toHaveBeenCalled();
    });

    it('refuses a file that is neither a JPEG nor a PNG, and keeps the circle it had', () => {
        const { saved } = drawForm(anna);
        const photo = new File([pngOpening], 'anna.gif', { type: 'image/gif' });

        fireEvent.change(screen.getByLabelText('Choose photo'), { target: { files: [photo] } });

        expect(screen.getByRole('alert').textContent).toBe('That file is neither a JPEG nor a PNG.');
        expect(screen.queryByRole('img', { name: 'Contact photo' })).toBeNull();
        expect(screen.getByLabelText('Choose photo')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Save changes' }).hasAttribute('disabled')).toBe(true);
        expect(saved).not.toHaveBeenCalled();
    });

    it('removes the photo somebody has as a change to save, and draws the empty circle meanwhile', async () => {
        const { saved } = drawForm({ ...anna, photo: 'data:image/png;base64,iVBORw0KGgo=' });

        fireEvent.click(screen.getByRole('button', { name: 'Remove photo' }));

        await waitFor(() => {
            expect(screen.queryByRole('img', { name: 'Contact photo' })).toBeNull();
        });
        fireEvent.click(screen.getByRole('button', { name: 'Save changes' }));

        expect(saved).toHaveBeenCalledWith(expect.objectContaining({ restated: false, photo: { kind: 'removed' } }));
    });
});
