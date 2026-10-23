// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailAccount } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { isCurrent, synchronizationStateLabel } from '../synchronization/synchronizationState';

// One account, and how current the deployment's copy of it is: its name and its state, and nothing more, because the
// row is read only once something needs a look and the state is what says which account it is.

export function AccountLine({ account }: { readonly account: MailAccount }) {
    const { translate } = useLocalization();
    const current = isCurrent(account.synchronizationState, account.behind);

    return (
        <li className="flex flex-wrap items-center gap-x-2.25 gap-y-1">
            <span
                aria-hidden="true"
                className={`size-1.75 shrink-0 rounded-full ${current ? 'bg-healthy' : 'bg-warning'}`}
            />
            <span className="font-medium text-text">{account.displayName}</span>
            <span className="ms-auto text-muted">
                {translate(synchronizationStateLabel(account.synchronizationState, account.behind))}
            </span>
        </li>
    );
}
