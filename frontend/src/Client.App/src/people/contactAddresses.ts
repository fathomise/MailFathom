// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { looksLikeAnAddress } from '../composer/composition';

// The addresses a contact form holds, and the one of them a new message goes to by default. The form is rows rather
// than a record, because a row somebody has added and not yet typed into is part of what they are looking at, and a
// record holds no empty address.
//
// **Exactly one row is the default, always.** A new contact's is its first row, and removing the default hands it to
// the first row left — so there is no state in which the form shows several, or none, and the record it is saved as
// never has to choose.

/** The address rows a contact form holds, and which of them is the default. */
export interface AddressRows {
    readonly addresses: readonly string[];

    /** The position of the default row in `addresses`. */
    readonly preferred: number;
}

/** What a new contact's form opens on: one empty row, which is the default. */
export const noAddressWritten: AddressRows = { addresses: [''], preferred: 0 };

/** The rows a recorded contact's form opens on, its preferred address chosen. */
export function rowsOf(addresses: readonly string[], preferredAddress: string): AddressRows {
    return addresses.length === 0
        ? noAddressWritten
        : { addresses, preferred: Math.max(addresses.indexOf(preferredAddress), 0) };
}

export function withAddressAdded(rows: AddressRows): AddressRows {
    return { ...rows, addresses: [...rows.addresses, ''] };
}

export function withAddressTyped(rows: AddressRows, at: number, typed: string): AddressRows {
    return { ...rows, addresses: rows.addresses.map((address, row) => (row === at ? typed : address)) };
}

export function withDefault(rows: AddressRows, at: number): AddressRows {
    return { ...rows, preferred: at };
}

/** The rows without one of them, which never empties the form: the last row is not removable. */
export function withAddressRemoved(rows: AddressRows, at: number): AddressRows {
    if (rows.addresses.length <= 1) {
        return rows;
    }

    return {
        addresses: rows.addresses.filter((_, row) => row !== at),
        preferred: at === rows.preferred ? 0 : at < rows.preferred ? rows.preferred - 1 : rows.preferred,
    };
}

/** Whether some row holds text that is not shaped like an address, which is worth saying under the rows. */
export function holdsUnusableAddress(rows: AddressRows): boolean {
    return rows.addresses.some((address) => address.trim() !== '' && !looksLikeAnAddress(address.trim()));
}

/**
 * The addresses the rows are saved as, with the one to use by default — or `null` where they cannot be saved: no
 * address at all, or one not shaped like an address.
 *
 * A row left empty is dropped. Where the default row is the one left empty, the first address kept takes its place,
 * which is the same rule removing the default follows.
 */
export function savedAddresses(
    rows: AddressRows,
): { readonly addresses: readonly string[]; readonly preferredAddress: string } | null {
    const typed = rows.addresses.map((address) => address.trim());
    const kept = typed.filter((address) => address !== '');
    const chosen = typed[rows.preferred];
    const preferredAddress = chosen !== undefined && chosen !== '' ? chosen : kept[0];

    if (preferredAddress === undefined || holdsUnusableAddress(rows)) {
        return null;
    }

    // An address written twice, in any case, is one address, and the record keeps the default's spelling of it.
    const seen = new Set<string>();
    const addresses = [preferredAddress, ...kept].filter((address) => {
        const folded = address.toLowerCase();

        return !seen.has(folded) && seen.add(folded);
    });

    return { addresses, preferredAddress };
}
