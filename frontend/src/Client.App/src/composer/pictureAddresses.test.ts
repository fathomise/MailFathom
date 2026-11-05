// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { pictureAddresses } from './pictureAddresses';
import type { HeldPicture } from './pictures';

const picture: HeldPicture = {
    octets: new Uint8Array([1, 2, 3]),
    type: 'image/png',
    width: 30,
    height: 20,
    label: null,
};

function issuing() {
    let issued = 0;
    const created = vi.spyOn(URL, 'createObjectURL').mockImplementation(() => `blob:composer/${String((issued += 1))}`);
    const revoked = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);

    return { created, revoked };
}

afterEach(() => {
    vi.restoreAllMocks();
});

describe('pictureAddresses', () => {
    it('issues one address per picture, the same one every time it is asked for', () => {
        const { created } = issuing();
        const addresses = pictureAddresses();

        expect(addresses.issue('one', picture)).toBe('blob:composer/1');
        expect(addresses.issue('one', picture)).toBe('blob:composer/1');
        expect(addresses.issue('two', picture)).toBe('blob:composer/2');
        expect(created).toHaveBeenCalledTimes(2);
        expect(addresses.addressOf('one')).toBe('blob:composer/1');
        expect(addresses.addressOf('three')).toBeNull();
    });

    it('reads back only an address it issued, as the picture and the size it was issued for', () => {
        issuing();
        const addresses = pictureAddresses();

        addresses.issue('one', picture);

        expect(addresses.pictureAt('blob:composer/1')).toEqual({ picture: 'one', width: 30, height: 20 });
        expect(addresses.pictureAt('blob:elsewhere/1')).toBeNull();
        expect(addresses.pictureAt('https://example.invalid/one.png')).toBeNull();
    });

    it('revokes every address it issued once the composition ends, and reads none of them back after', () => {
        const { revoked } = issuing();
        const addresses = pictureAddresses();

        addresses.issue('one', picture);
        addresses.issue('two', picture);
        addresses.release();

        expect(revoked.mock.calls).toEqual([['blob:composer/1'], ['blob:composer/2']]);
        expect(addresses.pictureAt('blob:composer/1')).toBeNull();
        expect(addresses.addressOf('one')).toBeNull();
    });
});
