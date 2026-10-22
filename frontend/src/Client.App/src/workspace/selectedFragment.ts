// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { Workspace } from './useWorkspace';

/** A passage of one message somebody selected, which the field under a correspondence quotes back before it asks. */
export interface SelectedFragment {
    /** The message the words were selected in, which is what makes the passage distinguishable from the message. */
    readonly messageId: string;

    /** The words themselves, because what the field does with a fragment is quote it back before the question is sent. */
    readonly text: string;
}

// A passage counts while the message it was taken from is one of the messages being read: the message opened on its
// own, or any of the ones a correspondence draws, which the workspace names collectively rather than one at a time.
// Reading it this way rather than clearing the value from each screen that closes is what keeps a passage from
// outliving its words — a question asked about a paragraph nobody can see any more is a disclosure.
export function fragmentBeingRead(workspace: Workspace): SelectedFragment | null {
    if (workspace.fragment === null) {
        return null;
    }

    return workspace.conversation !== null || workspace.selection === workspace.fragment.messageId
        ? workspace.fragment
        : null;
}
