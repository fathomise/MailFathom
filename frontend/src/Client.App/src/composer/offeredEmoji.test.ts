// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { committedPictures, offeredEmoji, pictureFileOf, pictureOf } from './offeredEmoji';

describe('offeredEmoji', () => {
    it('draws every emoji the menu offers from a picture committed in the tree', () => {
        expect(offeredEmoji.filter((emoji) => pictureOf(emoji.text) === undefined).map((emoji) => emoji.text)).toEqual(
            [],
        );
    });

    it('carries no picture the menu does not offer, so nothing sits in the bundle unnoticed', () => {
        expect([...committedPictures].sort()).toEqual(offeredEmoji.map((emoji) => pictureFileOf(emoji.text)).sort());
    });

    it('names a picture by its code points, leaving the emoji presentation selector out', () => {
        expect(pictureFileOf('❤️')).toBe('emoji_u2764.svg');
        expect(pictureFileOf('👍')).toBe('emoji_u1f44d.svg');
    });
});
