// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailSpamSubcategory, MailSpamVerdict } from '@mailfathom/client-backend';
import { catalogues } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { isDangerous, spamMenuDetail, spamRowName, spamWarning } from './spamWords';

/** Reads the English catalogue the way the provider does, which is a lookup and a hole filled. */
const translate: Translate = (key, values) =>
    catalogues.en[key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);

const readOnly =
    'While it is marked spam it is read-only: its images are not loaded, its links and attachments do not open, and it cannot be answered or forwarded.';

function verdict(overrides: Partial<MailSpamVerdict> = {}): MailSpamVerdict {
    return {
        verdict: 'Spam',
        decidedBy: 'Deterministic',
        spamSubcategory: null,
        spamSubcategoryNamedBy: null,
        phishingRequest: null,
        impersonatedParty: null,
        ...overrides,
    };
}

describe('isDangerous', () => {
    it.each<MailSpamSubcategory>(['Phishing', 'BusinessEmailCompromise', 'MalwareLure'])(
        'warns about %s',
        (subcategory) => {
            expect(isDangerous(subcategory)).toBe(true);
        },
    );

    it.each<MailSpamSubcategory>(['Scam', 'Extortion', 'Marketing'])('only informs about %s', (subcategory) => {
        expect(isDangerous(subcategory)).toBe(false);
    });

    it('does not warn about spam that names no kind', () => {
        expect(isDangerous(null)).toBe(false);
    });
});

describe('spamRowName', () => {
    it('names the kind and who named it where the verdict carries one', () => {
        expect(
            spamRowName(
                verdict({ decidedBy: 'Deterministic', spamSubcategory: 'Phishing', spamSubcategoryNamedBy: 'Model' }),
                translate,
            ),
        ).toBe('Spam: Phishing, judged by a model.');
    });

    it.each([
        ['Deterministic', 'Spam, judged by your mail server.'],
        ['Model', 'Spam, judged by a model.'],
        ['Person', 'Spam, marked by you.'],
    ] as const)('says spam with no kind and who judged it where %s judged it', (decidedBy, said) => {
        expect(spamRowName(verdict({ decidedBy }), translate)).toBe(said);
    });
});

describe('spamMenuDetail', () => {
    it('names the correspondent and the kind in a sentence', () => {
        expect(
            spamMenuDetail(
                'Parcel Desk',
                verdict({ spamSubcategory: 'BusinessEmailCompromise', spamSubcategoryNamedBy: 'Model' }),
                translate,
            ),
        ).toBe('Parcel Desk - business email compromise spam');
    });

    it('names the correspondent and spam where the verdict names no kind', () => {
        expect(spamMenuDetail('Parcel Desk', verdict(), translate)).toBe('Parcel Desk - Spam');
    });

    it('falls back to the row name where nobody is named', () => {
        expect(spamMenuDetail(undefined, verdict({ decidedBy: 'Person' }), translate)).toBe('Spam, marked by you.');
    });
});

describe('spamWarning', () => {
    it('draws the strong form for a dangerous kind, saying what it asks for and whom it poses as', () => {
        expect(
            spamWarning(
                verdict({
                    decidedBy: 'Model',
                    spamSubcategory: 'Phishing',
                    spamSubcategoryNamedBy: 'Model',
                    phishingRequest: 'Credentials',
                    impersonatedParty: 'Bank',
                }),
                translate,
            ),
        ).toEqual({
            strong: true,
            title: 'Phishing. This message is spam and a security risk.',
            lines: [
                'It asks for a password, and it is written to look as if it came from a bank.',
                "A model judged this message spam and named it phishing. The kind is the model's judgement and can be wrong.",
                'Do not give it anything it asks for.',
                readOnly,
            ],
        });
    });

    it('says only what it asks for where the verdict names no party', () => {
        const warning = spamWarning(
            verdict({
                spamSubcategory: 'Phishing',
                spamSubcategoryNamedBy: 'Model',
                phishingRequest: 'OneTimeCode',
                impersonatedParty: 'NobodyEvident',
            }),
            translate,
        );

        expect(warning.lines).toContain('It asks for a one-time code.');
        expect(warning.lines.some((line) => line.includes('look as if'))).toBe(false);
    });

    it('says only whom it poses as where the verdict names no request', () => {
        const warning = spamWarning(
            verdict({
                spamSubcategory: 'BusinessEmailCompromise',
                spamSubcategoryNamedBy: 'Model',
                phishingRequest: 'NothingStated',
                impersonatedParty: 'Executive',
            }),
            translate,
        );

        expect(warning.lines).toContain('It is written to look as if it came from a manager.');
        expect(warning.lines.some((line) => line.startsWith('It asks for'))).toBe(false);
    });

    it('says nothing about a request or a party the verdict leaves as other', () => {
        const warning = spamWarning(
            verdict({
                spamSubcategory: 'MalwareLure',
                spamSubcategoryNamedBy: 'Model',
                phishingRequest: 'Other',
                impersonatedParty: 'Other',
            }),
            translate,
        );

        expect(warning.lines).toEqual([
            'Your mail server judged this message spam.',
            "A model named it malware lure. The kind is the model's judgement and can be wrong.",
            'Do not give it anything it asks for.',
            readOnly,
        ]);
    });

    it('draws the quiet form for a harmless kind, with neither a request nor a caution', () => {
        expect(
            spamWarning(
                verdict({
                    decidedBy: 'Person',
                    spamSubcategory: 'Marketing',
                    spamSubcategoryNamedBy: 'Person',
                    phishingRequest: 'Credentials',
                    impersonatedParty: 'Bank',
                }),
                translate,
            ),
        ).toEqual({
            strong: false,
            title: 'Marketing. This message is spam and may be a security risk.',
            lines: ['You marked this message spam and named it marketing.', readOnly],
        });
    });

    it('draws the quiet form with no kind where the verdict names none', () => {
        expect(spamWarning(verdict(), translate)).toEqual({
            strong: false,
            title: 'This message is spam and may be a security risk.',
            lines: ['Your mail server judged this message spam.', readOnly],
        });
    });
});
