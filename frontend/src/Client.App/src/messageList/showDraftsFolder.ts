// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { readMailDraft, type ClientSession, type MailFathomTransport } from '@mailfathom/client-backend';
import { readFolderWithRole } from '../outbox/sentFolder';
import type { WorkspaceRevision } from '../workspace/useWorkspace';

/**
 * Puts the Drafts folder of the account a kept draft belongs to behind the composer carrying it on, which is where a
 * draft notification and its toast lead: the draft names its account, and the folders route names that account's
 * Drafts. Where either does not answer, the list stays where it was and the composer says what it could read.
 */
export async function showDraftsFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    revise: WorkspaceRevision['revise'],
    draftId: string,
): Promise<void> {
    const reading = await readMailDraft(session, transport, draftId);

    if (reading.outcome === 'failed') {
        return;
    }

    const drafts = await readFolderWithRole(session, transport, reading.value.draft.account, 'Drafts');

    if (drafts !== null) {
        revise({ scope: drafts, outbox: null, selection: null, conversation: null, selected: [] });
    }
}
