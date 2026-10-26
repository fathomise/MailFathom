// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it, vi } from 'vitest';
import type { ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { mailFolders, workAccount } from '../../../../tests/fixtures/deployment';
import * as drafts from '../../../../tests/fixtures/drafts';
import { showDraftsFolder } from './showDraftsFolder';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function answering(draft: { readonly status: number; readonly body: unknown }): MailFathomTransport {
    return (request) =>
        Promise.resolve(
            request.path.endsWith('/folders')
                ? { status: 200, body: JSON.stringify(mailFolders), headers: {} }
                : { status: draft.status, body: JSON.stringify(draft.body), headers: {} },
        );
}

describe('showDraftsFolder', () => {
    it('points the list at the Drafts folder of the account the draft belongs to', async () => {
        const revise = vi.fn();

        await showDraftsFolder(
            session,
            answering({ status: 200, body: drafts.outOfStepDraftReading }),
            revise,
            drafts.outOfStepDraft.draftId,
        );

        expect(revise).toHaveBeenCalledExactlyOnceWith({
            scope: { kind: 'folder', accountId: workAccount.id, folderId: 'DRAFTS' },
            outbox: null,
            selection: null,
            conversation: null,
            selected: [],
        });
    });

    it('leaves the list where it was when the draft is no longer kept', async () => {
        const revise = vi.fn();

        await showDraftsFolder(session, answering({ status: 404, body: '' }), revise, drafts.outOfStepDraft.draftId);

        expect(revise).not.toHaveBeenCalled();
    });
});
