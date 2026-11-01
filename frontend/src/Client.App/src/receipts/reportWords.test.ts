// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailReport, MailReportRecipient } from '@mailfathom/client-backend';
import { catalogues, type Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { reportHeadlineOf, reportWordsOf } from './reportWords';

/** Reads the catalogue the way the provider does, which is a lookup and a hole filled. */
function translating(locale: Locale): Translate {
    return (key, values) =>
        catalogues[locale][key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);
}

const english = translating('en');

function report(
    kind: MailReport['kind'],
    recipients: readonly MailReportRecipient[],
    ownMessageSubject: string | null,
): MailReport {
    return {
        kind,
        recipients,
        reportedAt: '2026-08-25T17:40:00+00:00',
        namesOwnMessage: ownMessageSubject !== null,
        ownMessageSubject,
    };
}

function words(said: MailReport, when: string | null = null) {
    return reportWordsOf(said, 'Marta Nowak', when, english, 'en');
}

describe('reportWordsOf', () => {
    it('heads a read receipt with who read which of the account’s messages', () => {
        expect(
            words(report('Read', [{ address: 'marta@example.invalid', outcome: 'Displayed' }], 'CPI calculation 2027'))
                ?.headline,
        ).toBe('Read receipt: Marta Nowak read “CPI calculation 2027”');
    });

    it('heads a read receipt naming no message in this mailbox by what it is and who sent it', () => {
        expect(
            words(report('Read', [{ address: 'marta@example.invalid', outcome: 'Displayed' }], null))?.headline,
        ).toBe('Read receipt from Marta Nowak · for a message not in this mailbox');
    });

    it('folds a read receipt to who read the message, and when', () => {
        expect(
            words(report('Read', [{ address: 'marta@example.invalid', outcome: 'Displayed' }], 'x'), '25.08 17:40')
                ?.line,
        ).toBe('Marta Nowak read your message · 25.08 17:40');
    });

    it.each([
        ['Failed', 'Not delivered to r.lis@example.invalid: “Quote”'],
        ['Delayed', 'Delayed for r.lis@example.invalid: “Quote”'],
        ['Delivered', 'Delivered to r.lis@example.invalid: “Quote”'],
    ] as const)('heads a delivery report saying %s with the recipient and the message', (outcome, headline) => {
        expect(words(report('Delivery', [{ address: 'r.lis@example.invalid', outcome }], 'Quote'))?.headline).toBe(
            headline,
        );
    });

    it('counts several recipients by the outcome that most needs the sender', () => {
        const several = report(
            'Delivery',
            [
                { address: 'a@example.invalid', outcome: 'Failed' },
                { address: 'b@example.invalid', outcome: 'Delivered' },
            ],
            'Request for the contract register',
        );

        expect(words(several, '24.08 09:13')).toMatchObject({
            headline: 'Not delivered to 1 of 2: “Request for the contract register”',
            line: 'Not delivered to 1 of 2 · 24.08 09:13',
        });
    });

    it('heads a delivery report naming no message in this mailbox by what it says', () => {
        expect(
            words(report('Delivery', [{ address: 'orders@example.invalid', outcome: 'Failed' }], null))?.headline,
        ).toBe('Delivery report: not delivered to orders@example.invalid · for a message not in this mailbox');
    });

    it('leaves a read receipt saying the message was deleted unread to its own subject', () => {
        expect(words(report('Read', [{ address: 'marta@example.invalid', outcome: 'Deleted' }], 'x'))).toBeNull();
    });
});

describe('reportHeadlineOf', () => {
    it('leaves a message that is no report to its own subject', () => {
        expect(
            reportHeadlineOf({ report: null, senderDisplayName: 'Billing', senderAddress: null }, english, 'en'),
        ).toBeNull();
    });

    it('names the reader of a read receipt by the address it came from where it gives no name', () => {
        expect(
            reportHeadlineOf(
                {
                    report: report('Read', [{ address: 'karol@example.invalid', outcome: 'Displayed' }], null),
                    senderDisplayName: null,
                    senderAddress: 'karol@example.invalid',
                },
                english,
                'en',
            ),
        ).toBe('Read receipt from karol@example.invalid · for a message not in this mailbox');
    });
});
