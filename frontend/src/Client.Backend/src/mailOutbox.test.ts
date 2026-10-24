// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import {
    dismissOutgoingMail,
    mailOutboxPageSize,
    offerOutgoingMailAgain,
    readMailOutbox,
    readOutgoingMail,
    withdrawOutgoingMail,
    type MailOutboxDecision,
    type MailSendWithdrawal,
} from './mailOutbox';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const outgoingEmailId = 'e9d1a2b3-0000-4000-8000-00000000abcd';

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function deciding(outcome: string): Answer {
    return { status: 200, body: JSON.stringify({ outgoingEmail: outgoingEmailId, outcome }) };
}

describe('withdrawOutgoingMail', () => {
    it('names the one send it withdraws on the client surface’s cancellation route', async () => {
        const requests: ClientRequest[] = [];

        await withdrawOutgoingMail(
            session,
            (request) => {
                requests.push(request);

                return Promise.resolve({ ...deciding('Accepted'), headers: {} });
            },
            outgoingEmailId,
        );

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/outbox/cancellation');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({ outgoingEmail: outgoingEmailId });
    });

    it.each<{ answered: string; withdrawal: MailSendWithdrawal }>([
        { answered: 'Accepted', withdrawal: 'withdrawn' },
        { answered: 'AttemptUnderWay', withdrawal: 'alreadyBeingSent' },
        { answered: 'StageDoesNotAllowIt', withdrawal: 'pastRecall' },
        { answered: 'RecordUnknown', withdrawal: 'noSuchSend' },
    ])('reads $answered as $withdrawal', async ({ answered, withdrawal }) => {
        const answer = await withdrawOutgoingMail(session, answering(deciding(answered)), outgoingEmailId);

        expect(answer).toStrictEqual({ outcome: 'read', value: withdrawal });
    });

    it('refuses an outcome that belongs to offering a send again as unreadable', async () => {
        const answer = await withdrawOutgoingMail(session, answering(deciding('RefusalNotRestated')), outgoingEmailId);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('refuses a body that is not JSON as unreadable', async () => {
        const answer = await withdrawOutgoingMail(
            session,
            answering({ status: 200, body: 'not json' }),
            outgoingEmailId,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });

    it('reads a credential without the sending grant as one the deployment will not do this for', async () => {
        const answer = await withdrawOutgoingMail(session, answering({ status: 403, body: '' }), outgoingEmailId);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });

    it('reads a deployment that never answered as one to try again', async () => {
        const answer = await withdrawOutgoingMail(
            session,
            () => Promise.reject(new Error('no route to host')),
            outgoingEmailId,
        );

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });
});

function recording(response: Answer, requests: ClientRequest[]): MailFathomTransport {
    return (request) => {
        requests.push(request);

        return Promise.resolve({ ...response, headers: {} });
    };
}

function page(sends: readonly unknown[], nextCursor: string | null = null): Answer {
    return { status: 200, body: JSON.stringify({ sends, nextCursor }) };
}

function sending(value: unknown): Answer {
    return { status: 200, body: JSON.stringify(value) };
}

const unreadable = { outcome: 'failed', failure: { reason: 'unreadable', status: 200 } };

const refusedRow = {
    outgoingEmail: outgoingEmailId,
    account: 'work',
    stage: 'Refused',
    heldByAttempt: false,
    recipients: [{ address: 'ola@example.invalid', role: 'To' }],
    otherRecipientCount: 2,
    subject: 'Quarterly figures',
    recordedAt: '2026-10-20T09:00:00+00:00',
    dueAt: null,
    dueTimeZone: null,
    availableAt: '2026-10-20T09:00:00+00:00',
    attemptCount: 3,
    lastFailureCode: 'MF-40301',
    lastReplyCode: 550,
    failure: 'The server refused the message.',
};

const preview = {
    ...refusedRow,
    recipients: [
        { address: 'ola@example.invalid', role: 'To', status: 'Refused', lastReplyCode: 550, answeredAt: null },
        { address: 'jan@example.invalid', role: 'Cc', status: 'Pending', lastReplyCode: null, answeredAt: null },
    ],
    inOutbox: true,
    dismissedAt: null,
    sender: { role: 'From', address: 'me@example.invalid', displayName: 'Me' },
    body: {
        availability: 'Readable',
        plainText: { text: 'Here are the figures.', originalCharacterCount: 21, truncation: null },
        document: null,
    },
    attachments: [{ fileName: 'figures.pdf', mediaType: 'application/pdf', sizeOctets: 2048 }],
};

describe('readMailOutbox', () => {
    it('asks for one account’s outbox at the most a page holds', async () => {
        const requests: ClientRequest[] = [];

        await readMailOutbox(session, recording(page([]), requests), 'work');

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe(
            `https://mail.example.invalid/api/client/outbox?account=work&pageSize=${String(mailOutboxPageSize)}`,
        );
    });

    it('reads every row of a page, and whether the outbox holds more', async () => {
        const answer = await readMailOutbox(session, answering(page([refusedRow], 'next')), 'work');

        expect(answer).toStrictEqual({
            outcome: 'read',
            value: {
                continues: true,
                sends: [
                    {
                        outgoingEmailId,
                        account: 'work',
                        stage: 'Refused',
                        heldByAttempt: false,
                        recipients: [{ address: 'ola@example.invalid', role: 'To' }],
                        otherRecipientCount: 2,
                        subject: 'Quarterly figures',
                        recordedAt: '2026-10-20T09:00:00+00:00',
                        dueAt: null,
                        availableAt: '2026-10-20T09:00:00+00:00',
                        attemptCount: 3,
                        failure: 'The server refused the message.',
                    },
                ],
            },
        });
    });

    it.each<{ shape: string; row: unknown }>([
        { shape: 'a stage only a send that left stands at', row: { ...refusedRow, stage: 'Sent' } },
        { shape: 'a stage nobody named', row: { ...refusedRow, stage: 'Lost' } },
        {
            shape: 'more recipients than a row names',
            row: { ...refusedRow, recipients: Array.from({ length: 4 }, () => refusedRow.recipients[0]) },
        },
        {
            shape: 'a recipient in no header',
            row: { ...refusedRow, recipients: [{ address: 'a@example.invalid', role: 'Reply-To' }] },
        },
        { shape: 'a negative attempt count', row: { ...refusedRow, attemptCount: -1 } },
        { shape: 'no identifier', row: { ...refusedRow, outgoingEmail: '' } },
        { shape: 'no instant it may be taken at', row: { ...refusedRow, availableAt: null } },
    ])('refuses a row with $shape', async ({ row }) => {
        const answer = await readMailOutbox(session, answering(page([row])), 'work');

        expect(answer).toStrictEqual(unreadable);
    });

    it('refuses a page longer than it asked for', async () => {
        const sends = Array.from({ length: mailOutboxPageSize + 1 }, () => refusedRow);

        const answer = await readMailOutbox(session, answering(page(sends)), 'work');

        expect(answer).toStrictEqual(unreadable);
    });

    it('reads a credential without the grants as one the deployment will not do this for', async () => {
        const answer = await readMailOutbox(session, answering({ status: 403, body: '' }), 'work');

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'unauthorized', status: 403 } });
    });
});

