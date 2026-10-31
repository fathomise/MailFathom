// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    holdsUnusableAddress,
    noAddressWritten,
    rowsOf,
    savedAddresses,
    withAddressAdded,
    withAddressRemoved,
    withAddressTyped,
    withDefault,
} from './contactAddresses';

const three = { addresses: ['anna@contoso.example', 'a.kowalska@fabrikam.example', 'anna@home.example'], preferred: 1 };

describe('contact address rows', () => {
    it('opens a new contact on one empty row, which is the default', () => {
        expect(noAddressWritten).toEqual({ addresses: [''], preferred: 0 });
    });

    it('opens a recorded contact with its preferred address chosen', () => {
        expect(rowsOf(three.addresses, 'anna@home.example')).toEqual({ addresses: three.addresses, preferred: 2 });
    });

    it('adds an empty row without moving the default', () => {
        expect(withAddressAdded(three)).toEqual({ addresses: [...three.addresses, ''], preferred: 1 });
    });

    it('makes the chosen row the default and no other', () => {
        expect(withDefault(three, 2).preferred).toBe(2);
    });

    it('hands the default to the first row left when the default is removed', () => {
        expect(withAddressRemoved(three, 1)).toEqual({
            addresses: ['anna@contoso.example', 'anna@home.example'],
            preferred: 0,
        });
    });

    it('keeps the same address the default when a row before it is removed', () => {
        expect(withAddressRemoved(three, 0)).toEqual({
            addresses: ['a.kowalska@fabrikam.example', 'anna@home.example'],
            preferred: 0,
        });
        expect(withAddressRemoved(three, 2).preferred).toBe(1);
    });

    it('never removes the last row', () => {
        expect(withAddressRemoved(noAddressWritten, 0)).toBe(noAddressWritten);
    });

    it('drops an empty row on save and puts the default first', () => {
        expect(savedAddresses(withAddressTyped(withAddressAdded(three), 3, '   '))).toEqual({
            addresses: ['a.kowalska@fabrikam.example', 'anna@contoso.example', 'anna@home.example'],
            preferredAddress: 'a.kowalska@fabrikam.example',
        });
    });

    it('takes the first address kept where the default row was left empty', () => {
        expect(savedAddresses({ addresses: ['', ' bartek@contoso.example '], preferred: 0 })).toEqual({
            addresses: ['bartek@contoso.example'],
            preferredAddress: 'bartek@contoso.example',
        });
    });

    it('saves an address written on several rows once, in any case, keeping the default spelling', () => {
        expect(
            savedAddresses({
                addresses: [
                    'bartek@contoso.example',
                    'Anna@Contoso.example',
                    'anna@contoso.example',
                    'BARTEK@contoso.example',
                ],
                preferred: 2,
            }),
        ).toEqual({
            addresses: ['anna@contoso.example', 'bartek@contoso.example'],
            preferredAddress: 'anna@contoso.example',
        });
    });

    it('saves nothing where no row holds an address, or one holds something that is not one', () => {
        expect(savedAddresses({ addresses: ['', ' '], preferred: 0 })).toBeNull();
        expect(savedAddresses({ addresses: ['anna@contoso.example', 'anna'], preferred: 0 })).toBeNull();
        expect(holdsUnusableAddress({ addresses: ['anna@contoso.example', 'anna'], preferred: 0 })).toBe(true);
        expect(holdsUnusableAddress({ addresses: ['anna@contoso.example', ''], preferred: 0 })).toBe(false);
    });
});
