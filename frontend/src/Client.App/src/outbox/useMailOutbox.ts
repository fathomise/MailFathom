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

/** A decision about one send that the deployment has not answered yet, which holds that send's controls. */
export interface OutboxDecisionUnderWay {
    readonly outgoingEmailId: string;
    readonly decision: 'cancel' | 'again' | 'edit';
}

/** A decision about one send asked for somewhere the Outbox is not drawn, which it takes once that send is on its page. */
export interface OutboxDecisionAsked {
    readonly account: string;
    readonly outgoingEmailId: string;
    readonly decision: 'again' | 'edit';

    /** Told once the Outbox has the send in front of it and takes the decision, which is what reads its notification. */
    readonly onTaken: () => void;
}

export interface MailOutbox {
    readonly page: OutboxPage;

    /** The decisions asked for and not answered yet; a send named here takes no other until its answer lands. */
    readonly underWay: readonly OutboxDecisionUnderWay[];

    /** Reads the page again, which is the way out of a read the deployment did not answer. */
    readonly readAgain: () => void;

    /**
     * Leaves the Outbox for its account's Sent folder, asked of the deployment rather than read off what is behind it:
     * an Outbox opened from a send toast stands over whatever was listed until that folder is known. Where the
     * deployment names none it closes the Outbox onto whatever is behind it, which is the one way out left.
     */
    readonly goToSent: () => Promise<void>;

    /** Withdraws a send that has not begun transmitting. */
    readonly cancel: (send: OutgoingMail) => void;

    /** Offers a refused send, or one whose outcome is unknown, again. */
    readonly sendAgain: (send: OutgoingMail) => void;

    /** Takes a refused send, or one whose outcome is unknown, out of the Outbox, for as long as its toast offers it back. */
    readonly dismiss: (send: OutgoingMail) => void;

    /**
     * Takes a refused send back to edit: it leaves the Outbox with nothing sent, and the composer opens on the draft it
     * was written in, which goes on being that draft.
     */
    readonly edit: (send: OutgoingMail) => void;
}

export const MailOutboxContext = createContext<MailOutbox | null>(null);

export function useMailOutbox(): MailOutbox {
    const outbox = useContext(MailOutboxContext);

    if (outbox === null) {
        throw new Error('A component read the Outbox outside the MailOutboxProvider the frame mounts.');
    }

    return outbox;
}
