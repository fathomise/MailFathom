// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type { ClientFailureReason, OutgoingMail } from '@mailfathom/client-backend';

// The open account's Outbox as the two surfaces drawing it read it: the list in the list's place, and the preview in the
// pane's. Both read one page and act through one set of decisions, so a send cancelled from the preview leaves the list
// in the same press. The context and its hook sit apart from the provider that fills them for the reason
// `localization/useLocalization.ts` gives.

/** What the open Outbox holds, or why it holds nothing yet. */
export type OutboxPage =
    | { readonly kind: 'reading' }
    | { readonly kind: 'offline' }
    | { readonly kind: 'failed'; readonly reason: ClientFailureReason }
    | { readonly kind: 'read'; readonly sends: readonly OutgoingMail[] };

export interface MailOutbox {
    readonly page: OutboxPage;

    /** Reads the page again, which is the way out of a read the deployment did not answer. */
    readonly readAgain: () => void;

    /** Withdraws a send that has not begun transmitting. */
    readonly cancel: (send: OutgoingMail) => void;

    /** Offers a refused send, or one whose outcome is unknown, again. */
    readonly sendAgain: (send: OutgoingMail) => void;

    /** Takes a refused send, or one whose outcome is unknown, out of the Outbox, for as long as its toast offers it back. */
    readonly dismiss: (send: OutgoingMail) => void;
}

export const MailOutboxContext = createContext<MailOutbox | null>(null);

export function useMailOutbox(): MailOutbox {
    const outbox = useContext(MailOutboxContext);

    if (outbox === null) {
        throw new Error('A component read the Outbox outside the MailOutboxProvider the frame mounts.');
    }

    return outbox;
}
