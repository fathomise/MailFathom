// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useLocalization } from '../localization/useLocalization';

// The chip a shared mailbox carries beside its name, which the design draws wherever an account is drawn or chosen: its
// group in the folder tree, the account menu's mailboxes, and the groups of the move dialog. One shape stated once, for
// the reason `MailboxMark` is: three similar arrangements would be three marks as far as a reader is concerned.
//
// Its word stays in the accessibility tree, so the row it sits in is announced as a shared mailbox rather than drawn as
// one only for a reader who can see it.

export function SharedMark() {
    const { translate } = useLocalization();

    return (
        <span className="inline-flex shrink-0 items-center rounded-xs border border-line-strong px-1.25 text-xs leading-3.75 font-semibold tracking-wide text-muted">
            {translate('shell.shared')}
        </span>
    );
}
