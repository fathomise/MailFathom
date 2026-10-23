// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type { MailAccount } from '@mailfathom/client-backend';

// An account reaches most routes as its identifier, and an identifier is how the deployment is asked rather than
// anything a reader knows a mailbox by. So whatever names an account on the screen names it through here, and an
// account the client holds no name for — the account list not having answered yet, say — is left unnamed rather than
// shown as the identifier it arrived as.
//
// A context because the lines naming an account sit several components below the frame holding the list the
// connection read — a conversation's messages, and the evidence inspector's source line — and none of the components
// between them reads it. It is provided around each region that names one rather than around the whole frame, as the
// reply drafting context is. The empty list is what stands outside those regions and in every test naming no account,
// and it leaves every account unnamed.

export const MailAccountsContext = createContext<readonly MailAccount[]>([]);

/** The name a reader knows the account by, or `null` where none of these accounts carries the identifier. */
export function accountNameIn(accounts: readonly MailAccount[], accountId: string): string | null {
    return accounts.find((account) => account.id === accountId)?.displayName ?? null;
}

/** The name a reader knows the account by, or `null` where the client holds no account with the identifier. */
export function useAccountName(accountId: string): string | null {
    return accountNameIn(useContext(MailAccountsContext), accountId);
}
