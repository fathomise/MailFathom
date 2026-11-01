// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type {
    MailReceipts,
    MailReceiptSummary,
    MailRecipientReceipts,
    MailRecordedReceipt,
} from '@mailfathom/client-backend';
import { catalogues, type Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { receiptNameOf, recipientOutcomeOf, standingOfReceipts, standingOfSummary, wordOf } from './receiptMarks';

/** Reads the catalogue the way the provider does, which is a lookup and a hole filled. */
function translating(locale: Locale): Translate {
    return (key, values) =>
        catalogues[locale][key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);
}

const english = translating('en');

function summary(recipients: number, delivery: Partial<MailReceiptSummary['delivery']>, read = 0): MailReceiptSummary {
    return {
        recipients,
        delivery: { requested: true, delivered: 0, delayed: 0, failed: 0, ...delivery },
        read: { requested: true, read },
    };
}

function recorded(outcome: MailRecordedReceipt['outcome']): MailRecordedReceipt {
    return { outcome, reportedAt: '2026-08-26T16:21:00+00:00', source: 'Report', status: null, reportingServer: null };
}

function recipient(
    address: string,
    delivery: MailRecordedReceipt['outcome'] | null,
    read: MailRecordedReceipt['outcome'] | null = null,
): MailRecipientReceipts {
    return {
        address,
        delivery: delivery === null ? null : recorded(delivery),
        read: read === null ? null : recorded(read),
    };
}

function receipts(...recipients: MailRecipientReceipts[]): MailReceipts {
    return { deliveryRequested: true, readRequested: true, recipients };
}

describe('standingOfSummary', () => {
    it.each([
        ['not delivered', summary(1, { failed: 1 }), 'failed'],
        ['delayed', summary(1, { delayed: 1 }), 'delayed'],
        ['read', summary(1, { delivered: 1 }, 1), 'read'],
        ['delivered', summary(1, { delivered: 1 }), 'delivered'],
    ] as const)('marks a row whose one recipient’s report says %s', (_said, counted, mark) => {
        expect(standingOfSummary(counted)?.mark).toBe(mark);
    });

    it('marks a failure over a read, the failure being what asks something of the sender', () => {
        expect(standingOfSummary(summary(2, { failed: 1, delivered: 1 }, 1))).toStrictEqual({
            mark: 'failed',
            count: 1,
            of: 2,
        });
    });

    it('draws nothing on a row nothing came back for', () => {
        expect(standingOfSummary(summary(2, {}))).toBeNull();
    });

    it('draws nothing on a row of mail that asked for nothing and got nothing', () => {
        expect(standingOfSummary(null)).toBeNull();
    });
});

describe('standingOfReceipts', () => {
    it('takes each recipient as far as a report took them, a read past a delivery', () => {
        expect(
            standingOfReceipts(
                receipts(
                    recipient('anna@example.invalid', 'Delivered', 'Displayed'),
                    recipient('marek@example.invalid', 'Delivered', 'Displayed'),
                    recipient('jacek@example.invalid', 'Delivered'),
                ),
            ),
        ).toStrictEqual({ mark: 'read', count: 2, of: 3 });
    });

    it('marks a request nothing answered yet, which only an opened message draws', () => {
        expect(standingOfReceipts(receipts(recipient('ewa@example.invalid', null)))).toStrictEqual({
            mark: 'requested',
            count: 0,
            of: 1,
        });
    });

    it('marks nothing for a message that asked for nothing and got nothing', () => {
        expect(standingOfReceipts(null)).toBeNull();
    });
});

describe('recipientOutcomeOf', () => {
    it('reads a receipt saying the message was deleted unread as no read', () => {
        expect(recipientOutcomeOf(recipient('a@example.invalid', 'Relayed', 'Deleted'))).toBe('delivered');
    });
});

describe('wordOf', () => {
    it('counts the recipients the mark names out of all of them', () => {
        expect(wordOf({ mark: 'read', count: 2, of: 3 }, english, 'en')).toBe('Read by 2 of 3');
        expect(wordOf({ mark: 'failed', count: 1, of: 2 }, translating('pl'), 'pl')).toBe('Nie doręczono do 1 z 2');
    });

    it('drops the count where the mark names every recipient', () => {
        expect(wordOf({ mark: 'delivered', count: 2, of: 2 }, english, 'en')).toBe('Delivered');
    });
});

describe('receiptNameOf', () => {
    it('names every recipient’s own outcome after the word, by the name the message gives them', () => {
        const came = receipts(
            recipient('anna@example.invalid', 'Delivered', 'Displayed'),
            recipient('jacek@example.invalid', 'Delivered'),
        );

        expect(
            receiptNameOf(
                { mark: 'read', count: 1, of: 2 },
                came,
                [
                    { role: 'From', address: 'me@example.invalid', displayName: 'Me' },
                    { role: 'To', address: 'anna@example.invalid', displayName: 'Anna Kowalska' },
                    { role: 'Cc', address: 'jacek@example.invalid', displayName: null },
                ],
                english,
                'en',
            ),
        ).toBe(
            `Read by 1 of 2 - ${new Intl.ListFormat('en', { type: 'unit', style: 'short' }).format([
                'Anna Kowalska read',
                'jacek@example.invalid delivered',
            ])}`,
        );
    });

    it('says a request stands with nothing back yet', () => {
        expect(
            receiptNameOf(
                { mark: 'requested', count: 0, of: 1 },
                receipts(recipient('ewa@example.invalid', null)),
                [{ role: 'To', address: 'ewa@example.invalid', displayName: 'Ewa Sikora' }],
                english,
                'en',
            ),
        ).toBe('Receipts requested, nothing back yet - Ewa Sikora nothing back yet');
    });
});
