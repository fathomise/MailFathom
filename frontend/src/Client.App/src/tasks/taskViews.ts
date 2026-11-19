// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { CalendarEvent, PersonalTask } from '@mailfathom/client-backend';
import { calendarDayIn, dayFrom } from '../localization/instants';

// The four views the design draws the Tasks space as, and which group of which view a task stands in. It is a
// rendering decision and therefore the application's: the deployment answers two ordered walks, and which heading a
// task falls under is what a reader is shown rather than something the service has an opinion about.
//
// **A day is read against the reader's own day.** A due date is a calendar day somebody picked and is compared as
// text; a plan is an instant, and the day it falls on is read in the zone the reader's record states — the same zone
// every wording on the row is placed in, so a row planned *today* never stands under *Tomorrow*.
//
// **Done is what the deployment says it is.** A reply that answers a waiting task completes it, so a task closed by a
// reply is a completed one that also names the reply — which is why *Closed by a reply* stands in *Waiting for* even
// while done work is hidden, and *Received* holds only what the person marked themselves.

/** The four views, in the order the design draws them. */
export const taskViews = ['today', 'upcoming', 'waiting', 'proposed'] as const;

export type TaskView = (typeof taskViews)[number];

/** Which heading a task stands under, named as the design names it. A name is unique within one view. */
export type TaskGroupName =
    | 'planned'
    | 'dueNotPlanned'
    | 'followUp'
    | 'tomorrow'
    | 'thisWeek'
    | 'later'
    | 'noDay'
    | 'followUpToday'
    | 'waiting'
    | 'closedByReply'
    | 'received'
    | 'today';

/** One heading and the tasks under it. */
export interface TaskGroup {
    readonly name: TaskGroupName;
    readonly tasks: readonly PersonalTask[];
}

// How far past today *This week* reaches: the six days after tomorrow's, so the heading is a rolling week rather than
// the remainder of a calendar one.
const daysInTheWeekAhead = 6;

/** The day a task is planned into, in the reader's zone, or `null` where it is not planned. */
export function plannedDay(task: PersonalTask, timeZone: string | null): string | null {
    if (task.plan === null) {
        return null;
    }

    const at = Date.parse(task.plan.startAt);

    return Number.isNaN(at) ? null : calendarDayIn(at, timeZone);
}

// What one reading of the list needs to place a task, worked out once rather than per task.
interface Placing {
    readonly today: string;
    readonly tomorrow: string;
    readonly weekAhead: string;
    readonly timeZone: string | null;
}

const owed = (task: PersonalTask): boolean => task.direction === 'Owed';
const awaited = (task: PersonalTask): boolean => task.direction === 'Awaited';
const dueBy = (task: PersonalTask, day: string): boolean => task.dueOn !== null && task.dueOn <= day;

function plannedToday(task: PersonalTask, placing: Placing): boolean {
    return plannedDay(task, placing.timeZone) === placing.today;
}

// Whether something owed belongs to today at all: due by today, or planned into it whatever day it is due.
function owedToday(task: PersonalTask, placing: Placing): boolean {
    return dueBy(task, placing.today) || plannedToday(task, placing);
}

function placingFor(today: string, timeZone: string | null): Placing {
    return {
        today,
        tomorrow: dayFrom(today, 1),
        weekAhead: dayFrom(today, daysInTheWeekAhead),
        timeZone,
    };
}

function byPlannedStart(left: PersonalTask, right: PersonalTask): number {
    return (left.plan?.startAt ?? '').localeCompare(right.plan?.startAt ?? '');
}

/**
 * The groups one view draws, with a heading nothing falls under left out.
 *
 * @param view Which of the four views is drawn.
 * @param tasks What the person committed to, in the order the deployment walked it.
 * @param proposals What was proposed and nobody has answered, in the order the deployment walked it.
 * @param today The reader's own day, as `yyyy-mm-dd`.
 * @param timeZone The zone the reader's record states, or `null` for the runtime's own.
 * @param showDone Whether work the person finished is drawn too.
 * @returns The headings that have something under them, in the order the design draws them.
 */
