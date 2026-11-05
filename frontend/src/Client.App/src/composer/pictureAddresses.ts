// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { HeldPicture } from './pictures';
import type { PictureAt } from './writtenText';

// The object URLs one composer draws its pictures from, and the only addresses its editor reads back as a picture.
//
// **One per composer, and released when the composition ends**, not when a picture leaves the tree. ADR 0047 Question
// 4 keeps a picture that was cut, deleted, or replaced resolvable until the composition is sent, saved, given up, or
// closed, so that pasting it back elsewhere or one Ctrl+Z brings it back. An object URL is the one thing here whose
// lifetime is outside React — it holds its octets until somebody revokes it — so it is issued the first time a picture
// is drawn and revoked, with every other, in one act when the composer goes.
//
// A blob URL rather than a data URL, unlike `deployment/dataUrl.ts`, because here the address *is* the identity: the
// editor reads a picture back by the address it was drawn from, and only an address this composer issued can be one. A
// data URL is something any page can write.

/** The addresses one composer has issued for the pictures it holds. */
export interface PictureAddresses {
    /** The address a picture is drawn from, issued the first time it is asked for and the same one every time after. */
    readonly issue: (name: string, picture: HeldPicture) => string;

    /** The address already issued for a picture, or `null` for one never drawn. */
    readonly addressOf: (name: string) => string | null;

    /** The picture an address names, which only an address issued here does. */
    readonly pictureAt: PictureAt;

    /** Revokes every address issued, which the composer does once the composition has ended. */
    readonly release: () => void;
}

/** A fresh set of addresses, for one composer. */
export function pictureAddresses(): PictureAddresses {
    const byName = new Map<string, string>();
    const byAddress = new Map<string, { readonly picture: string; readonly width: number; readonly height: number }>();

    return {
        issue: (name, picture) => {
            const issued = byName.get(name);

            if (issued !== undefined) {
                return issued;
            }

            // A copy, because a `Blob` part is a view over a buffer of its own and the held octets may be a view into one.
            const address = URL.createObjectURL(new Blob([picture.octets.slice()], { type: picture.type }));

            byName.set(name, address);
            byAddress.set(address, { picture: name, width: picture.width, height: picture.height });

            return address;
        },
        addressOf: (name) => byName.get(name) ?? null,
        pictureAt: (source) => byAddress.get(source) ?? null,
        release: () => {
            for (const address of byName.values()) {
                URL.revokeObjectURL(address);
            }

            byName.clear();
            byAddress.clear();
        },
    };
}
