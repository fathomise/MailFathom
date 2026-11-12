// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientFailureReason } from '@mailfathom/client-backend';
import type { MessageKey } from './en';

/**
 * What each of the five failures means for somebody trying to write mail, said as what they do next.
 *
 * Everything that writes mail reads its failures from this one table — the composer's reading and saving, a drafting,
 * and a rewrite of part of a draft — because four of the five are the same answer to each of them. `missing` reaches
 * only the read: the message route is the one that reads a `404` as something the deployment no longer holds, so the
 * sentence is about the message being answered rather than about a draft.
 */
export const failureSaid: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'compose.failedUnauthenticated',
    unauthorized: 'compose.failedUnauthorized',
    unavailable: 'compose.failedUnavailable',
    unreadable: 'compose.failedUnreadable',
    missing: 'compose.failedMissing',
};
