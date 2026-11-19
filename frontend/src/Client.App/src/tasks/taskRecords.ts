// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { longestAnchorLabel, longestTaskTitle, type PersonalTaskRecord } from '@mailfathom/client-backend';
import type { TaskDraft } from './NewTask';

/**
 * Words cut to what the deployment keeps, so a long subject or title is written short rather than refused. A cut that
 * would split a pair of surrogates drops its first half, which is no character on its own.
 */
function cutTo(words: string, longest: number): string {
    const cut = words.slice(0, longest);

    return /[\uD800-\uDBFF]$/u.test(cut) ? cut.slice(0, -1) : cut;
}

/**
 * The words an anchor is drawn with: what the thread or the event is called, or `blank` where that is nothing, since
 * the deployment keeps no anchor without a label — a reply to a message with no subject still waits in its thread.
 */
export function anchorLabel(words: string | null, blank: string): string {
    const said = words?.trim() ?? '';

    return cutTo(said === '' ? blank : said, longestAnchorLabel);
}

/** A task's line proposed from something longer than a line may be, cut to the longest the deployment keeps. */
export function taskLine(words: string): string {
    return cutTo(words.trim(), longestTaskTitle);
}

/**
 * The record a task written down in the new-task dialog is sent as, wherever the dialog was opened.
 *
 * It announces nothing, which is what the design's own *New task* offers: what announces a task is set once it exists,
 * a day being what a lead is measured back from. It cites no message and is planned nowhere yet.
 */
export function recordOfDraft(draft: TaskDraft): PersonalTaskRecord {
    return {
        title: draft.title,
        dueOn: draft.dueOn === '' ? null : draft.dueOn,
        reminders: [],
        dueDayOffsetMinutes: null,
        sourceMessageId: null,
        direction: draft.direction,
        estimatedMinutes: draft.estimatedMinutes,
        plan: null,
        anchor: draft.anchor,
        counterpart: draft.counterpart,
    };
}
