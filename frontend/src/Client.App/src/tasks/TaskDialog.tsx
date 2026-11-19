// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, useState, type RefObject } from 'react';
import {
    dueDayOffsetMinutes,
    longestTaskTitle,
    reviseTask,
    wentUnanswered,
    type CalendarEvent,
    type ClientFailure,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTask,
    type TaskAnchor,
    type TaskCounterpart,
    type TaskDirection,
} from '@mailfathom/client-backend';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { ChoiceSegment } from '../controls/ChoiceSegment';
import { dialogField } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { SurfaceControl } from '../controls/SurfaceControl';
import { SymbolControl } from '../controls/SymbolControl';
import type { MessageKey } from '../localization/en';
import { calendarDayIn, dayFrom, wordInstant } from '../localization/instants';
import type { Locale } from '../localization/locale';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { wordReminderCount } from '../reminders/reminderWords';
import { useTopLayerSurface } from '../shell/screenLayers';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { clashOf } from './taskViews';
import { wordCounterpart, wordPlan, wordReply } from './taskWording';

// One task opened, which is where it is read and where it is changed: its line, which way it runs, the day it is due
// or followed up on, where it is planned, what it belongs to and who it is with, what announces it, and the three acts
// the design puts at its foot.
//
// **One *Save* writes the whole of it.** The route takes the whole record rather than the part that changed, so what
// was edited here is restated beside everything the dialog does not edit — the reminders, the estimate, the plan and
// the message it cites — and the write is this dialog's own, so the calendar and a person's page can open the same
// task and get the same answer to a refusal.
//
// **A refusal is read for what it means here.** A `404` names the task gone when nothing about what it belongs to
// changed, and the context it was moved to otherwise; a `409` is the deployment refusing to turn a task a reply already
// answered the other way. Each is said in the dialog, which stays open on what was typed, except a task that is gone,
// which there is nothing left to edit.
//
// **Four things the design draws here are not drawn yet, each the change of a later task.** *Plan* and *Move* open the
// plan dialog; *Change* beside *Belongs to* chooses among recent threads and events, which no route lists; *Follow up*
// opens a reply in the thread, which is the composer's; and *Open* on the person is the person page's. What is drawn
// in their place is what the task holds, never a control that would do nothing.
//
// The dialog is the platform's own, so the page behind it is inert, focus moves into it and is held there, Escape
// leaves it, and leaving it puts focus back on what opened it — which is why the caller holds the reference.

const anchorGlyphs: Readonly<Record<TaskAnchor['kind'], IconName>> = { Thread: 'mail', Event: 'event' };
const anchorKinds: Readonly<Record<TaskAnchor['kind'], MessageKey>> = {
    Thread: 'tasks.kindThread',
    Event: 'tasks.kindEvent',
};

// How many working days after tomorrow the design offers as chips beside *Today* and *Tomorrow*.
const workingDaysOffered = 3;

// The days offered as chips: none, today, tomorrow and the next working days after it — and the day the task is
// already due on where it is none of those, so the chip that says what is held is always drawn.
function dayChoices(today: string, held: string | null): readonly (string | null)[] {
    const later: string[] = [];

    for (let ahead = 2; later.length < workingDaysOffered; ahead += 1) {
        const day = dayFrom(today, ahead);
        const weekday = new Date(`${day}T00:00:00Z`).getUTCDay();

        if (weekday !== 0 && weekday !== 6) {
            later.push(day);
        }
    }

    const offered = [null, today, dayFrom(today, 1), ...later];

    return held === null || offered.includes(held) ? offered : [...offered, held];
}

