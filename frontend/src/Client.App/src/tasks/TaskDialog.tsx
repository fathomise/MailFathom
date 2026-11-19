// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type RefObject } from 'react';
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
// or followed up on, where it is planned, what it belongs to — the thread or event, or the person where it has neither
// — what announces it, and the three acts the design puts at its foot. *Keep waiting* on a wait a reply closed reopens
// it here, with the dialog left open on it.
//
// **A chip applies as it is pressed, and only the line waits for *Save*.** Which way the task runs, its day and what it
// belongs to are each written the moment they are chosen, as the design's dialog applies them; the line is typed, so it
// is written when the person says they are done with it. Every write restates the whole record, because the route takes
// the whole record rather than the part that changed — the reminders, the estimate, the plan and the message it cites
// travel beside the one field that moved — and the write is this dialog's own, so the calendar and a person's page can
// open the same task and get the same answer to a refusal.
//
// **A refusal is read for what it means here.** A `404` names the task gone when nothing about what it belongs to
// changed, and the context it was moved to otherwise; a `409` is the deployment refusing to turn a task a reply already
// answered the other way. Each is said in the dialog, which stays open with the chip back where the deployment holds
// it, except a task that is gone, which there is nothing left to edit.
//
// **An answer outlives the dialog it was asked from**, because the caller holds one dialog element for every task it
// opens. So an answer arriving after this dialog was left only tells the screen what the write did; it never closes
// or speaks in whatever dialog is open by then.
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

// What the dialog's chips write, each the moment it is pressed, and the reminders that follow the day.
interface TaskChoices {
    readonly dueOn: string | null;
    readonly reminders: readonly number[];
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
    const [title, setTitle] = useState(task.title);

    // What the chips pressed here applied, over what the task the screen read holds: a chip stands where it was pressed
    // while its write is out, and stays there once the deployment took it, whatever the list has read back by then.
    // `pressed` is the same thing for the write that answers later, and `taken` what the deployment last took.
    const [applied, setApplied] = useState<Partial<TaskChoices>>({});
    const pressed = useRef<Partial<TaskChoices>>({});
    const taken = useRef<Partial<TaskChoices>>({});
    const sending = useRef(false);
    const [said, setSaid] = useState<MessageKey | null>(null);
    const [writing, setWriting] = useState<'save' | 'chip' | null>(null);
    // Whether this dialog still stands, and how many times it has been left: an answer acts here only where neither
    // moved since its write went out, since a dialog left and opened again on the same task is a different question.
    const here = useRef(true);
    const leftTimes = useRef(0);

    useEffect(() => {
        here.current = true;

        return () => {
            here.current = false;
        };
    }, []);

    const choicesOver = (over: Partial<TaskChoices>): TaskChoices => ({
        dueOn: task.dueOn,
        reminders: task.reminders,
        direction: task.direction,
        anchor: task.anchor,
        counterpart: task.counterpart,
        ...over,
    });
    const chosen = choicesOver(applied);
    const writeRefused = useWriteRefusal();
    const refused = writeRefused(translate('tasks.save'));
    const today = calendarDayIn(now, timeZone);
    const line = title.trim();
    const savable = line !== '' && line.length <= longestTaskTitle && writing === null;
    const awaited = chosen.direction === 'Awaited';
    const replied =
        task.answer === null ? null : wordReply(task.answer.arrivedAt, task.counterpart, locale, timeZone, translate);
    const clash = clashOf(task, events);
    const plan = task.plan === null ? null : wordPlan(task.plan, locale, now, timeZone, translate);

