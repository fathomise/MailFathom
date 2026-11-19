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
import type { MessageKey } from '../localization/en';
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

// How long a task takes as somebody types it — *30*, *30 min*, *1 hr*, *1h 30*, *2 hrs* — in either language the
// client reads in. A bare number is minutes, and a number after hours is the minutes past them.
const typedLength =
    /^(?:(\d+)\s*(?:h|hr|hrs|hour|hours|godz|godzina|godziny|godzin)\.?\s*)?(?:(\d+)\s*(?:m|min|mins|minute|minutes|minuta|minuty|minut)?\.?)?$/iu;

/**
 * How long somebody typed that a task takes, the inverse of {@link wordEstimate}.
 *
 * @returns The minutes, `null` where nothing was typed, or `undefined` where what was typed is not a length at all.
 */
export function readEstimate(typed: string): number | null | undefined {
    const text = typed.trim();

    if (text === '') {
        return null;
    }

    const read = typedLength.exec(text);
    const [, hours, minutes] = read ?? [];

    if (read === null || (hours === undefined && minutes === undefined)) {
        return undefined;
    }

    return Number(hours ?? 0) * 60 + Number(minutes ?? 0);
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

/**
 * The working days after tomorrow, as many as asked for, which the design offers as day chips beside *Today* and
 * *Tomorrow* — walked over the calendar, so a weekend is stepped over rather than counted.
 */
export function workingDaysAfterTomorrow(today: string, count: number): readonly string[] {
    const later: string[] = [];

    for (let ahead = 2; later.length < count; ahead += 1) {
        const day = dayFrom(today, ahead);
        const weekday = new Date(`${day}T00:00:00Z`).getUTCDay();

        if (weekday !== 0 && weekday !== 6) {
            later.push(day);
        }
    }

    return later;
}

/** What a day chip says: *No day*, *Today*, *Tomorrow*, or the weekday with the day and the month. */
export function wordDayChoice(
    day: string | null,
    today: string,
    locale: Locale,
    translate: (key: MessageKey) => string,
): string {
    if (day === null) {
        return translate('tasks.noDay');
    }

    if (day === today) {
        return translate('tasks.dayToday');
    }

    return day === dayFrom(today, 1)
        ? translate('tasks.dayTomorrow')
        : new Intl.DateTimeFormat(locale, { weekday: 'short', day: '2-digit', month: '2-digit' }).format(
              new Date(`${day}T00:00:00`),
          );
}
