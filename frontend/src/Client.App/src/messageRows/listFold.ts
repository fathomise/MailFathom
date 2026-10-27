// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';

// Whether the message list stands folded to a strip of senders, which only the fold does: two panes under a finger in
// less width than the tablet, with something open beside the list. Carried through context because the list is a
// region handed to the Mail space already built, so its rows learn how they are drawn from here rather than from a
// prop; the control that turns it stands in the toolbar, which the space hands the same value.

/** The fold's list, folded or widened, whether it is kept open as other messages are opened, and the two acts on it. */
export interface ListFold {
    readonly folded: boolean;
    readonly kept: boolean;

    /** Widens a folded list, or narrows a widened one and lets go of keeping it open. */
    readonly turn: () => void;

    /** Keeps a widened list open as other messages are opened, or lets it fold again at the next one. */
    readonly keep: () => void;
}

export const ListFoldContext = createContext<ListFold | null>(null);

/** The list's fold, or `null` where the composition draws the list whole and offers nothing to fold it with. */
export function useListFold(): ListFold | null {
    return useContext(ListFoldContext);
}
