// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord, parseEach } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// What came back for the account's own mail, what a report that arrived as mail says, and a received message's request
// for a read receipt — the three receipt fields the deployment publishes beside a message — and the one write that
// answers such a request.
//
// A report is a message anybody could have written, so what it says is information rather than proof; the parsers
// hold it to the closed sets the service publishes and refuse anything else, as they would any other field.

/** Which report a message was recognized as. */
export type MailReportKind = 'Delivery' | 'Read';

/**
 * What a report said about one recipient: a delivery report's action, or a read receipt's disposition.
 *
 * A delivery report says `Delivered`, `Relayed`, `Expanded`, `Failed`, or `Delayed`; a read receipt says `Displayed`,
 * `Deleted`, `Dispatched`, or `Processed`.
 */
export type MailReceiptOutcome =
    'Delivered' | 'Relayed' | 'Expanded' | 'Failed' | 'Delayed' | 'Displayed' | 'Deleted' | 'Dispatched' | 'Processed';

/** Where an outcome came from: a report that arrived as mail, or the deployment observing two accounts of one organization. */
export type MailReceiptSource = 'Report' | 'Organization';

/** What a report that arrived as mail said about one recipient it named. */
export interface MailReportRecipient {
    readonly address: string;
    readonly outcome: MailReceiptOutcome;
}

/** A message recognized as a delivery report or a read receipt when it arrived. */
export interface MailReport {
    readonly kind: MailReportKind;

    /** What it said about each recipient it named, in its own order. */
    readonly recipients: readonly MailReportRecipient[];

    /** When it says it was written, or when it arrived where it carried no usable date. */
    readonly reportedAt: string | null;

    /** Whether it named a message this account sent. */
    readonly namesOwnMessage: boolean;

    /** The subject of that message, where it named one and that one had a subject. */
    readonly ownMessageSubject: string | null;
}

/** What came back for the account's own message, counted, which is what a row in a list carries. */
export interface MailReceiptSummary {
    /** How many recipients the message was sent to, which every count below is out of. */
    readonly recipients: number;
    readonly delivery: {
        readonly requested: boolean;

        /** Delivered, relayed onward, or expanded to a list. */
        readonly delivered: number;
        readonly delayed: number;
        readonly failed: number;
    };
    readonly read: {
        readonly requested: boolean;

        /** How many recipients' mail clients reported the message displayed. */
        readonly read: number;
    };
}

/** One outcome recorded for one recipient of the account's own message. */
export interface MailRecordedReceipt {
    readonly outcome: MailReceiptOutcome;
    readonly reportedAt: string;
    readonly source: MailReceiptSource;

    /** A delivery report's RFC 3463 status code, such as `5.1.1`, where it gave one. */
    readonly status: string | null;

    /** The server a delivery report names as having reported it, where it named one. */
    readonly reportingServer: string | null;
}

/** What came back about one recipient of the account's own message. */
export interface MailRecipientReceipts {
    readonly address: string;
    readonly delivery: MailRecordedReceipt | null;
    readonly read: MailRecordedReceipt | null;
}

/** What the account's own message asked to be told and what came back, recipient by recipient. */
export interface MailReceipts {
    readonly deliveryRequested: boolean;
    readonly readRequested: boolean;

    /** Every recipient the message was sent to, in its own order. */
    readonly recipients: readonly MailRecipientReceipts[];
}

/** A received message's request for a read receipt, and whether this account answered it. */
export interface MailReadReceiptRequest {
    /** The address a receipt would go to. */
    readonly address: string;

    /** Whether that address is neither the message's return path nor its author, which a reader is told before answering. */
    readonly namesAnotherAddress: boolean;

    /** When this account sent the receipt, or `null` while it has sent none. */
    readonly sentAt: string | null;
}

/**
 * What asking for a read receipt to be sent came to: sent — now, or earlier, in which case the time is that earlier
 * one — or refused by the deployment for a reason no second attempt changes.
 */
