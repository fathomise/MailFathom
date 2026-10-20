// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';

// Whether the message list stands folded to a strip of senders, which only the fold does: two panes under a finger in
// less width than the tablet, with something open beside the list. Carried through context for the reason the drawer's
// opener is — the list is a region handed to the Mail space already built, so its head and its rows learn how they are
// drawn from here rather than from a prop.

/** The fold's list, folded or widened, and the one control that turns it the other way. */
export interface ListFold {
    readonly folded: boolean;
    readonly turn: () => void;
}

export const ListFoldContext = createContext<ListFold | null>(null);

/** The list's fold, or `null` where the composition draws the list whole and offers nothing to fold it with. */
export function useListFold(): ListFold | null {
    return useContext(ListFoldContext);
}