    // One write of the whole record, with what was chosen laid over it, and never two at once. A chip pressed while a
    // write is out is laid over the dialog at once and written when that write answers, the latest press winning; a
    // refusal puts every chip back to what the deployment last took. The answer acts on this dialog only while the
    // dialog still stands; once it has been left, the screen is told what the write did and nothing else.
    function send(over: Partial<TaskChoices>, line: string, closes: boolean): void {
        if (session === null) {
            return;
        }

        const next = choicesOver(over);
        const anchorMoved = next.anchor !== null && next.anchor.id !== task.anchor?.id;
        const asking = leftTimes.current;

        sending.current = true;
        setWriting(closes ? 'save' : 'chip');
        setSaid(null);

        void reviseTask(session, transport, task.id, {
            title: line,
            dueOn: next.dueOn,
            reminders: next.reminders,
            dueDayOffsetMinutes: dueDayOffsetMinutes(next.dueOn, timeZone),
            sourceMessageId: task.sourceMessageId,
            direction: next.direction,
            estimatedMinutes: task.estimatedMinutes,
            plan: task.plan,
            anchor: next.anchor,
            counterpart: next.counterpart,
        }).then((answer) => {
            const refusal = answer.outcome === 'read' ? null : refusalOf(answer.failure, anchorMoved);
            const standing = here.current && leftTimes.current === asking && asked.current?.open === true;

            if (refusal !== null && refusal !== 'gone') {
                sending.current = false;
                pressed.current = taken.current;

                if (here.current) {
                    setApplied(taken.current);
                    setWriting(null);
                }

                if (standing) {
                    setSaid(refusal);
                }

                return;
            }

            if (refusal === null) {
                taken.current = over;

                if (!closes && pressed.current !== over) {
                    send(pressed.current, task.title, false);

                    return;
                }
            }

            sending.current = false;

            if (here.current) {
                setWriting(null);
            }

            if (standing && (closes || refusal === 'gone')) {
                asked.current?.close();
            }

            onWritten(refusal === null ? 'tasks.revised' : 'tasks.gone');
        });
    }

    function apply(change: Partial<TaskChoices>): void {
        if (session === null || refused !== null) {
            return;
        }

        // A lead is measured back from the due day, so a task left with no day announces nothing — and stays that
        // way when a day is chosen again, whatever the list has not read back yet.
        const over = { ...pressed.current, ...change, ...(change.dueOn === null ? { reminders: [] } : {}) };

        pressed.current = over;
        setApplied(over);
        setSaid(null);

        if (!sending.current) {
            send(over, task.title, false);
        }
    }

    function save(): void {
        if (savable && !sending.current) {
            send(pressed.current, line, true);
        }
    }

    function closeThen(act: () => void): void {
        asked.current?.close();
        act();
    }

    // What the task belongs to, which is one thing: the thread or event it is anchored to, or the person it is with
    // where it is anchored to nothing — a person being one more thing a task can belong to.
    const anchor = chosen.anchor;
    const counterpart = anchor === null ? chosen.counterpart : null;

    return (
        <dialog
            onToggle={closesOnBack}
            ref={asked}
            aria-labelledby={heading}
            className="surface-pop m-auto w-dialog max-w-dialog-narrow rounded-lg border border-line bg-panel p-0 text-text shadow-dialog backdrop:bg-scrim"
            // The screen lets go of the task as the dialog is left, which takes this component down with it — so
            // nothing half-typed outlives the question it was typed into.
            onClose={() => {
                leftTimes.current += 1;
                setWriting(null);
                onLeft();
            }}
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
                                    onToggleCompleted(task);
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
                        value={title}
                        className={dialogField}
                        onChange={(typing) => {
                            setTitle(typing.target.value);
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
                                chosen={chosen.direction === direction}
                                onChoose={() => {
                                    apply({ direction });
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
                                    chosen={chosen.dueOn === day}
                                    onChoose={() => {
                                        apply({ dueOn: day });
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
                                    apply({ anchor: null });
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
                                    apply({ counterpart: null });
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
                            {chosen.reminders.length === 0
                                ? translate('tasks.noReminder')
                                : wordReminderCount(chosen.reminders.length, locale, translate)}
                        </span>
                        {chosen.dueOn === null ? null : (
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
                        {translate(writing === 'save' ? 'tasks.saving' : 'tasks.save')}
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