describe('readOutgoingMail', () => {
    it('asks for the one send by its identifier', async () => {
        const requests: ClientRequest[] = [];

        await readOutgoingMail(session, recording(sending(preview), requests), outgoingEmailId);

        expect(requests[0]?.path).toBe(`https://mail.example.invalid/api/client/outbox/${outgoingEmailId}`);
    });

    it('reads the send whole, with every recipient and what the server said about each', async () => {
        const answer = await readOutgoingMail(session, answering(sending(preview)), outgoingEmailId);

        expect(answer).toMatchObject({
            outcome: 'read',
            value: {
                inOutbox: true,
                sender: 'me@example.invalid',
                recipients: [
                    { address: 'ola@example.invalid', role: 'To', status: 'Refused' },
                    { address: 'jan@example.invalid', role: 'Cc', status: 'Pending' },
                ],
                text: 'Here are the figures.',
                attachments: [{ fileName: 'figures.pdf', mediaType: 'application/pdf', sizeOctets: 2048 }],
            },
        });
    });

    it('reads a send that has left by the stage it left at', async () => {
        const answer = await readOutgoingMail(
            session,
            answering(sending({ ...preview, stage: 'Sent', inOutbox: false })),
            outgoingEmailId,
        );

        expect(answer).toMatchObject({ outcome: 'read', value: { stage: 'Sent', inOutbox: false } });
    });

    it.each<{ shape: string; body: unknown }>([
        { shape: 'a message that could not be read', body: null },
        { shape: 'a message held back from reading', body: { ...preview.body, availability: 'Withheld' } },
    ])('reads $shape as no text', async ({ body }) => {
        const answer = await readOutgoingMail(
            session,
            answering(sending({ ...preview, body, sender: null })),
            outgoingEmailId,
        );

        expect(answer).toMatchObject({ outcome: 'read', value: { text: null, sender: null } });
    });

    it('reads a send this user does not hold as missing', async () => {
        const answer = await readOutgoingMail(session, answering({ status: 404, body: '' }), outgoingEmailId);

        expect(answer).toStrictEqual({ outcome: 'failed', failure: { reason: 'missing', status: 404 } });
    });

    it.each<{ shape: string; value: unknown }>([
        { shape: 'a recipient with no status', value: { ...preview, recipients: refusedRow.recipients } },
        { shape: 'no word on whether it is in the outbox', value: { ...preview, inOutbox: 'yes' } },
        { shape: 'a sender with no address', value: { ...preview, sender: { role: 'From' } } },
        { shape: 'a body with no words', value: { ...preview, body: { availability: 'Readable' } } },
        {
            shape: 'an attachment of no size',
            value: { ...preview, attachments: [{ fileName: null, mediaType: 'text/plain' }] },
        },
    ])('refuses a send with $shape', async ({ value }) => {
        const answer = await readOutgoingMail(session, answering(sending(value)), outgoingEmailId);

        expect(answer).toStrictEqual(unreadable);
    });
});

