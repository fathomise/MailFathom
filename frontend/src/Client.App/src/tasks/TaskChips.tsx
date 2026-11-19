// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, useState } from 'react';
import type { PersonalTask } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { calendarDayIn } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { wordPlan, wordReply, wordTaskDay } from './taskWording';
import { doneTaskFade } from './taskShapes';

// The tasks one thing carries, drawn where that thing is read — a thread, an event, a person — each as its line, what
// it waits on or when it is due, and the way into it. A wait carries an hourglass and the reader's own work a box, so
// the two directions stay apart at a glance. Past the first few the rest are counted quietly rather than drawn, and the
// count opens them, so a thread carrying a dozen tasks does not push the message it heads off the screen. A person's
// card is the exception and draws every one, as the design does.
//
// A task done is still drawn, faded, where the caller hands one over: an event's tasks are what was prepared for it,
// done or not, while a thread hands over only what is open.

// How many chips stand before the rest are counted.
const chipsShown = 4;

export function TaskChips({
    tasks,
    onOpen,
    stacked = false,
    whole = false,
    labelledBy,
}: {
    readonly tasks: readonly PersonalTask[];

    /** Opens one task, by its identity. */
    readonly onOpen: (taskId: string) => void;

    /** Whether each stands on a line of its own, which the event dialog draws, rather than wrapping as chips. */
    readonly stacked?: boolean;

    /** Whether every task is drawn rather than the first few and a count, as a person's card draws them. */
    readonly whole?: boolean;

    /** The element naming the list where it stands beside another, as a person's two sides do. */
    readonly labelledBy?: string;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());
    const [all, setAll] = useState(false);

    const shown = all || whole ? tasks : tasks.slice(0, chipsShown);
    const counted = tasks.length - shown.length;
    const countWords = new Intl.NumberFormat(locale).format(counted);
    const more = translate('tasks.moreTasks', { count: countWords });
    const moreSaid = useId();
    const today = calendarDayIn(now, timeZone);

    // When the task stands: who replied, when to follow a wait up, where it is planned, when it is due, or no day.
    function whenOf(task: PersonalTask): string {
        if (task.answer !== null) {
            return wordReply(task.answer.arrivedAt, task.counterpart, locale, timeZone, translate);
        }

        if (task.direction === 'Awaited') {
            return task.dueOn === null
                ? translate('tasks.chipNoDay')
                : task.dueOn <= today
                  ? translate('tasks.followUpToday')
                  : translate('tasks.followUpOn', { day: wordTaskDay(task.dueOn, locale, now, timeZone) });
        }

        const plan = task.plan === null ? null : wordPlan(task.plan, locale, now, timeZone, translate);

        return (
            plan ??
            (task.dueOn === null
                ? translate('tasks.chipNoDay')
                : translate('tasks.chipDue', { day: wordTaskDay(task.dueOn, locale, now, timeZone) }))
        );
    }

    return (
        <ul aria-labelledby={labelledBy} className={stacked ? 'flex flex-col gap-0.5' : 'flex flex-wrap gap-1.5'}>
            {shown.map((task) => {
                const awaited = task.direction === 'Awaited';

                return (
                    <li key={task.id} className={stacked ? 'min-w-0' : 'min-w-0 max-w-full'}>
                        <button
                            type="button"
                            className={`flex min-w-0 max-w-full items-center gap-2 text-start ${
                                stacked
                                    ? 'w-full rounded-md px-2 py-1.25 hover:bg-hover'
                                    : 'rounded-full border border-line bg-sunken px-2.75 py-1 text-sm text-text-soft hover:border-accent'
                            } ${task.completed ? doneTaskFade : ''}`}
                            onClick={() => {
                                onOpen(task.id);
                            }}
                        >
                            <Icon
                                name={awaited ? 'hourglass_top' : 'check_box_outline_blank'}
                                className={`size-icon-sm shrink-0 ${awaited ? 'text-warning' : 'text-accent-deep'}`}
                            />
                            <span className={`min-w-0 truncate ${stacked ? 'flex-1 text-base' : ''}`}>
                                {task.title}
                            </span>
                            <span className="shrink-0 text-xs text-muted">{whenOf(task)}</span>
                        </button>
                    </li>
                );
            })}

            {counted === 0 ? null : (
                <li>
                    {/* Named by what it shows, so somebody speaking the words they see reaches it, and described by
                        what pressing it does. */}
                    <button
                        type="button"
                        aria-describedby={moreSaid}
                        className="rounded-full px-2.25 py-1 text-sm text-muted hover:bg-hover"
                        onClick={() => {
                            setAll(true);
                        }}
                    >
                        {more}
                    </button>
                    <span id={moreSaid} hidden>
                        {translate('tasks.moreTasksNamed', { count: countWords })}
                    </span>
                </li>
            )}
        </ul>
    );
}
