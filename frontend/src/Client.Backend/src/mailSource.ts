// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

/** The route one message's raw source is served at, relative to the client prefix. */
export function mailSourceRoute(storedEmailId: string): string {
    return `/messages/${encodeURIComponent(storedEmailId)}/source`;
}

/** The most of one source the service serves, in octets, and so the most of one answer read. */
export const longestMailSource = 1_048_576;

/** The header a cut source states the size of the whole message in, as the transport lower-cases it. */
const truncatedFromHeader = 'mailfathom-source-truncated-from';

/**
 * One message's source as received, or the fact that this deployment withholds it.
 *
 * Withheld is a value rather than a failure: the deployment was reached and decided, because a message whose mail is
 * screened for sensitive content has a body that is redacted when read, and its raw octets cannot be.
 */
export type MailSource =
    | {
          readonly withheld: false;

          /** Every header and every part, as the message arrived, cut where `wholeOctets` says it was. */
          readonly text: string;

          /** How many octets the whole message holds where the source was cut, or `null` where `text` is all of it. */
          readonly wholeOctets: number | null;
      }
    | { readonly withheld: true };

/**
 * Reads one message's raw source from the local copy, which the service asks no mail server for.
 *
 * A `404` is `missing` here, because this route names one message: there is no local copy of it this reader may see.
 */
export function readMailSource(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailId: string,
): Promise<ClientResult<MailSource>> {
    return spanned('GET /messages/{storedEmailId}/source', async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailSourceRoute(storedEmailId)),
            headers: { ...headersFor(session), Accept: 'text/plain' },
            longestAnswer: longestMailSource,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        switch (response.status) {
            // The transport answers an over-bound or bodiless answer with nothing, and no stored message is empty.
            case 200:
                return response.body === ''
                    ? failed('unreadable', response.status)
                    : read({ withheld: false, text: response.body, wholeOctets: wholeOctetsOf(response.headers) });
            case 404:
                return failed('missing', response.status);
            case 409:
                return read({ withheld: true });
            default:
                return failed(failureReasonForStatus(response.status), response.status);
        }
    });
}

function wholeOctetsOf(headers: Readonly<Record<string, string>>): number | null {
    const stated = headers[truncatedFromHeader];

    if (stated === undefined || !/^\d+$/u.test(stated)) {
        return null;
    }

    const wholeOctets = Number(stated);

    return Number.isSafeInteger(wholeOctets) ? wholeOctets : null;
}
