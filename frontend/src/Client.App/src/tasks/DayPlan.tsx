// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, useState, type DragEvent } from 'react';
import type { ClientFailureReason, PersonalTask } from '@mailfathom/client-backend';
import { instantOf } from '../composer/sendLater';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { calendarDayIn, wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { addressOf } from '../routing/spaces';
import { carriedTaskType, dayHours, hourOf } from './dayHours';
import { HourItem } from './HourItem';
import { clashOf } from './taskViews';
import { wordPlan } from './taskWording';
import type { TodayCalendarInForce } from './useTodayCalendar';

// Today beside the list, hour by hour from eight to five: the meetings the calendar holds and the tasks planned into
// the day, each at the hour it begins. Every hour is a place to drop a task row on, which plans the task there — and
// the hour under the drag is outlined, which is the one thing saying a drop lands here. A finger drags nothing, so under
// one the line above the hours says to plan a task from its own *Plan* instead.
//
// Where the day stands above the list rather than beside it, only the hours that hold something are drawn: an empty
// hour there is room taken from the list for a drop no finger can make.
//
// Every failure says what it is and offers the way out, which here is reading the day again.

const dayFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'tasks.dayFailedUnauthenticated',
    unauthorized: 'tasks.dayFailedUnauthorized',
    unavailable: 'tasks.dayFailedUnavailable',
    unreadable: 'tasks.dayFailedUnreadable',
    missing: 'tasks.dayFailedUnavailable',
};

function carriesTask(event: DragEvent): boolean {
    return Array.from(event.dataTransfer.types).includes(carriedTaskType);
}

