// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState } from 'react';
import type { CalendarEvent, PersonalTask, TaskPlan } from '@mailfathom/client-backend';
import { instantOf, sendTimeAt } from '../composer/sendLater';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { ChoiceSegment } from '../controls/ChoiceSegment';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useTip } from '../controls/useTip';
import { calendarDayIn, wordInstant, wordInstantRange } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useTopLayerSurface } from '../shell/screenLayers';
import { endAfter } from './dayInstants';
import { dayHours, hourOf } from './dayHours';
import { HourItem } from './HourItem';
import { clashOf } from './taskViews';
import { daysOffered, wordDayChoice, wordEstimate, wordPlan, wordTaskDay } from './taskWording';

// Planning one task into a day: four days to choose from, how long it takes, and the chosen day's hours with what the
// calendar and the other planned tasks already hold in them. An hour is pressed to place the task there, the line under
// the hours says when it is planned, and a meeting the stretch overlaps is named — which never stops it being planned,
// since a person may well mean to.
//
// **The dialog hands back a plan and writes nothing itself.** Whoever opened it holds the write, so the task dialog's
// plan goes through the same queued write as its chips, and the list's through the list's.
//
// **It opens itself as it is drawn and says when it is left**, so a caller draws it for the task to plan and lets go of
// it on `onLeft`. The dialog is the platform's own, so focus goes back to what opened it when it closes.

// The lengths the design offers, in minutes.
const lengths = [15, 30, 45, 60, 90, 120];

