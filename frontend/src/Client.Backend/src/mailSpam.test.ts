// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { isSpam, parseSpamVerdict } from './mailSpam';

const phishing = {
    verdict: 'Spam',
    decidedBy: 'Model',
    spamSubcategory: 'Phishing',
    spamSubcategoryNamedBy: 'Model',
    phishingRequest: 'Credentials',
    impersonatedParty: 'OnlineService',
};

describe('parseSpamVerdict', () => {
    it('reads a message no stage has reached as one with no verdict', () => {
        expect(parseSpamVerdict(null)).toStrictEqual({ spam: null });
        expect(parseSpamVerdict(undefined)).toStrictEqual({ spam: null });
    });

    it('reads a phishing verdict with what it asks for and whom it pretends to be', () => {
        expect(parseSpamVerdict(phishing)).toStrictEqual({ spam: phishing });
    });

    it('reads a verdict that names no kind, which is spam all the same', () => {
        const parsed = parseSpamVerdict({ verdict: 'Spam', decidedBy: 'Deterministic' });

        expect(parsed).toStrictEqual({
            spam: {
                verdict: 'Spam',
                decidedBy: 'Deterministic',
                spamSubcategory: null,
                spamSubcategoryNamedBy: null,
                phishingRequest: null,
                impersonatedParty: null,
            },
        });
        expect(isSpam(parsed?.spam ?? null)).toBe(true);
    });

    it('keeps what a message asks for and whom it pretends to be off a kind that carries neither', () => {
        const parsed = parseSpamVerdict({ ...phishing, spamSubcategory: 'Extortion' });

        expect(parsed?.spam?.phishingRequest).toBeNull();
        expect(parsed?.spam?.impersonatedParty).toBeNull();
    });

    it('keeps whom a business email compromise pretends to be, and nothing it asks for', () => {
        const parsed = parseSpamVerdict({ ...phishing, spamSubcategory: 'BusinessEmailCompromise' });

        expect(parsed?.spam?.phishingRequest).toBeNull();
        expect(parsed?.spam?.impersonatedParty).toBe('OnlineService');
    });

    it.each([
        ['a value that is not an object', 'Spam'],
        ['a verdict outside the closed set', { ...phishing, verdict: 'Junk' }],
        ['a judge outside the closed set', { ...phishing, decidedBy: 'Server' }],
        ['a kind outside the closed set', { ...phishing, spamSubcategory: 'Newsletter' }],
        ['a request outside the closed set', { ...phishing, phishingRequest: 'Password' }],
        ['a party outside the closed set', { ...phishing, impersonatedParty: 'Nordwind' }],
        ['a kind on a message that is not spam', { ...phishing, verdict: 'NotSpam' }],
        ['a kind nobody named', { ...phishing, spamSubcategoryNamedBy: null }],
        ['a namer with no kind', { verdict: 'Spam', decidedBy: 'Model', spamSubcategoryNamedBy: 'Model' }],
    ])('refuses %s', (_, value) => {
        expect(parseSpamVerdict(value)).toBeNull();
    });
});

describe('isSpam', () => {
    it.each([
        ['NotSpam', false],
        ['Undetermined', false],
    ] as const)('reads a %s verdict as not spam', (verdict, expected) => {
        const parsed = parseSpamVerdict({ verdict, decidedBy: 'Person' });

        expect(isSpam(parsed?.spam ?? null)).toBe(expected);
    });

    it('reads a message with no verdict as not spam', () => {
        expect(isSpam(null)).toBe(false);
    });
});