export function DayPlan({
    now,
    day,
    tasks,
    onDrop,
    onOpenTask,
    onOpenEvent,
}: {
    /** The reader's own clock, which decides which day is today. */
    readonly now: number;

    /** What the calendar holds today, and the way to read it again. */
    readonly day: TodayCalendarInForce;

    /** The tasks the screen holds, which the hours draw where one is planned into today. */
    readonly tasks: readonly PersonalTask[];

    /** Plans the task dropped on an hour from the instant that hour begins. */
    readonly onDrop: (taskId: string, startAt: string) => void;

    /** Opens a planned task in its dialog. */
    readonly onOpenTask: (task: PersonalTask) => void;

    /** Opens a meeting, or `undefined` where the frame offers no way to open one. */
    readonly onOpenEvent?: ((eventId: string) => void) | undefined;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [dropAt, setDropAt] = useState<number | null>(null);
    const heading = useId();
    const today = calendarDayIn(now, timeZone);
    const planned = tasks.filter((task) => task.plan !== null && !task.completed && task.direction === 'Owed');

    return (
        <section aria-labelledby={heading} className="flex flex-col gap-2.5">
            <div className="flex items-center gap-2.25">
                <h2 id={heading} className="text-xs font-semibold text-muted">
                    {translate('tasks.dayPlanHead', {
                        day: new Intl.DateTimeFormat(locale, { weekday: 'short', day: 'numeric' }).format(
                            new Date(`${today}T00:00:00`),
                        ),
                    })}
                </h2>

                <a
                    href={addressOf('calendar')}
                    className="ms-auto rounded-sm text-sm text-accent-deep transition hover:underline"
                >
                    {translate('tasks.openTheCalendar')}
                </a>
            </div>

            <p className="text-sm text-muted pointer-coarse:hidden">{translate('tasks.dragOntoAnHour')}</p>
            <p className="hidden text-sm text-muted pointer-coarse:block">{translate('tasks.planFromItsOwnPlan')}</p>

            {day.reading ? (
                <p role="status" className="text-sm text-muted">
                    {translate('tasks.dayReading')}
                </p>
            ) : null}

            {day.failure === null ? null : (
                <div className="flex flex-col items-start gap-2">
                    <p role="alert" className="text-sm text-warning text-pretty">
                        {translate(dayFailures[day.failure])}
                    </p>
                    <SecondaryButton label={translate('tasks.readAgain')} shape="compact" onActivate={day.readAgain} />
                </div>
            )}

            <ol className="flex flex-col">
                {dayHours.map((hour) => {
                    const time = `${String(hour).padStart(2, '0')}:00`;
                    const opens = instantOf({ date: today, time }, timeZone);
                    const startAt = opens === null ? null : new Date(opens).toISOString();
                    const meetings = day.events.filter(
                        (event) => !event.isAllDay && hourOf(event.start, timeZone, today) === hour,
                    );
                    const planning = planned.filter(
                        (task) => task.plan !== null && hourOf(task.plan.startAt, timeZone, today) === hour,
                    );
                    const free = meetings.length === 0 && planning.length === 0;

                    return (
                        <li
                            key={hour}
                            className={`min-h-7.5 gap-2.5 border-t border-line-soft py-1.25 pointer-coarse:min-h-11 ${
                                free ? 'hidden desktop:flex' : 'flex'
                            } ${dropAt === hour ? 'rounded-md bg-accent-soft ring-1 ring-accent ring-inset' : ''}`}
                            onDragEnter={(event) => {
                                if (carriesTask(event)) {
                                    event.preventDefault();
                                    setDropAt(hour);
                                }
                            }}
                            onDragOver={(event) => {
                                if (carriesTask(event)) {
                                    event.preventDefault();
                                    setDropAt(hour);
                                }
                            }}
                            onDragLeave={(event) => {
                                if (!(
                                    event.relatedTarget instanceof Node &&
                                    event.currentTarget.contains(event.relatedTarget)
                                )) {
                                    setDropAt((at) => (at === hour ? null : at));
                                }
                            }}
                            onDrop={(event) => {
                                setDropAt(null);

                                const taskId = carriesTask(event) ? event.dataTransfer.getData(carriedTaskType) : '';

                                if (taskId !== '' && startAt !== null) {
                                    event.preventDefault();
                                    onDrop(taskId, startAt);
                                }
                            }}
                        >
                            <span className="w-10 shrink-0 pt-1.5 text-xs text-faint">
                                {startAt === null ? '' : wordInstant(startAt, locale, 'time', timeZone)}
                            </span>
                            <span className="flex min-w-0 flex-1 flex-col gap-1">
                                {meetings.map((meeting) =>
                                    onOpenEvent === undefined ? (
                                        <HourItem
                                            key={meeting.id}
                                            glyph="groups"
                                            title={meeting.title}
                                            time={wordInstant(meeting.start, locale, 'time', timeZone) ?? ''}
                                            tone="meeting"
                                        />
                                    ) : (
                                        <button
                                            key={meeting.id}
                                            type="button"
                                            className="flex min-w-0 flex-col text-start"
                                            onClick={() => {
                                                onOpenEvent(meeting.id);
                                            }}
                                        >
                                            <HourItem
                                                glyph="groups"
                                                title={meeting.title}
                                                time={wordInstant(meeting.start, locale, 'time', timeZone) ?? ''}
                                                tone="meeting"
                                            />
                                        </button>
                                    ),
                                )}
                                {planning.map((task) => (
                                    <button
                                        key={task.id}
                                        type="button"
                                        className="flex min-w-0 flex-col text-start"
                                        onClick={() => {
                                            onOpenTask(task);
                                        }}
                                    >
                                        <HourItem
                                            glyph="check_box_outline_blank"
                                            title={task.title}
                                            time={
                                                task.plan === null
                                                    ? ''
                                                    : (wordPlan(task.plan, locale, now, timeZone, translate) ?? '')
                                            }
                                            tone={clashOf(task, day.events) === null ? 'task' : 'clash'}
                                        />
                                    </button>
                                ))}
                            </span>
                        </li>
                    );
                })}
            </ol>
        </section>
    );
}
