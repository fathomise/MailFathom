// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { afterEach, describe, expect, it, vi } from 'vitest';
import { nothingWrittenYet, type Composition } from './composition';
import {
    forgetComposition,
    picturesKeptHereOnly,
    rememberComposition,
    rememberedComposition,
    rememberPictures,
} from './keptComposition';

const storageKey = 'mailfathom.composition';
const picturesKey = 'mailfathom.composition.pictures';

const written: Composition = {
    ...nothingWrittenYet('work'),
    subject: 'Invoice',
    to: ['ada@example.invalid'],
    cc: ['bo@example.invalid'],
    words: [{ text: 'Here it is.' }],
    pictures: new Map(),
};

afterEach(() => {
    window.sessionStorage.clear();
});

describe('rememberedComposition', () => {
    it('reads back what was being written, so a reload returns to it', () => {
        rememberComposition(written);

        expect(rememberedComposition()).toEqual(written);
    });

    it('reads back an answer with the message it answers', () => {
        const answering: Composition = { ...written, answering: { storedEmailId: 'e1', answers: 'everyone' } };

        rememberComposition(answering);

        expect(rememberedComposition()).toEqual(answering);
    });

    it('reads back a draft with the stored message it was carried on from', () => {
        const carriedOn: Composition = { ...written, continuing: 'e1' };

        rememberComposition(carriedOn);

        expect(rememberedComposition()).toEqual(carriedOn);
    });

    it('answers nothing where nothing was kept', () => {
        expect(rememberedComposition()).toBeNull();
    });

    it('reads back the requests the message makes', () => {
        const asking = { ...written, requestsDeliveryNotification: true, requestsReadReceipt: true };

        rememberComposition(asking);

        expect(rememberedComposition()).toEqual(asking);
    });

    it('reads a kept message that states no request as asking for neither', () => {
        window.sessionStorage.setItem(
            storageKey,
            JSON.stringify({ ...written, requestsDeliveryNotification: undefined, requestsReadReceipt: undefined }),
        );

        expect(rememberedComposition()).toEqual(written);
    });

    it('drops what was being written, which is what signing out does', () => {
        rememberComposition(written);
        forgetComposition();

        expect(rememberedComposition()).toBeNull();
    });

    it.each([
        ['text that is not a composition at all', 'not json'],
        ['a list rather than a message', '[]'],
        ['a message with no words', JSON.stringify({ ...written, words: undefined })],
        ['a subject that is not text', JSON.stringify({ ...written, subject: 7 })],
        ['an address that is not text', JSON.stringify({ ...written, to: [7] })],
        ['an empty address', JSON.stringify({ ...written, to: [''] })],
        ['a header that is not a list', JSON.stringify({ ...written, cc: 'bo@example.invalid' })],
        [
            'more addresses than one header takes',
            JSON.stringify({ ...written, to: Array.from({ length: 257 }, () => 'a@b') }),
        ],
        ['a subject past a header line', JSON.stringify({ ...written, subject: 'x'.repeat(999) })],
        ['an address past the longest one', JSON.stringify({ ...written, to: ['x'.repeat(321)] })],
        ['an account past an identifier', JSON.stringify({ ...written, account: 'x'.repeat(257) })],
        ['an answer to nothing named', JSON.stringify({ ...written, answering: { answers: 'everyone' } })],
        [
            'an answer of a kind there is none of',
            JSON.stringify({ ...written, answering: { storedEmailId: 'e1', answers: 'shout' } }),
        ],
        ['an answer that is a list', JSON.stringify({ ...written, answering: [] })],
        ['a request that is neither on nor off', JSON.stringify({ ...written, requestsReadReceipt: 'yes' })],
        ['a request kept as null', JSON.stringify({ ...written, requestsDeliveryNotification: null })],
        ['a draft carried on from something that is not text', JSON.stringify({ ...written, continuing: 7 })],
        [
            'a draft carried on from an identifier longer than one',
            JSON.stringify({ ...written, continuing: 'x'.repeat(257) }),
        ],
        [
            'a message that is both an answer and a draft carried on, which nothing here writes',
            JSON.stringify({ ...written, answering: { storedEmailId: 'e1', answers: 'everyone' }, continuing: 'e2' }),
        ],
    ])('answers nothing for %s, rather than a message with a hole in it', (_, stored) => {
        window.sessionStorage.setItem(storageKey, stored);

        expect(rememberedComposition()).toBeNull();
    });
});

