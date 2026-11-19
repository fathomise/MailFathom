// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { PersonalTaskRecord } from '@mailfathom/client-backend';
import type { TaskDraft } from './NewTask';

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
