// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { mostDraftContextMessages } from '@mailfathom/client-backend';

// What the composer's AI drafts from while the list stands beside it, as the design decides it: a plain click on a row
// makes that message the context, and a selection makes the whole selection the context. Emptying a selection is not
// pointing at anything, so it leaves the context where it stood — which is why this answers "nothing changed" as well
// as a set of messages.

/** What the list points at: the message open, and the messages picked out. */
export interface ListPointer {
    readonly selection: string | null;
    readonly selected: readonly string[];
}

/**
 * The context a move of the list's pointer makes, or `null` where the move points at nothing new.
 *
 * A selection outranks the open message, because a click that adds a row to a selection may open it as well, and what
 * somebody building a selection means is the selection.
 *
 * @param before What the list pointed at when the composer last looked.
 * @param now What it points at now.
 */
export function contextPointedAt(before: ListPointer, now: ListPointer): readonly string[] | null {
    if (now.selected !== before.selected && now.selected.length > 0) {
        return now.selected.slice(0, mostDraftContextMessages);
    }

    if (now.selection !== before.selection && now.selection !== null) {
        return [now.selection];
    }

    return null;
}

/** Whether two contexts hold the same messages, whatever order they were taken in. */
export function sameMessages(one: readonly string[], other: readonly string[]): boolean {
    return one.length === other.length && one.every((message) => other.includes(message));
}