describe('rememberPictures', () => {
    const pictureName = '01890a5d-ac96-774b-bcce-b302099a8057';
    const otherPictureName = '01890a5d-ac96-774b-bcce-b302099a8058';

    // The smallest PNG header that states a size: the signature, then the header chunk naming itself and 3 by 2 pixels.
    const octets = new Uint8Array([
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 3, 0, 0, 0, 2,
    ]);
    const content = btoa(String.fromCharCode(...octets));

    const withPicture: Composition = {
        ...written,
        words: [{ text: 'Here it is.' }, { element: 'img', picture: pictureName, width: 300, height: 200 }],
        pictures: new Map([
            [pictureName, { octets, type: 'image/png', width: 300, height: 200, label: 'chart.png' }],
            [otherPictureName, { octets, type: 'image/png', width: 3, height: 2, label: null }],
        ]),
    };

    it('keeps the pictures the words name beside them, so a reload returns to both', () => {
        rememberComposition(withPicture);

        expect(rememberPictures(withPicture)).toBe(true);
        expect(rememberedComposition()).toEqual({
            ...withPicture,
            pictures: new Map([[pictureName, withPicture.pictures.get(pictureName)]]),
        });
    });

    it('reads back the words without a picture the store did not keep, rather than refusing them', () => {
        rememberComposition(withPicture);

        expect(rememberedComposition()).toEqual({
            ...withPicture,
            words: [{ text: 'Here it is.' }],
            pictures: new Map(),
        });
    });

    it('removes the pictures it kept earlier where the store refuses the new ones, and says it did not keep them', () => {
        rememberPictures(withPicture);
        const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
            throw new DOMException('full', 'QuotaExceededError');
        });

        try {
            expect(rememberPictures(withPicture)).toBe(false);
        } finally {
            setItem.mockRestore();
        }

        expect(window.sessionStorage.getItem(picturesKey)).toBeNull();
        expect(picturesKeptHereOnly()).toBe(true);

        rememberPictures(withPicture);

        expect(picturesKeptHereOnly()).toBe(false);
    });

    it('stops saying the pictures were not kept once the composition is forgotten', () => {
        const setItem = vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
            throw new DOMException('full', 'QuotaExceededError');
        });

        try {
            rememberPictures(withPicture);
        } finally {
            setItem.mockRestore();
        }

        forgetComposition();

        expect(picturesKeptHereOnly()).toBe(false);
    });

    it('keeps a picture whose name is longer than a kept label may be, under the name cut to that length', () => {
        const named = 'a'.repeat(1024);
        const longNamed: Composition = {
            ...withPicture,
            pictures: new Map([[pictureName, { octets, type: 'image/png', width: 300, height: 200, label: named }]]),
        };

        rememberComposition(longNamed);
        rememberPictures(longNamed);

        expect(rememberedComposition()?.pictures.get(pictureName)?.label).toBe(named.slice(0, 256));
    });

    it('keeps nothing once the words name no picture', () => {
        rememberPictures(withPicture);
        rememberPictures(written);

        expect(window.sessionStorage.getItem(picturesKey)).toBeNull();
    });

    it.each([
        ['a name the composer never mints', [{ name: 'chart', content, width: 3, height: 2, label: null }]],
        [
            'content that is not a picture',
            [{ name: pictureName, content: btoa('text'), width: 3, height: 2, label: null }],
        ],
        ['a side of nothing', [{ name: pictureName, content, width: 0, height: 2, label: null }]],
        ['a label that is not text', [{ name: pictureName, content, width: 3, height: 2, label: 7 }]],
        ['more pictures than a composition holds', Array.from({ length: 17 }, () => ({}))],
    ])('keeps no picture where the store holds %s', (_, kept) => {
        rememberComposition(withPicture);
        window.sessionStorage.setItem(picturesKey, JSON.stringify(kept));

        expect(rememberedComposition()?.pictures).toEqual(new Map());
    });
});