export function PlanDialog({
    task,
    events,
    tasks = [],
    onPlan,
    onLeft,
}: {
    readonly task: PersonalTask;

    /** The calendar events the caller has read, which the chosen day's hours are drawn and read for a clash against. */
    readonly events: readonly CalendarEvent[];

    /** The other tasks the caller holds, which the chosen day's hours draw where they are planned. */
    readonly tasks?: readonly PersonalTask[];

    /** Hands back the plan chosen, or `null` where the task is to be taken out of the calendar. */
    readonly onPlan: (plan: TaskPlan | null) => void;

    /** Says the dialog has been left, which is what lets the caller stop drawing it. */
    readonly onLeft: () => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const closesOnBack = useTopLayerSurface();
    const heading = useId();
    const asked = useRef<HTMLDialogElement | null>(null);

    // Read once for the life of the dialog, as the task dialog reads it, so a chip cannot change day under a finger.
    const [now] = useState(() => Date.now());
    const today = calendarDayIn(now, timeZone);
    const held = task.plan === null ? null : sendTimeAt(Date.parse(task.plan.startAt), timeZone);
    const [day, setDay] = useState(held?.date ?? (task.dueOn !== null && task.dueOn >= today ? task.dueOn : today));
    const [time, setTime] = useState<string | null>(held?.time ?? null);
    const [minutes, setMinutes] = useState(task.plan?.minutes ?? task.estimatedMinutes ?? 30);

    // Opening the platform's dialog is an imperative call on an element that has to exist first.
    useEffect(() => {
        if (asked.current?.open === false) {
            asked.current.showModal();
        }
    }, []);

    const offeredDays = daysOffered(today);
    const days = offeredDays.includes(day) ? offeredDays : [...offeredDays, day];
    const lengthsOffered = lengths.includes(minutes)
        ? lengths
        : [...lengths, minutes].sort((one, other) => one - other);
    const startsAt = time === null ? null : instantOf({ date: day, time }, timeZone);
    const plan = startsAt === null ? null : { startAt: new Date(startsAt).toISOString(), minutes };
    const clash = plan === null ? null : clashOf({ ...task, completed: false, plan }, events);
    const planned = tasks.filter((other) => other.id !== task.id && !other.completed && other.plan !== null);
    const pickedHour = time === null ? null : Number(time.slice(0, 2));

    const cannotPlan = translate('tasks.pickAnHourFirst');
    const { anchor, tip } = useTip({
        says: cannotPlan,
        describes: plan === null,
        onPress:
            plan === null
                ? undefined
                : () => {
                      leaveWith(plan);
                  },
    });

    function leaveWith(chosen: TaskPlan | null): void {
        asked.current?.close();
        onPlan(chosen);
    }

    return (
        <dialog
            ref={asked}
            // Neither event goes further: a caller may draw this inside a dialog of its own, and React carries both
            // up its tree, where they would read as that dialog opening, closing or being left.
            onToggle={(event) => {
                event.stopPropagation();
                closesOnBack(event);
            }}
            aria-labelledby={heading}
            className="surface-pop m-auto w-dialog max-w-dialog-narrow rounded-lg border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            onClose={(event) => {
                event.stopPropagation();
                onLeft();
            }}
        >
            <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                <div className="flex min-w-0 flex-1 flex-col gap-px">
                    <h2 id={heading} className="text-lg font-semibold">
                        {translate('tasks.planATime')}
                    </h2>
                    <span className="truncate text-sm text-muted">{task.title}</span>
                </div>

                <SurfaceControl
                    label={translate('tasks.closeTask')}
                    icon="close"
                    onActivate={() => {
                        asked.current?.close();
                    }}
                />
            </div>

            <div className="flex flex-col gap-3 overflow-auto px-4.5 py-4">
                <fieldset className="flex flex-wrap gap-1.5">
                    <legend className="sr-only">{translate('tasks.planDay')}</legend>
                    {days.map((offered) => (
                        <ChoiceSegment
                            key={offered}
                            shape="chip"
                            name={`${heading}-day`}
                            value={offered}
                            chosen={offered === day}
                            onChoose={() => {
                                setDay(offered);
                            }}
                        >
                            {wordDayChoice(offered, today, locale, translate)}
                        </ChoiceSegment>
                    ))}
                </fieldset>

                <fieldset className="flex flex-col gap-1.5">
                    <legend className="mb-1.5 text-sm font-semibold text-muted">{translate('tasks.howLong')}</legend>
                    <div className="flex flex-wrap gap-1.5">
                        {lengthsOffered.map((length) => (
                            <ChoiceSegment
                                key={length}
                                shape="chip"
                                name={`${heading}-length`}
                                value={String(length)}
                                chosen={length === minutes}
                                onChoose={() => {
                                    setMinutes(length);
                                }}
                            >
                                {wordEstimate(length, locale)}
                            </ChoiceSegment>
                        ))}
                    </div>
                </fieldset>

                <ol className="flex flex-col rounded-lg border border-line bg-panel px-3 py-1">
                    {dayHours.map((hour) => {
                        const opens = instantOf({ date: day, time: `${String(hour).padStart(2, '0')}:00` }, timeZone);
                        const meetings = events.filter(
                            (event) => !event.isAllDay && hourOf(event.start, timeZone, day) === hour,
                        );
                        const planning = planned.filter(
                            (other) => other.plan !== null && hourOf(other.plan.startAt, timeZone, day) === hour,
                        );
                        const picked = pickedHour === hour;

                        return (
                            <li key={hour}>
                                <button
                                    type="button"
                                    aria-pressed={picked}
                                    className="flex min-h-7.5 w-full items-start gap-2.5 border-t border-line-soft py-1.25 text-start first:border-t-0 hover:bg-hover"
                                    onClick={() => {
                                        setTime(`${String(hour).padStart(2, '0')}:00`);
                                    }}
                                >
                                    <span className="w-10 shrink-0 pt-1.5 text-xs text-faint">
                                        {opens === null
                                            ? ''
                                            : wordInstant(new Date(opens).toISOString(), locale, 'time', timeZone)}
                                    </span>
                                    <span className="flex min-w-0 flex-1 flex-col gap-1">
                                        {meetings.map((meeting) => (
                                            <HourItem
                                                key={meeting.id}
                                                glyph="groups"
                                                title={meeting.title}
                                                time={wordInstant(meeting.start, locale, 'time', timeZone) ?? ''}
                                                tone="meeting"
                                            />
                                        ))}
                                        {planning.map((other) => (
                                            <HourItem
                                                key={other.id}
                                                glyph="check_box_outline_blank"
                                                title={other.title}
                                                time={
                                                    other.plan === null
                                                        ? ''
                                                        : (wordPlan(other.plan, locale, now, timeZone, translate) ?? '')
                                                }
                                                tone={clashOf(other, events) === null ? 'task' : 'clash'}
                                            />
                                        ))}
                                        {picked && plan !== null ? (
                                            <HourItem
                                                glyph="check_box_outline_blank"
                                                title={task.title}
                                                time={wordRange(plan)}
                                                tone="picked"
                                            />
                                        ) : null}
                                    </span>
                                </button>
                            </li>
                        );
                    })}
                </ol>

                <p className="text-base text-text-soft">
                    {plan === null
                        ? translate('tasks.pickAnHour')
                        : translate('tasks.plannedHint', {
                              day: wordTaskDay(day, locale, now, timeZone),
                              time: wordRange(plan),
                          })}
                </p>

                {clash === null ? null : (
                    <p className="flex items-center gap-1.5 text-sm text-error-text">
                        <Icon name="warning" className="size-icon-sm shrink-0" />
                        {translate('tasks.planClashesAt', {
                            title: clash.title,
                            time: wordInstant(clash.start, locale, 'time', timeZone) ?? '',
                        })}
                    </p>
                )}

                <div className="flex flex-wrap items-center gap-2.25">
                    {task.plan === null ? null : (
                        <button
                            type="button"
                            className="rounded-md px-2.5 py-2 text-base text-text-soft transition hover:bg-hover"
                            onClick={() => {
                                leaveWith(null);
                            }}
                        >
                            {translate('tasks.takeOutOfCalendar')}
                        </button>
                    )}

                    <span className="flex-1" />

                    <button
                        type="button"
                        className={mannerDrawn.back}
                        onClick={() => {
                            asked.current?.close();
                        }}
                    >
                        {translate('act.cancel')}
                    </button>

                    <button
                        type="button"
                        {...anchor}
                        aria-disabled={plan === null ? true : undefined}
                        className={`${mannerDrawn.act} aria-disabled:cursor-not-allowed aria-disabled:opacity-45`}
                    >
                        {translate('tasks.plan')}
                    </button>
                    {tip}
                </div>
            </div>
        </dialog>
    );

    function wordRange(chosen: TaskPlan): string {
        return (
            wordInstantRange(chosen.startAt, endAfter(chosen.startAt, chosen.minutes), locale, 'time', timeZone) ?? ''
        );
    }
}
