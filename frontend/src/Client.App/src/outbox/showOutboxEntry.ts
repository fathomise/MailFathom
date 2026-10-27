// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientFailureReason, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import type { Translate } from '../localization/useLocalization';
import { failureLabels } from '../pendingChanges/changeWording';
import { addressOf } from '../routing/spaces';
import type { WorkspaceRevision } from '../workspace/useWorkspace';
import { readSentFolder } from './sentFolder';

/**
 * Opens one send in its account's Outbox, which a toast after a send and a notification about a refusal both do: the
 * Mail space in front, the Outbox open with the send picked, and its account's Sent folder in scope behind it once the
 * deployment has named that folder.
 */
export function showOutboxEntry(
    session: ClientSession,
    transport: MailFathomTransport,
    revise: WorkspaceRevision['revise'],
    account: string,
    outgoingEmailId: string,
): void {
    window.location.hash = addressOf('mail');

    revise({ outbox: { account, entry: outgoingEmailId }, selection: null, conversation: null, selected: [] });

    void readSentFolder(session, transport, account).then((sent) => {
        if (sent !== null) {
            revise(({ outbox }) => (outbox?.account === account ? { scope: sent, outbox } : {}));
        }
    });
}

/** What is said where a send could not be shown: that the server holds it no longer, or why it could not be read. */
export function wordUnshownSend(reason: ClientFailureReason, translate: Translate): string {
    return reason === 'missing'
        ? translate('compose.noSuchSend')
        : translate('compose.sendNotRead', { reason: translate(failureLabels[reason]) });
}
