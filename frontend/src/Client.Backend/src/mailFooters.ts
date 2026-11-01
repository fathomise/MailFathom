// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { maximumAccountsInDirectory } from './mailAccounts';
import { parseMailDocument, type MailDocument } from './mailBody';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// The footer a message sent now from each of the person's accounts would carry, which the service appends on sending.
// It arrives as the same closed document tree a message body does, its images inlined, so a client draws it with what
// already draws a message and fetches nothing more.

/** The route the footers in force are read at, relative to the client prefix. */
export const mailFootersRoute = '/mail-footers';

/** Whose footer an account sends with: the person's own, or the template the organization holding it sets. */
export type MailFooterSource = 'own' | 'organization';

/** The footer one account sends with. */
export interface MailFooterInForce {
    /** The account's identifier. */
    readonly account: string;

    readonly source: MailFooterSource;

    /** The footer as a document, or `null` where it reduced to nothing a client could draw. */
    readonly document: MailDocument | null;
}

/**
 * The most of one footers answer this operation reads, in bytes.
 *
 * A footer at its largest is five images of 256 KiB, which travel as base64 and so a third longer, beside 16 KiB of
 * markup — about 1.7 MiB. This reads nine of those, which is more accounts carrying a footer at its largest than a
 * person is expected to hold; an answer past it is refused as unreadable, and a composer then draws no footer rather
 * than a guessed one.
 */
export const longestMailFootersAnswer = 16 * 1024 * 1024;

const sources: readonly MailFooterSource[] = ['own', 'organization'];

/** Reads the footer each of the signed-in person's accounts sends with; an account sending without one has no entry. */
export function readMailFooters(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<readonly MailFooterInForce[]>> {
    return spanned(`GET ${mailFootersRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailFootersRoute),
            headers: headersFor(session),
            longestAnswer: longestMailFootersAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const footers = parseFooters(response.body);

        return footers === null ? failed('unreadable', response.status) : read(footers);
    });
}

function parseFooters(body: string): readonly MailFooterInForce[] | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const entries = asRecord(parsed)?.['footers'];
    if (!Array.isArray(entries) || entries.length > maximumAccountsInDirectory) {
        return null;
    }

    const footers: MailFooterInForce[] = [];
    for (const entry of entries) {
        const footer = parseFooter(entry);
        if (footer === null) {
            return null;
        }

        footers.push(footer);
    }

    return footers;
}

function parseFooter(value: unknown): MailFooterInForce | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const account = record['account'];
    const source = record['source'];
    const stated = record['document'] ?? null;

    if (typeof account !== 'string' || !isSource(source)) {
        return null;
    }

    const document = stated === null ? null : parseMailDocument(stated);
    if (stated !== null && document === null) {
        return null;
    }

    return { account, source, document };
}

function isSource(value: unknown): value is MailFooterSource {
    return typeof value === 'string' && sources.includes(value as MailFooterSource);
}
