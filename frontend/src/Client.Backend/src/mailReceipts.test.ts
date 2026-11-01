// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    mailReadReceiptRoute,
    parseReadReceiptRequest,
    parseReceipts,
    parseReceiptSummary,
    parseReport,
    sendReadReceipt,
} from './mailReceipts';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

function answering(response: ClientResponse, asked: ClientRequest[] = []) {
    return (request: ClientRequest) => {
        asked.push(request);

        return Promise.resolve(response);
    };
}

describe('parseReport', () => {
    it('reads a delivery report naming the account’s own message', () => {
        expect(
            parseReport({
                kind: 'Delivery',
                recipients: [{ address: 'r.lis@northwind.example', outcome: 'Failed' }],
                reportedAt: '2026-08-24T09:13:00+00:00',
                namesOwnMessage: true,
                ownMessageSubject: 'Quote for the reporting module',
            }),
        ).toStrictEqual({
            kind: 'Delivery',
            recipients: [{ address: 'r.lis@northwind.example', outcome: 'Failed' }],
            reportedAt: '2026-08-24T09:13:00+00:00',
            namesOwnMessage: true,
            ownMessageSubject: 'Quote for the reporting module',
        });
    });

    it('reads a message that is no report as no report', () => {
        expect(parseReport(null)).toBeNull();
        expect(parseReport(undefined)).toBeNull();
    });

    it('refuses an outcome no report publishes', () => {
        expect(
            parseReport({
                kind: 'Delivery',
                recipients: [{ address: 'r.lis@northwind.example', outcome: 'Vanished' }],
                reportedAt: null,
                namesOwnMessage: false,
                ownMessageSubject: null,
            }),
        ).toBeUndefined();
    });
});

describe('parseReceiptSummary', () => {
    const summary = {
        recipients: 3,
        delivery: { requested: true, delivered: 3, delayed: 0, failed: 0 },
        read: { requested: true, read: 2 },
    };

    it('reads the counts a row carries', () => {
        expect(parseReceiptSummary(summary)).toStrictEqual(summary);
    });

    it('reads a message that asked for nothing and got nothing as having no summary', () => {
        expect(parseReceiptSummary(null)).toBeNull();
    });

    it('refuses a count larger than the recipients it is out of', () => {
        expect(parseReceiptSummary({ ...summary, read: { requested: true, read: 4 } })).toBeUndefined();
    });
});

describe('parseReceipts', () => {
    it('reads each recipient’s own outcomes, where they came from, and what the reports named', () => {
        expect(
            parseReceipts({
                deliveryRequested: true,
                readRequested: false,
                recipients: [
                    {
                        address: 'j.wrona@lawoffice.example',
                        delivery: {
                            outcome: 'Delivered',
                            reportedAt: '2026-08-26T16:21:00+00:00',
                            source: 'Report',
                            status: '2.0.0',
                            reportingServer: 'mx1.contoso.example',
                        },
                        read: null,
                    },
                ],
            }),
        ).toStrictEqual({
            deliveryRequested: true,
            readRequested: false,
            recipients: [
                {
                    address: 'j.wrona@lawoffice.example',
                    delivery: {
                        outcome: 'Delivered',
                        reportedAt: '2026-08-26T16:21:00+00:00',
                        source: 'Report',
                        status: '2.0.0',
                        reportingServer: 'mx1.contoso.example',
                    },
                    read: null,
                },
            ],
        });
    });

    it('refuses an outcome from a source the service does not publish', () => {
        expect(
            parseReceipts({
                deliveryRequested: true,
                readRequested: true,
                recipients: [
                    {
                        address: 'j.wrona@lawoffice.example',
                        delivery: null,
                        read: {
                            outcome: 'Displayed',
                            reportedAt: '2026-08-26T16:48:00+00:00',
                            source: 'Rumour',
                            status: null,
                            reportingServer: null,
                        },
                    },
                ],
            }),
        ).toBeUndefined();
    });
});

describe('parseReadReceiptRequest', () => {
    it('reads where a receipt would go and whether one was sent', () => {
        expect(
            parseReadReceiptRequest({
                address: 'campaign-tracking@mailer.example',
                namesAnotherAddress: true,
                sentAt: null,
            }),
        ).toStrictEqual({ address: 'campaign-tracking@mailer.example', namesAnotherAddress: true, sentAt: null });
    });

    it('reads a message that asks for none as asking for none', () => {
        expect(parseReadReceiptRequest(null)).toBeNull();
    });

    it('refuses a sending time that is no instant', () => {
        expect(
            parseReadReceiptRequest({ address: 'a@b.example', namesAnotherAddress: false, sentAt: 'yesterday' }),
        ).toBeUndefined();
    });
});

describe('sendReadReceipt', () => {
    it('asks for the one receipt the message asked for, with nothing in the request to say what it is', async () => {
        const asked: ClientRequest[] = [];

        await sendReadReceipt(
            session,
            answering({ status: 200, body: '{"sentAt":"2026-11-01T10:57:00+00:00"}', headers: {} }, asked),
            messageId,
        );

        expect(asked).toEqual([
            expect.objectContaining({
                method: 'POST',
                path: `https://mail.example.invalid/api/client${mailReadReceiptRoute(messageId)}`,
            }),
        ]);
        expect(asked[0]?.body).toBeUndefined();
    });

    it('answers when the receipt was sent', async () => {
        const result = await sendReadReceipt(
            session,
            answering({ status: 200, body: '{"sentAt":"2026-11-01T10:57:00+00:00"}', headers: {} }),
            messageId,
        );

        expect(result).toStrictEqual({
            outcome: 'read',
            value: { outcome: 'sent', sentAt: '2026-11-01T10:57:00+00:00' },
        });
    });

    it('answers a receipt sent earlier as sent, at the time it was', async () => {
        const result = await sendReadReceipt(
            session,
            answering({
                status: 409,
                body: '{"reason":"AlreadySent","sentAt":"2026-08-29T08:52:00+00:00"}',
                headers: {},
            }),
            messageId,
        );

        expect(result).toStrictEqual({
            outcome: 'read',
            value: { outcome: 'sent', sentAt: '2026-08-29T08:52:00+00:00' },
        });
    });

    it('answers any other conflict as the deployment refusing to send one', async () => {
        const result = await sendReadReceipt(
            session,
            answering({ status: 409, body: '{"reason":"Spam"}', headers: {} }),
            messageId,
        );

        expect(result).toStrictEqual({ outcome: 'read', value: { outcome: 'refused' } });
    });

    it('answers a message no longer held as gone', async () => {
        const result = await sendReadReceipt(session, answering({ status: 404, body: '', headers: {} }), messageId);

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it('answers a refused grant as the failure it is', async () => {
        const result = await sendReadReceipt(session, answering({ status: 403, body: '', headers: {} }), messageId);

        expect(result).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });
});
