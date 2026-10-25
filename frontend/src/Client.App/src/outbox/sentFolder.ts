// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import {
    readMailFolders,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderDirectory,
} from '@mailfathom/client-backend';
import type { MailScope } from '../workspace/mailScope';

// An Outbox opens on its account's Sent folder, which is where whatever closes it leaves the reader. The mark on the Sent
// row already knows that folder; a send toast does not, so it asks the folders route which folder that is.

/** The account's own Sent folder as a scope, or `null` where the account has none or is not in the directory. */
export function sentFolderOf(directory: MailFolderDirectory, account: string): MailScope | null {
    const folder = directory.accounts
        .find((entry) => entry.account.id === account)
        ?.folders.find((candidate) => candidate.role === 'Sent');

    return folder === undefined ? null : { kind: 'folder', accountId: account, folderId: folder.id };
}

/** Reads which folder is the account's Sent folder, answering `null` where the deployment did not say. */
export async function readSentFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    account: string,
): Promise<MailScope | null> {
    const answered = await readMailFolders(session, transport);

    return answered.outcome === 'read' ? sentFolderOf(answered.value, account) : null;
}
