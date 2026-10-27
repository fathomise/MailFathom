// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { FollowedLink } from '../shellOperations/linkOpener';
import { askingFor } from './linkTrust';

const trusted = ['nordwind.example'];

function pressed(target: string, said: Partial<FollowedLink> = {}): FollowedLink {
    return { target, picture: false, words: null, warnings: [], ...said };
}

describe('askingFor', () => {
    it('opens a link to a trusted domain without asking', () => {
        expect(askingFor(pressed('https://nordwind.example/price-list'), trusted)).toBeNull();
    });

    it('opens a link to a subdomain of a trusted domain without asking', () => {
        expect(askingFor(pressed('https://files.nordwind.example/materials.zip'), trusted)).toBeNull();
    });

    it.each([
        ['a name that borrows the trusted one', 'https://nordwind-billing.example/invoice'],
        ['a name that ends in the trusted one without being under it', 'https://evilnordwind.example/'],
        ['a host nobody listed', 'https://click.travelmail.example/check-in'],
    ])('asks before %s', (_, target) => {
        expect(askingFor(pressed(target), trusted)).toMatchObject({ target, host: new URL(target).host });
    });

    // A browser parses the address before it goes anywhere, and the parser drops what surrounds or splits a scheme, so
    // these leave for the web however their text begins.
    it.each([
        ['a space in front of it', ' https://evil.example/'],
        ['a control character in front of it', '\u0001https://evil.example/'],
        ['a tab inside its scheme', 'ht\ttps://evil.example/'],
    ])('asks about an address with %s, which still leaves for the web', (_, target) => {
        expect(askingFor(pressed(target), [])).toMatchObject({ target, host: 'evil.example' });
    });

    it('opens a link that does not leave for the web without asking', () => {
        expect(askingFor(pressed('mailto:someone@example.invalid'), [])).toBeNull();
    });

    it('says a picture was pressed, and reads no words off one', () => {
        expect(
            askingFor(pressed('https://hotel.example/offer', { picture: true, words: 'travel.example' }), trusted),
        ).toEqual({
            target: 'https://hotel.example/offer',
            host: 'hotel.example',
            picture: true,
            warnings: [],
            wordsName: null,
        });
    });

    it('carries the warnings the link already drew', () => {
        const warnings = ['This address is in none of the messages this answer cites.'];

        expect(askingFor(pressed('https://example.invalid/', { warnings }), trusted)).toMatchObject({ warnings });
    });

    it('says the words name another host where nothing else warned', () => {
        expect(
            askingFor(pressed('https://click.travelmail.example/c/42', { words: 'travel.example' }), trusted),
        ).toMatchObject({ wordsName: 'travel.example', host: 'click.travelmail.example' });
    });

    it('leaves the words to a warning the link already carries', () => {
        expect(
            askingFor(
                pressed('https://click.travelmail.example/c/42', { words: 'travel.example', warnings: ['Judged.'] }),
                trusted,
            ),
        ).toMatchObject({ wordsName: null, warnings: ['Judged.'] });
    });

    it.each([
        ['words naming the host it goes to', 'https://www.travel.example/check-in', 'travel.example/check-in'],
        ['words that are a sentence rather than an address', 'https://travel.example/', 'Check in now.'],
    ])('says nothing about %s', (_, target, words) => {
        expect(askingFor(pressed(target, { words }), trusted)).toMatchObject({ wordsName: null });
    });
});