export type MailReadReceiptSending =
    { readonly outcome: 'sent'; readonly sentAt: string } | { readonly outcome: 'refused' };

/** The route the read receipt one message asked for is sent through, relative to the client prefix. */
export function mailReadReceiptRoute(storedEmailId: string): string {
    return `/messages/${encodeURIComponent(storedEmailId)}/read-receipt`;
}

// Far above anything a mail server produces, and there for the answer that is not a message at all.
const mostRecipients = 2048;
const longestText = 4096;
const longestAnswer = 16 * 1024;

const reportKinds: readonly MailReportKind[] = ['Delivery', 'Read'];

const outcomes: readonly MailReceiptOutcome[] = [
    'Delivered',
    'Relayed',
    'Expanded',
    'Failed',
    'Delayed',
    'Displayed',
    'Deleted',
    'Dispatched',
    'Processed',
];

const sources: readonly MailReceiptSource[] = ['Report', 'Organization'];

/**
 * Sends the read receipt one received message asked for, once, answering an expected failure as a value.
 *
 * A receipt already sent — from another device, or a press whose answer never arrived — answers as sent, with the time
 * it was, because what the reader is owed is that it went rather than that this press was not the one that sent it.
 *
 * @param session Who is asking and where.
 * @param transport How the request goes out.
 * @param storedEmailId The message being answered.
 * @returns Whether it was sent and when, or why the request never answered.
 */
