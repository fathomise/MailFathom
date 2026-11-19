// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { TaskCounterpart, TaskPlan } from '@mailfathom/client-backend';
import {
    calendarDayIn,
    dayFrom,
    wordDueDay,
    wordInstant,
    wordInstantRange,
    wordLasting,
} from '../localization/instants';
import type { Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { endAfter } from './dayInstants';

// What a task's commitment is called wherever it is drawn: the row, the dialog, and — once they draw tasks — the
// calendar, a thread and a person's page. Every one of them words the same plan, day and person, so the wording is
// here once rather than in each, and two of them cannot come to disagree about when a task is planned.

/**
 * The day a task is due or followed up on, the short way the design writes it on a row: *today*, *tomorrow*, or the
 * day and the month.
 */
export function wordTaskDay(day: string, locale: Locale, now: number, timeZone: string | null): string {
    return day === dayFrom(calendarDayIn(now, timeZone), 1)
        ? new Intl.RelativeTimeFormat(locale, { numeric: 'auto' }).format(1, 'day')
        : wordDueDay(day, locale, now, timeZone);
}

/**
 * The stretch a task is planned into, as the run between its two clock times, with the day beside it where that day
 * is not the reader's today.
 *
 * @returns The wording, or `null` where the plan does not name an instant this client can read.
 */
export function wordPlan(
    plan: TaskPlan,
    locale: Locale,
    now: number,
    timeZone: string | null,
    translate: Translate,
): string | null {
    const at = Date.parse(plan.startAt);
    const time = wordInstantRange(plan.startAt, endAfter(plan.startAt, plan.minutes), locale, 'time', timeZone);

    if (time === null || Number.isNaN(at)) {
        return null;
    }

    const day = calendarDayIn(at, timeZone);

    return day === calendarDayIn(now, timeZone)
        ? time
        : translate('tasks.planOnDay', { time, day: wordTaskDay(day, locale, now, timeZone) });
}

/** How long somebody thinks a task takes, the short way: *45 min*, *2 hr*. */
export function wordEstimate(minutes: number, locale: Locale): string {
    return wordLasting({ unit: 'minute', count: minutes }, locale, 'short');
}

/** The person on the other side of a task, by the name they are known by, or by their address where none is held. */
export function wordCounterpart(counterpart: TaskCounterpart): string {
    return counterpart.displayName ?? counterpart.address;
}

/**
 * What closed a waiting task: who replied, and when.
 *
 * @param arrivedAt When the reply arrived, as the deployment stated it.
 * @param counterpart Who the task was waiting on, or `null` where it names nobody.
 */
export function wordReply(
    arrivedAt: string,
    counterpart: TaskCounterpart | null,
    locale: Locale,
    timeZone: string | null,
    translate: Translate,
): string {
    const time = wordInstant(arrivedAt, locale, 'time', timeZone) ?? '';

    return counterpart === null
        ? translate('tasks.repliedAt', { time })
        : translate('tasks.personRepliedAt', { name: wordCounterpart(counterpart), time });
}