function wordDayChoice(day: string | null, today: string, locale: Locale, translate: (key: MessageKey) => string) {
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

/** What a write refused said, as the sentence the dialog shows, or `'gone'` where the task is no longer there. */
function refusalOf(failure: ClientFailure, anchorMoved: boolean): MessageKey | 'gone' {
    if (failure.reason === 'missing') {
        return anchorMoved ? 'tasks.anchorUnopenable' : 'gone';
    }

    if (failure.status === 409) {
        return 'tasks.directionAnswered';
    }

    return wentUnanswered(failure) ? 'tasks.writeUnknown' : 'tasks.writeFailed';
}

interface TaskEdit {
    readonly title: string;
    readonly dueOn: string | null;
    readonly direction: TaskDirection;
    readonly anchor: TaskAnchor | null;
    readonly counterpart: TaskCounterpart | null;
}

export function TaskDialog({
    session,
    transport,
    task,
    asked,
    events,
    onWritten,
    onToggleCompleted,
    onAskErasure,
    onReminders,
    onOpenAnchor,
    onLeft,
}: {
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly task: PersonalTask;

    /** The dialog itself, held by the caller so that every way of opening it reaches the same one. */
    readonly asked: RefObject<HTMLDialogElement | null>;

    /** The calendar events the plan is read against for a clash, which is the day the caller has read. */
    readonly events: readonly CalendarEvent[];

    /** Says what a write left behind once the dialog has closed on it, so the screen reads the list again. */
    readonly onWritten: (said: MessageKey) => void;

    /** Completes the task or reopens it — a reply-closed one included, which is what *Keep waiting* is. */
    readonly onToggleCompleted: (task: PersonalTask) => void;

    /** Raises the question the erasure stands behind. */
    readonly onAskErasure: (task: PersonalTask) => void;

    /** Opens what announces the task, over this dialog. */
    readonly onReminders: (task: PersonalTask) => void;

    /** Opens what the task belongs to, or `undefined` where this credential reaches neither a thread nor the calendar. */
    readonly onOpenAnchor: ((anchor: TaskAnchor) => void) | undefined;

    /** Says the dialog has been left, which is what lets the screen stop holding the task it was drawing. */
    readonly onLeft: () => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const closesOnBack = useTopLayerSurface();
    const heading = useId();
    const titled = useId();

    // Read once for the life of the dialog, as the screen reads it: a chip that moved from *Tomorrow* to *Today* under
    // a reader's finger at midnight would be a different day than the one they pressed.
    const [now] = useState(() => Date.now());
    const [edit, setEdit] = useState<TaskEdit>(() => ({
        title: task.title,
        dueOn: task.dueOn,
        direction: task.direction,
        anchor: task.anchor,
        counterpart: task.counterpart,
    }));
    const [said, setSaid] = useState<MessageKey | null>(null);
    const [saving, setSaving] = useState(false);

    const writeRefused = useWriteRefusal();
    const refused = writeRefused(translate('tasks.save'));
    const today = calendarDayIn(now, timeZone);
    const line = edit.title.trim();
    const savable = line !== '' && line.length <= longestTaskTitle && !saving;
    const awaited = edit.direction === 'Awaited';
    const replied =
        task.answer === null ? null : wordReply(task.answer.arrivedAt, task.counterpart, locale, timeZone, translate);
    const clash = clashOf(task, events);
    const plan = task.plan === null ? null : wordPlan(task.plan, locale, now, timeZone, translate);

    function save(): void {
        if (session === null || !savable || refused !== null) {
            return;
        }

        const anchorMoved = edit.anchor !== null && edit.anchor.id !== task.anchor?.id;

        setSaving(true);
        setSaid(null);

        void reviseTask(session, transport, task.id, {
            title: line,
            dueOn: edit.dueOn,
            // A lead is measured back from the due day, so a task with no day announces nothing.
            reminders: edit.dueOn === null ? [] : task.reminders,
            dueDayOffsetMinutes: dueDayOffsetMinutes(edit.dueOn, timeZone),
            sourceMessageId: task.sourceMessageId,
            direction: edit.direction,
            estimatedMinutes: task.estimatedMinutes,
            plan: task.plan,
            anchor: edit.anchor,
            counterpart: edit.counterpart,
        }).then((answer) => {
            setSaving(false);

            if (answer.outcome === 'read') {
                asked.current?.close();
                onWritten('tasks.revised');

                return;
            }

            const refusal = refusalOf(answer.failure, anchorMoved);

            if (refusal === 'gone') {
                asked.current?.close();
                onWritten('tasks.gone');
            } else {
                setSaid(refusal);
            }
        });
    }

    function closeThen(act: () => void): void {
        asked.current?.close();
        act();
    }

    const anchor = edit.anchor;
    const counterpart = edit.counterpart;

    return (
        <dialog
            onToggle={closesOnBack}
            ref={asked}
            aria-labelledby={heading}
            className="surface-pop m-auto w-dialog max-w-dialog-narrow rounded-lg border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            // The screen lets go of the task as the dialog is left, which takes this component down with it — so
            // nothing half-typed outlives the question it was typed into.
            onClose={onLeft}
        >
            <form
                className="flex flex-col"
                onSubmit={(submitting) => {
                    submitting.preventDefault();
                    save();
                }}
            >
                <div className="flex items-center gap-3 border-b border-line bg-sunken px-4.5 py-3.5">
                    <h2 id={heading} className="min-w-0 flex-1 truncate text-lg font-semibold">
                        {translate(awaited ? 'tasks.dialogWaiting' : 'tasks.dialogTask')}
                    </h2>

                    <SurfaceControl
                        label={translate('tasks.closeTask')}
                        icon="close"
                        onActivate={() => {
                            asked.current?.close();
                        }}
                    />
                </div>

                <div className="flex flex-col gap-3.5 overflow-auto px-4.5 py-4.5">
                    {replied === null ? null : (
                        <div className="flex flex-wrap items-center gap-2.5 rounded-lg border border-accent-line bg-accent-soft px-3 py-2.5 text-base text-text-soft">
                            <Icon name="mark_email_read" className="size-icon-lg shrink-0 text-accent-deep" />
                            <span className="min-w-0 flex-1">
                                {translate('tasks.closedItself', { reply: replied })}
                            </span>
                            <button
                                type="button"
                                className="text-sm text-accent-deep hover:underline"
                                onClick={() => {
                                    closeThen(() => {
                                        onToggleCompleted(task);
                                    });
                                }}
                            >
                                {translate('tasks.keepWaiting')}
                            </button>
                        </div>
                    )}

                    <label htmlFor={titled} className="sr-only">
                        {translate('tasks.whatIsIt')}
                    </label>
                    <input
                        id={titled}
                        type="text"
                        maxLength={longestTaskTitle}
                        placeholder={translate('tasks.whatIsIt')}
                        value={edit.title}
                        className={dialogField}
                        onChange={(typing) => {
                            setEdit({ ...edit, title: typing.target.value });
                        }}
                    />

                    <fieldset className="flex gap-1.5">
                        <legend className="sr-only">{translate('tasks.directions')}</legend>
                        {(['Owed', 'Awaited'] as const).map((direction) => (
                            <ChoiceSegment
                                key={direction}
                                shape="chip"
                                name={`${heading}-direction`}
                                value={direction}
                                chosen={edit.direction === direction}
                                onChoose={() => {
                                    setEdit({ ...edit, direction });
                                }}
                            >
                                {translate(direction === 'Owed' ? 'tasks.iOwe' : 'tasks.iAwait')}
                            </ChoiceSegment>
                        ))}
                    </fieldset>

                    <fieldset className="flex flex-col gap-1.75">
                        <legend className="mb-1.75 text-sm font-semibold text-muted">
                            {translate(awaited ? 'tasks.followUpOnLabel' : 'tasks.due')}
                        </legend>
                        <div className="flex flex-wrap gap-1.5">
                            {dayChoices(today, task.dueOn).map((day) => (
                                <ChoiceSegment
                                    key={day ?? 'none'}
                                    shape="chip"
                                    name={`${heading}-day`}
                                    value={day ?? ''}
                                    chosen={edit.dueOn === day}
                                    onChoose={() => {
                                        setEdit({ ...edit, dueOn: day });
                                    }}
                                >
                                    {wordDayChoice(day, today, locale, translate)}
                                </ChoiceSegment>
                            ))}
                        </div>
                    </fieldset>

                    {awaited ? null : (
                        <div className="flex flex-col gap-1.75 rounded-lg border border-line bg-sunken px-3.25 py-3">
                            <div className="flex items-center gap-2.25">
                                <Icon name="schedule" className="size-icon-lg shrink-0 text-muted" />
                                <span className="text-base font-semibold">{translate('tasks.planned')}</span>
                                <span className="min-w-0 flex-1 text-base text-text-soft">
                                    {plan === null
                                        ? translate('tasks.notPlanned')
                                        : task.plan !== null &&
                                            calendarDayIn(Date.parse(task.plan.startAt), timeZone) === today
                                          ? translate('tasks.plannedToday', { time: plan })
                                          : plan}
                                </span>
                            </div>
                            {clash === null ? null : (
                                <p className="flex items-center gap-1.5 text-sm text-error-text">
                                    <Icon name="warning" className="size-icon-sm shrink-0" />
                                    {translate('tasks.clashesAt', {
                                        title: clash.title,
                                        time: wordInstant(clash.start, locale, 'time', timeZone) ?? '',
                                    })}
                                </p>
                            )}
                        </div>
                    )}

                    <div className="flex flex-col gap-1.75 rounded-lg border border-line bg-sunken px-3.25 py-3">
                        <div className="flex items-center gap-2.25">
                            <Icon name="link" className="size-icon-lg shrink-0 text-muted" />
                            <span className="text-base font-semibold">{translate('tasks.belongsTo')}</span>
                            {anchor === null && counterpart === null ? (
                                <span className="flex-1 text-base text-muted">
                                    {translate('tasks.belongsToNothing')}
                                </span>
                            ) : null}
                        </div>

                        {anchor === null ? null : (
                            <ContextRow
                                glyph={anchorGlyphs[anchor.kind]}
                                kind={translate(anchorKinds[anchor.kind])}
                                label={anchor.label}
                                onOpen={
                                    onOpenAnchor === undefined
                                        ? undefined
                                        : () => {
                                              closeThen(() => {
                                                  onOpenAnchor(anchor);
                                              });
                                          }
                                }
                                onRemove={() => {
                                    setEdit({ ...edit, anchor: null });
                                }}
                            />
                        )}

                        {counterpart === null ? null : (
                            <ContextRow
                                glyph="person"
                                kind={translate('tasks.kindPerson')}
                                label={wordCounterpart(counterpart)}
                                onOpen={undefined}
                                onRemove={() => {
                                    setEdit({ ...edit, counterpart: null });
                                }}
                            />
                        )}
                    </div>

                    {awaited && replied === null ? (
                        <p className="flex items-center gap-2.5 text-base text-text-soft">
                            <Icon name="hourglass_top" className="size-icon-lg shrink-0 text-warning" />
                            {translate('tasks.closesOnReply')}
                        </p>
                    ) : null}

                    <div className="flex items-center gap-2.25 text-base">
                        <Icon name="notifications_active" className="size-icon-lg shrink-0 text-muted" />
                        <span className="flex-1 text-text-soft">
                            {task.reminders.length === 0
                                ? translate('tasks.noReminder')
                                : wordReminderCount(task.reminders.length, locale, translate)}
                        </span>
                        {task.dueOn === null ? null : (
                            <button
                                type="button"
                                aria-label={translate('tasks.changeReminders')}
                                className="text-sm text-accent-deep hover:underline"
                                onClick={() => {
                                    onReminders(task);
                                }}
                            >
                                {translate('tasks.change')}
                            </button>
                        )}
                    </div>

                    {said === null && refused === null ? null : (
                        <p role="alert" className="text-sm text-warning text-pretty">
                            {refused ?? (said === null ? '' : translate(said))}
                        </p>
                    )}
                </div>

                <div className="flex flex-wrap items-center gap-2.25 border-t border-line-soft px-4.5 py-3">
                    <button
                        type="button"
                        className="rounded-md px-2.5 py-2 text-base text-error-text transition hover:bg-hover"
                        onClick={() => {
                            closeThen(() => {
                                onAskErasure(task);
                            });
                        }}
                    >
                        {translate('tasks.deleteOne')}
                    </button>

                    <span className="flex-1" />

                    <button
                        type="button"
                        className={mannerDrawn.back}
                        onClick={() => {
                            closeThen(() => {
                                onToggleCompleted(task);
                            });
                        }}
                    >
                        {translate(doneLabel(task, awaited))}
                    </button>

                    <button
                        type="submit"
                        disabled={!savable}
                        aria-disabled={refused === null ? undefined : true}
                        className={`${mannerDrawn.act} disabled:cursor-not-allowed disabled:bg-rail disabled:text-faint disabled:opacity-100 aria-disabled:cursor-not-allowed aria-disabled:opacity-60`}
                    >
                        {translate(saving ? 'tasks.saving' : 'tasks.save')}
                    </button>
                </div>
            </form>
        </dialog>
    );
}

// One thing the task belongs to or is with: its kind and name, the way to open it where there is one, and the way to
// make the task belong to it no longer.
function ContextRow({
    glyph,
    kind,
    label,
    onOpen,
    onRemove,
}: {
    readonly glyph: IconName;
    readonly kind: string;
    readonly label: string;
    readonly onOpen: (() => void) | undefined;
    readonly onRemove: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex items-center gap-2.25 rounded-md border border-line bg-panel px-2.5 py-2">
            <Icon name={glyph} className="size-icon-lg shrink-0 text-accent-deep" />
            <span className="flex min-w-0 flex-1 flex-col gap-px">
                <span className="text-xs text-muted">{kind}</span>
                <span className="truncate text-base">{label}</span>
            </span>
            {onOpen === undefined ? null : (
                <button type="button" className="text-sm text-accent-deep hover:underline" onClick={onOpen}>
                    {translate('tasks.openAnchor')}
                </button>
            )}
            <SymbolControl
                label={translate('tasks.removeLinkTo', { label })}
                className="flex size-7 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
                onPress={onRemove}
            >
                <Icon name="link_off" className="size-icon-sm" />
            </SymbolControl>
        </div>
    );
}

// What the foot's completion act says, which is what pressing it would do to this task.
function doneLabel(task: PersonalTask, awaited: boolean): MessageKey {
    if (task.completed) {
        return awaited ? 'tasks.stillWaiting' : 'tasks.markNotDone';
    }

    return awaited ? 'tasks.markReceived' : 'tasks.markDone';
}