describe('offerOutgoingMailAgain', () => {
    it('names the send and whether its refusal was restated on the requeue route', async () => {
        const requests: ClientRequest[] = [];

        await offerOutgoingMailAgain(session, recording(deciding('Accepted'), requests), outgoingEmailId, true);

        expect(requests[0]?.method).toBe('POST');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/outbox/requeue');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({
            outgoingEmail: outgoingEmailId,
            refusalRestated: true,
        });
    });

    it.each<{ answered: string; decision: MailOutboxDecision }>([
        { answered: 'Accepted', decision: 'accepted' },
        { answered: 'AttemptUnderWay', decision: 'alreadyBeingSent' },
        { answered: 'StageDoesNotAllowIt', decision: 'pastRecall' },
        { answered: 'RecordUnknown', decision: 'noSuchSend' },
        { answered: 'RefusalNotRestated', decision: 'refusalNotRestated' },
    ])('reads $answered as $decision', async ({ answered, decision }) => {
        const answer = await offerOutgoingMailAgain(session, answering(deciding(answered)), outgoingEmailId, false);

        expect(answer).toStrictEqual({ outcome: 'read', value: decision });
    });

    it('refuses an outcome nobody named as unreadable', async () => {
        const answer = await offerOutgoingMailAgain(session, answering(deciding('Maybe')), outgoingEmailId, false);

        expect(answer).toStrictEqual(unreadable);
    });
});

describe('dismissOutgoingMail', () => {
    it('names the one send it takes out of the outbox on the dismissal route', async () => {
        const requests: ClientRequest[] = [];

        const answer = await dismissOutgoingMail(session, recording(deciding('Accepted'), requests), outgoingEmailId);

        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/outbox/dismissal');
        expect(JSON.parse(requests[0]?.body ?? '')).toStrictEqual({ outgoingEmail: outgoingEmailId });
        expect(answer).toStrictEqual({ outcome: 'read', value: 'accepted' });
    });
});
