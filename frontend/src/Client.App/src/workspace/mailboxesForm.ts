// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCoarsePointer, useDesktopComposition, useTwoPanes } from '../shell/useWideWorkspace';
import { useWorkspace } from './useWorkspace';

// The three forms the mailbox column takes, and each question about it reads one axis. The width says whether it
// stands at all: as a column from the desktop composition, folded below it, and in a drawer wherever one pane stands,
// where the list or the message already takes the whole width and a strip beside it would leave neither. The pointer says what *folded* is between the two: a drawer under a finger, which has no hover to
// tell a strip of symbols apart by, and the strip of symbols under a mouse, which the toolbar's control opens to the
// column again. Where nobody has folded or opened it, the strip is what stands below the desktop composition and the
// column what stands in it.
//
// Here rather than in the Mail space because two things read it — the space, which lays the column out, and the tree
// inside it, which draws a row as a name or as a symbol — and the tree is a region the space was handed already built.

export type MailboxesForm = 'column' | 'strip' | 'drawer';

/** The form the mailbox column takes in this window, under this pointer, as this person last left it. */
export function useMailboxesForm(): MailboxesForm {
    const { workspace } = useWorkspace();
    const twoPanes = useTwoPanes();
    const desktop = useDesktopComposition();
    const coarse = useCoarsePointer();

    if (!twoPanes || (!desktop && coarse)) {
        return 'drawer';
    }

    return (workspace.mailboxesFolded ?? !desktop) ? 'strip' : 'column';
}