export function sendReadReceipt(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailId: string,
): Promise<ClientResult<MailReadReceiptSending>> {
    const route = mailReadReceiptRoute(storedEmailId);

    return spanned(`POST ${route}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            path: routeFor(session, route),
            headers: headersFor(session),
            longestAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        // The one route here naming a single message, so its `404` is that message gone rather than a surface missing.
        if (response.status === 404) {
            return failed('missing', response.status);
        }

        if (response.status === 409) {
            const sentAt = asRecord(bodyOf(response.body))?.['sentAt'];

            return read(isInstant(sentAt) ? { outcome: 'sent', sentAt } : { outcome: 'refused' });
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const sentAt = asRecord(bodyOf(response.body))?.['sentAt'];

        return isInstant(sentAt) ? read({ outcome: 'sent', sentAt }) : failed('unreadable', response.status);
    });
}

/**
 * Reads a message's report mark: the report, `null` for every message that is none, or `undefined` for a field that
 * is neither.
 */
export function parseReport(value: unknown): MailReport | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    if (record === null) {
        return undefined;
    }

    const kind = record['kind'];
    const reportedAt = record['reportedAt'] ?? null;
    const namesOwnMessage = record['namesOwnMessage'];
    const ownMessageSubject = record['ownMessageSubject'] ?? null;
    const recipients = parseEach(record['recipients'], mostRecipients, parseReportRecipient);

    if (!isOneOf(reportKinds, kind) || recipients === null || typeof namesOwnMessage !== 'boolean') {
        return undefined;
    }

    if (!isOptionalText(reportedAt) || !isOptionalText(ownMessageSubject)) {
        return undefined;
    }

    return { kind, recipients, reportedAt, namesOwnMessage, ownMessageSubject };
}

/**
 * Reads what came back for the account's own message, counted: the summary, `null` where it asked for nothing and
 * nothing came, or `undefined` for a field that is neither.
 */
export function parseReceiptSummary(value: unknown): MailReceiptSummary | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    const delivery = asRecord(record?.['delivery']);
    const readReceipt = asRecord(record?.['read']);

    if (record === null || delivery === null || readReceipt === null) {
        return undefined;
    }

    const recipients = record['recipients'];
    const deliveryRequested = delivery['requested'];
    const readRequested = readReceipt['requested'];
    const delivered = delivery['delivered'];
    const delayed = delivery['delayed'];
    const failedCount = delivery['failed'];
    const readCount = readReceipt['read'];

    if (typeof deliveryRequested !== 'boolean' || typeof readRequested !== 'boolean') {
        return undefined;
    }

    if (
        !isCount(recipients) ||
        !isCount(delivered) ||
        !isCount(delayed) ||
        !isCount(failedCount) ||
        !isCount(readCount)
    ) {
        return undefined;
    }

    // A count is of the recipients, so one past their number is an answer no deployment produces.
    if (delivered + delayed + failedCount > recipients || readCount > recipients) {
        return undefined;
    }

    return {
        recipients,
        delivery: { requested: deliveryRequested, delivered, delayed, failed: failedCount },
        read: { requested: readRequested, read: readCount },
    };
}

/**
 * Reads what came back for the account's own message, recipient by recipient: the receipts, `null` where it asked for
 * nothing and nothing came, or `undefined` for a field that is neither.
 */
export function parseReceipts(value: unknown): MailReceipts | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    if (record === null) {
        return undefined;
    }

    const deliveryRequested = record['deliveryRequested'];
    const readRequested = record['readRequested'];
    const recipients = parseEach(record['recipients'], mostRecipients, parseRecipientReceipts);

    if (typeof deliveryRequested !== 'boolean' || typeof readRequested !== 'boolean' || recipients === null) {
        return undefined;
    }

    return { deliveryRequested, readRequested, recipients };
}

/**
 * Reads a received message's request for a read receipt: the request, `null` where it asked for none or is spam, or
 * `undefined` for a field that is neither.
 */
export function parseReadReceiptRequest(value: unknown): MailReadReceiptRequest | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    if (record === null) {
        return undefined;
    }

    const address = record['address'];
    const namesAnotherAddress = record['namesAnotherAddress'];
    const sentAt = record['sentAt'] ?? null;

    if (!isAddress(address) || typeof namesAnotherAddress !== 'boolean' || (sentAt !== null && !isInstant(sentAt))) {
        return undefined;
    }

    return { address, namesAnotherAddress, sentAt };
}

function parseReportRecipient(value: unknown): MailReportRecipient | null {
    const record = asRecord(value);
    const address = record?.['address'];
    const outcome = record?.['outcome'];

    return isAddress(address) && isOneOf(outcomes, outcome) ? { address, outcome } : null;
}

function parseRecipientReceipts(value: unknown): MailRecipientReceipts | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const address = record['address'];
    const delivery = parseRecordedReceipt(record['delivery']);
    const readReceipt = parseRecordedReceipt(record['read']);

    if (!isAddress(address) || delivery === undefined || readReceipt === undefined) {
        return null;
    }

    return { address, delivery, read: readReceipt };
}

function parseRecordedReceipt(value: unknown): MailRecordedReceipt | null | undefined {
    if (value === undefined || value === null) {
        return null;
    }

    const record = asRecord(value);
    if (record === null) {
        return undefined;
    }

    const outcome = record['outcome'];
    const reportedAt = record['reportedAt'];
    const source = record['source'];
    const status = record['status'] ?? null;
    const reportingServer = record['reportingServer'] ?? null;

    if (!isOneOf(outcomes, outcome) || !isInstant(reportedAt) || !isOneOf(sources, source)) {
        return undefined;
    }

    if (!isOptionalText(status) || !isOptionalText(reportingServer)) {
        return undefined;
    }

    return { outcome, reportedAt, source, status, reportingServer };
}

function bodyOf(body: string): unknown {
    try {
        return JSON.parse(body);
    } catch {
        return null;
    }
}

function isOneOf<TValue extends string>(members: readonly TValue[], value: unknown): value is TValue {
    return typeof value === 'string' && members.includes(value as TValue);
}

function isAddress(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestText;
}

function isOptionalText(value: unknown): value is string | null {
    return value === null || (typeof value === 'string' && value.length <= longestText);
}

// An instant the service sent, which is a string this client can read as one.
function isInstant(value: unknown): value is string {
    return typeof value === 'string' && value.length <= 64 && !Number.isNaN(Date.parse(value));
}

function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
