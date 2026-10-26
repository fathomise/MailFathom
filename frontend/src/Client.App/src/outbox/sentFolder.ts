// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import {
    readMailFolders,
    type ClientSession,
    type MailFathomTransport,
    type MailFolderDirectory,
    type MailFolderRole,
} from '@mailfathom/client-backend';
import type { MailScope } from '../workspace/mailScope';

// An Outbox opens on its account's Sent folder, which is where whatever closes it leaves the reader. The mark on the Sent
// row already knows that folder; a send toast does not, so it asks the folders route which folder that is.

/** The account's own Sent folder as a scope, or `null` where the account has none or is not in the directory. */
export function sentFolderOf(directory: MailFolderDirectory, account: string): MailScope | null {
    return folderWithRole(directory, account, 'Sent');
}

/** Reads which folder is the account's Sent folder, answering `null` where the deployment did not say. */
export function readSentFolder(
    session: ClientSession,
    transport: MailFathomTransport,
    account: string,
): Promise<MailScope | null> {
    return readFolderWithRole(session, transport, account, 'Sent');
}

/** Reads which of the account's folders has the role named, answering `null` where the deployment did not say. */
export async function readFolderWithRole(
    session: ClientSession,
    transport: MailFathomTransport,
    account: string,
    role: MailFolderRole,
): Promise<MailScope | null> {
    const answered = await readMailFolders(session, transport);

    return answered.outcome === 'read' ? folderWithRole(answered.value, account, role) : null;
}

function folderWithRole(directory: MailFolderDirectory, account: string, role: MailFolderRole): MailScope | null {
    const folder = directory.accounts
        .find((entry) => entry.account.id === account)
        ?.folders.find((candidate) => candidate.role === role);

    return folder === undefined ? null : { kind: 'folder', accountId: account, folderId: folder.id };
}