export function tasksIn(
    view: TaskView,
    tasks: readonly PersonalTask[],
    proposals: readonly PersonalTask[],
    today: string,
    timeZone: string | null = null,
    showDone = false,
): readonly TaskGroup[] {
    const placing = placingFor(today, timeZone);
    const shown = (task: PersonalTask): boolean => showDone || !task.completed;
    const owedShown = tasks.filter((task) => owed(task) && shown(task));
    const waitingOpen = tasks.filter((task) => awaited(task) && !task.completed);
    const later = owedShown.filter((task) => !owedToday(task, placing));

    const groups: Readonly<Record<TaskView, readonly TaskGroup[]>> = {
        today: [
            {
                name: 'planned',
                tasks: owedShown.filter((task) => plannedToday(task, placing)).sort(byPlannedStart),
            },
            {
                name: 'dueNotPlanned',
                tasks: owedShown.filter((task) => dueBy(task, today) && !plannedToday(task, placing)),
            },
            { name: 'followUp', tasks: waitingOpen.filter((task) => dueBy(task, today)) },
        ],
        upcoming: [
            {
                name: 'tomorrow',
                tasks: later.filter(
                    (task) =>
                        task.dueOn === placing.tomorrow ||
                        (task.dueOn === null && plannedDay(task, timeZone) === placing.tomorrow),
                ),
            },
            {
                name: 'thisWeek',
                tasks: later.filter(
                    (task) => task.dueOn !== null && task.dueOn > placing.tomorrow && task.dueOn <= placing.weekAhead,
                ),
            },
            { name: 'later', tasks: later.filter((task) => task.dueOn !== null && task.dueOn > placing.weekAhead) },
            {
                name: 'noDay',
                tasks: later.filter((task) => task.dueOn === null && plannedDay(task, timeZone) !== placing.tomorrow),
            },
        ],
        waiting: [
            { name: 'followUpToday', tasks: waitingOpen.filter((task) => dueBy(task, today)) },
            { name: 'waiting', tasks: waitingOpen.filter((task) => !dueBy(task, today)) },
            { name: 'closedByReply', tasks: tasks.filter((task) => awaited(task) && task.answer !== null) },
            {
                name: 'received',
                tasks: showDone ? tasks.filter((task) => awaited(task) && task.completed && task.answer === null) : [],
            },
        ],
        proposed: [
            { name: 'today', tasks: proposals.filter((task) => dueBy(task, today)) },
            { name: 'later', tasks: proposals.filter((task) => task.dueOn !== null && task.dueOn > today) },
            { name: 'noDay', tasks: proposals.filter((task) => task.dueOn === null) },
        ],
    };

    return groups[view].filter((group) => group.tasks.length > 0);
}

/**
 * How much each view holds that is still open, which is the count the design draws on each view's control.
 *
 * @returns The open count for each of the four views.
 */
export function openIn(
    tasks: readonly PersonalTask[],
    proposals: readonly PersonalTask[],
    today: string,
    timeZone: string | null = null,
): Readonly<Record<TaskView, number>> {
    const placing = placingFor(today, timeZone);
    const open = tasks.filter((task) => !task.completed);
    const owedOpen = open.filter(owed);
    const waitingOpen = open.filter(awaited);
    const owedOpenToday = owedOpen.filter((task) => owedToday(task, placing)).length;

    return {
        today: owedOpenToday + waitingOpen.filter((task) => dueBy(task, today)).length,
        upcoming: owedOpen.length - owedOpenToday,
        waiting: waitingOpen.length,
        proposed: proposals.length,
    };
}

/**
 * What is owed today and still open, which is what the day's capacity counts: what is due by today and what is
 * planned into it.
 */
export function openOwedToday(
    tasks: readonly PersonalTask[],
    today: string,
    timeZone: string | null,
): readonly PersonalTask[] {
    const placing = placingFor(today, timeZone);

    return tasks.filter((task) => owed(task) && !task.completed && owedToday(task, placing));
}

/**
 * The first timed event a task's planned stretch overlaps, which the row, the dialog and the day beside the list each
 * name as a clash. A day-long event claims no hours, so nothing clashes with one, and done work clashes with nothing.
 *
 * @param task The task.
 * @param events The events of the day the task is planned into, as the calendar answered them.
 * @returns The event it clashes with, or `null` where it clashes with none.
 */
export function clashOf(task: PersonalTask, events: readonly CalendarEvent[]): CalendarEvent | null {
    if (task.plan === null || task.completed) {
        return null;
    }

    const starts = Date.parse(task.plan.startAt);
    const ends = starts + task.plan.minutes * 60_000;

    return (
        events.find((event) => {
            const from = Date.parse(event.start);
            const until = event.end === null ? from : Date.parse(event.end);

            return !event.isAllDay && starts < until && from < ends;
        }) ?? null
    );
}
