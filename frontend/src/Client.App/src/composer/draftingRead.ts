// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientResult, MailDocument, MailReplyDrafting } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { failureSaid } from '../localization/writingFailures';
import type { ToastKind } from '../toasts/useToasts';

// What asking the deployment for a draft came to, read once for the two places that ask: the composer's own drafting
// block, and the field under a correspondence. Both say a drafting that wrote nothing in the same words, which is why the
// reading is here rather than in either of them.

/** A draft the deployment wrote, or what to tell somebody about one it did not. */
export type DraftingRead =
    | { readonly drafted: true; readonly body: string; readonly document: MailDocument | null }
    | { readonly drafted: false; readonly kind: ToastKind; readonly said: MessageKey };

/**
 * Reads what a drafting came to.
 *
 * A request that failed is an error; a deployment that answered and wrote nothing is a warning, because nothing went
 * wrong — it declined, or the allowance for drafting is spent, and each of those is its own sentence.
 */
export function draftingRead(answer: ClientResult<MailReplyDrafting>): DraftingRead {
    if (answer.outcome === 'failed') {
        return { drafted: false, kind: 'error', said: failureSaid[answer.failure.reason] };
    }

    if (answer.value.outcome !== 'drafted') {
        return {
            drafted: false,
            kind: 'warning',
            said: answer.value.outcome === 'allowanceSpent' ? 'compose.draftAllowanceSpent' : 'compose.notDrafted',
        };
    }

    return { drafted: true, body: answer.value.body, document: answer.value.document };
}
