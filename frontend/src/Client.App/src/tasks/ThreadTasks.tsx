// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent } from 'react';
import { useLocalization } from '../localization/useLocalization';
import { TaskChips } from './TaskChips';
import { useMailTasks, type MailTasks } from './useMailTasks';
import { useTasksWhere } from './useTasksWhere';

// The commitments a thread carries, drawn under the head's address line as the design's `forThread` does: only the
// open ones, each opening its task. Nothing is drawn while none is open, where the person keeps no tasks, or while
// they are being read, so a thread carrying none keeps the head it always had.

export function ThreadTasks({
    threadId,
    compact,
}: {
    readonly threadId: string | null;

    /** Whether the head is one pane's compact bar, whose lines stand inset under the way back. */
    readonly compact: boolean;
}) {
    const tasks = useMailTasks();

    return tasks === null || threadId === null ? null : (
        <OpenInThread tasks={tasks} threadId={threadId} compact={compact} />
    );
}

function OpenInThread({
    tasks,
    threadId,
    compact,
}: {
    readonly tasks: MailTasks;
    readonly threadId: string;
    readonly compact: boolean;
}) {
    const { translate } = useLocalization();
    const where = useTasksWhere(tasks.session, tasks.transport, { threadId });
    const open = where.tasks.filter((task) => !task.completed);
    const readAgain = useEffectEvent(() => where.readAgain());
    const { follow } = tasks;

    // Read again after a task is written down from here or opened and changed, which is a subscription to the writes.
    useEffect(() => follow(() => readAgain()), [follow]);

    if (where.failure !== null && open.length === 0) {
        return <p className={`text-sm text-muted ${compact ? 'ps-2' : ''}`}>{translate('tasks.threadTasksUnread')}</p>;
    }

    return open.length === 0 ? null : (
        <div className={compact ? 'ps-2' : ''}>
            <TaskChips
                tasks={open}
                onOpen={(taskId) => {
                    const task = open.find((held) => held.id === taskId);

                    if (task !== undefined) {
                        tasks.openTask(task);
                    }
                }}
            />
        </div>
    );
}
