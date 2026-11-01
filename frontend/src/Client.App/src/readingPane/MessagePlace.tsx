// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailMessage } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useMailboxActs } from '../mailboxActs/useMailboxActs';
import { useAccountName } from '../shell/accountNames';
import { folderRoleLabels } from '../workspace/mailScope';

// Where an open message is kept, said at its foot: the folder and the name of the account that hold it. Read by
// conversation a thread gathers its messages from every folder of an account, and read by message a conversation does
// the same, so each message says where it is rather than leaving the list it was opened from to imply it.
//
// It names what the folder and account are called, never what they are identified by, and says only as much as this
// client knows: a folder the tree has not been read for is left out, and the line goes where neither name is known.

export function MessagePlace({ message }: { readonly message: MailMessage }) {
    const { translate } = useLocalization();
    const acts = useMailboxActs();
    const accountName = useAccountName(message.account);
    const folder = acts.folderOf({
        storedEmailId: message.storedEmailId,
        account: message.account,
        folder: message.folder,
        unread: message.unread,
        flagged: message.flagged,
        spam: message.spam,
    });
    const folderName =
        folder === null
            ? null
            : folder.role === null
              ? (folder.path.at(-1) ?? null)
              : translate(folderRoleLabels[folder.role]);

    const said =
        folderName === null
            ? accountName
            : accountName === null
              ? folderName
              : translate('message.place', { folder: folderName, account: accountName });

    return said === null ? null : (
        <p data-message-place className="-mt-1 flex min-w-0 items-center gap-1.5 text-sm text-faint">
            <Icon name="folder_open" className="size-icon-sm shrink-0" />
            <span className="truncate">{said}</span>
        </p>
    );
}
