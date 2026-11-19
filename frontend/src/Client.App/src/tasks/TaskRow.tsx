// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { KeyboardEvent } from 'react';
import type { CalendarEvent, PersonalTask, TaskAnchor } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useRowPress } from '../contextMenu/rowPress';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import { useTip } from '../controls/useTip';
import type { IconName } from '../controls/icons';
import { calendarDayIn, wordCalendarDay, wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { wordReminderCount } from '../reminders/reminderWords';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { carriedTaskType } from './dayHours';
import { wordCounterpart, wordEstimate, wordPlan, wordReply, wordTaskDay } from './taskWording';
import { doneTaskFade } from './taskShapes';

const anchorGlyphs: Readonly<Record<TaskAnchor['kind'], IconName>> = { Thread: 'mail', Event: 'event' };

const contextChip =
    'flex max-w-full min-w-0 items-center gap-1.25 rounded-full bg-accent-soft px-2 py-0.5 text-xs text-accent-deep';

// What the task belongs to, pressed to open it. Its name is the label a reader sees, so a voice control reaches it by
// what is on the screen; what pressing it does is the sentence a mouse rests on, a key focuses, or a finger holds.
function AnchorChip({
    glyph,
    label,
    hint,
    onOpen,
}: {
    readonly glyph: IconName;
    readonly label: string;
    readonly hint: string;
    readonly onOpen: () => void;
}) {
    const { anchor, tip } = useTip({ says: hint, describes: true, onPress: onOpen, tapReveals: false });

    return (
        <>
            <button type="button" {...anchor} className={`${contextChip} hover:underline`}>
                <Icon name={glyph} className="size-icon-xs shrink-0" />
                <span className="truncate">{label}</span>
            </button>
            {tip}
        </>
    );
}

// One commitment, as the design draws a task row: the box that completes it — a round dashed hourglass for one the
// person waits on, and the *AI* or *Agent* tag in its place on a proposal — the line, then what is set and nothing
// that is not: what it belongs to — the thread or event, or the person where it has neither — where it is planned or
// how long it takes, a clash with the calendar, the day it is due or followed up on, and the reminder bell. Pressed, it
// opens the task.
//
// **Where a proposal came from is read off what it cites.** One that cites a message was read out of mail; one that
// cites none was suggested by the Agent, which is the only other writer of proposals.
//
// **It answers a press exactly as every other list in this client does**, through `contextMenu/rowPress.ts` rather
// than through a gesture of its own, and through `ContextMenu` above it rather than a second menu — the design gives
// all six of its lists one behaviour.
//
// **The row is not an option, and that is a correctness decision rather than a preference.** Every other selectable
// list here is a `listbox` of rows carrying no controls; a task row carries four, and ARIA refuses interactive content
// inside an `option`. So this is an ordinary list item, and what conveys the selection is the design's own mark made
// pressable: while a selection is held every row draws it as a toggle naming the task, which is how somebody with a
// keyboard adds a fifth row to four and how a screen reader is told which rows are held. Starting the selection where
// no modifier key is being held is the menu's *Select tasks*, exactly as the design draws it.
//
// **Two things the design draws are not drawn here.** *sent Mon 24.08* on a waiting row names when the message went,
// and the task record carries no such instant; and *Follow up* opens a reply, which is a later change.
//
// **A task is planned from its row.** *Plan* stands on an owed task with no time, the planned time is pressed to move
// it, and the row is dragged onto an hour of the day beside the list — except under a finger, where nothing drags and
// *Plan* is the way.
//
// **What announces a task is drawn where the design draws it**, first in the block at the end of the row, as the pill
// the design gives it: the bell filled where something is set and outlined where nothing is, and the count beside it
// only once there is more than one, a single reminder being what the filled bell already says. It is absent on a task
// nobody dated, because a lead is measured back from the due day and the deployment refuses one stated against no day
// — a control that could only be refused is not one this row draws.
//
// **The day is written short and said long.** The design gives it the width of two words at the end of the row, which
// a spelled-out date does not fit, so what is drawn is `localization/instants.ts`'s own short wording. A bare *today*
// or *02.09* means the day something is due only because of where it sits, and position is exactly what a screen
// reader does not convey — so the sentence saying which day it is travels beside it for anybody listening rather than
// being drawn for everybody.

const planChip =
    'flex items-center gap-1.25 rounded-full border border-dashed border-accent bg-panel px-2 py-0.5 text-xs whitespace-nowrap text-text-soft';

// The planned time on a row, pressed to move it, saying so for a mouse, a key and a finger's hold.
function PlanChip({
    plan,
    hint,
    onMove,
}: {
    readonly plan: string;
    readonly hint: string;
    readonly onMove: () => void;
}) {
    const { anchor, tip } = useTip({ says: hint, describes: true, onPress: onMove, tapReveals: false });

    return (
        <>
            <button type="button" {...anchor} className={`${planChip} hover:border-accent-deep`}>
                <Icon name="schedule" className="size-icon-xs shrink-0 text-accent-deep" />
                {plan}
            </button>
            {tip}
        </>
    );
}

export function TaskRow({
    task,
    now,
    clash = null,
    selected,
    selecting,
    completed = task.completed,
    onToggleSelected,
    onToggleCompleted,
    onOpen,
    onOpenAnchor,
    onAccept,
    onDismiss,
    onOpenSource,
    onReminders,
    onPlan,
    dragsToPlan = false,
    onPress,
    onElement,
}: {
    readonly task: PersonalTask;

    /** The reader's own clock, which is what decides whether the day this is due is theirs. */
    readonly now: number;

    /** The calendar event the planned stretch overlaps, or `null` where it clashes with none. */
    readonly clash?: CalendarEvent | null;

    /** Whether this row is one of those picked out. */
    readonly selected: boolean;

    /** Whether a selection is being held at all, which is what draws the mark as something to press. */
    readonly selecting: boolean;

    /** What the box says, which is the state it was last pressed into until the list has read that write back. */
    readonly completed?: boolean;

    /** Puts this row into the selection or takes it out, which a modifier-held press and the mark both reach. */
    readonly onToggleSelected: () => void;

    readonly onToggleCompleted: () => void;

    /** Opens the task in its dialog, or `undefined` for a proposal, which is answered rather than opened. */
    readonly onOpen?: (() => void) | undefined;

    /** Opens what the task belongs to, or `undefined` where that cannot be reached from here. */
    readonly onOpenAnchor?: (() => void) | undefined;

    /** Takes a task mail proposed on, or `undefined` for one the person already owes. */
    readonly onAccept: (() => void) | undefined;

    /** Turns a proposal down, or `undefined` for one the person already owes. */
    readonly onDismiss?: (() => void) | undefined;

    /** Opens the message this task cites, or `undefined` where it cites none. */
    readonly onOpenSource: (() => void) | undefined;

    /** Opens what announces this task, or `undefined` for one nobody dated, which can announce nothing. */
    readonly onReminders: (() => void) | undefined;

    /** Opens the plan dialog for this task, or `undefined` where it cannot be planned from here. */
    readonly onPlan?: (() => void) | undefined;

    /** Whether the row is dragged onto an hour of the day to plan it, which a finger never does. */
    readonly dragsToPlan?: boolean;

    /** Opens this row's own menu at the point the gesture happened. */
    readonly onPress: (at: MenuPoint) => void;

    /** Hands the list this row's element, which is what lets it put focus back here when the menu closes. */
    readonly onElement: (element: HTMLLIElement | null) => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const counted = new Intl.NumberFormat(locale);
    const press = useRowPress(onPress);
    const writeRefused = useWriteRefusal();

    // Every control on the row but the way to the message it cites writes, so while the server is out of reach each
    // is drawn as one that cannot act and is named by why, rather than taken and failing after.
    const refusedAs = (label: string): { readonly held?: true; readonly hint?: string } => {
        const why = writeRefused(label);

        return why === null ? {} : { held: true, hint: why };
    };
    const proposal = task.origin === 'Proposed';
    const awaited = task.direction === 'Awaited';
    const answer = awaited ? task.answer : null;
    const replied = answer !== null;
    const today = calendarDayIn(now, timeZone);
    const followUpCame = awaited && !completed && task.dueOn !== null && task.dueOn <= today;
    const boxLabel = translate(
        awaited
            ? completed
                ? 'tasks.markStillWaitingRow'
                : 'tasks.markReceivedRow'
            : completed
              ? 'tasks.markNotDoneRow'
              : 'tasks.markDoneRow',
        { title: task.title },
    );
    const boxRefused = writeRefused(boxLabel);
    const remindersLabel = translate(task.reminders.length === 0 ? 'tasks.addReminder' : 'tasks.remindersOnRow', {
        title: task.title,
        count: wordReminderCount(task.reminders.length, locale, translate),
    });
    const remindersRefused = writeRefused(remindersLabel);

    function pressed(event: KeyboardEvent): void {
        // The two the platform itself offers for a row's menu, so nothing this row holds is reachable only by gesture:
        // the dedicated key where a keyboard has one, and the chord where it does not.
        if (event.key !== 'ContextMenu' && !(event.key === 'F10' && event.shiftKey)) {
            return;
        }

        const box = event.currentTarget.getBoundingClientRect();

        event.preventDefault();
        onPress({ x: box.left + box.width / 2, y: box.top + box.height / 2 });
    }

    const plan = task.plan === null ? null : wordPlan(task.plan, locale, now, timeZone, translate);
    const anchor = task.anchor;

    return (
        <li
            ref={onElement}
            tabIndex={-1}
            className={`flex flex-wrap items-center gap-x-3 gap-y-2.5 rounded-lg border p-3 transition ${
                selected
                    ? 'border-accent-line bg-accent-soft'
                    : `${clash === null ? 'border-line' : 'border-error'} bg-panel hover:bg-hover`
            } ${completed ? doneTaskFade : ''}`}
            draggable={dragsToPlan}
            onDragStart={(event) => {
                // A type of this client's own rather than text, so a drop on a text field somewhere else pastes nothing.
                event.dataTransfer.effectAllowed = 'move';
                event.dataTransfer.setData(carriedTaskType, task.id);
            }}
            onKeyDown={pressed}
            onContextMenu={press.onContextMenu}
            onPointerDown={press.onPointerDown}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
            onClick={(event) => {
                // The tap that follows a press which has already opened a menu does nothing, or the menu would be
                // closed by the same finger that asked for it.
                if (press.tapSuppressed()) {
                    return;
                }

                // A press on one of the row's own controls is that control's act and not a gesture on the row, so the
                // row acts only where the press landed on the row itself. Asked of the element the event came from
                // rather than of a flag a handler set, because the controls are several and a second one added later
                // would otherwise have to remember to say so.
                if (event.target instanceof Element && event.target.closest('button, input, a, label') !== null) {
                    return;
                }

                // A modifier held picks the row out, and so does a plain press while a selection is already held. A
                // plain press with nothing selected opens the task, which is what the design's row does.
                if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
                    onToggleSelected();
                } else {
                    onOpen?.();
                }
            }}
        >
            <div className="flex min-w-60 flex-1 items-start gap-2.75">
                {selecting ? (
                    <SymbolControl
                        pressed={selected}
                        label={translate('tasks.selectRow', { title: task.title })}
                        className={`mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full border transition ${
                            selected
                                ? 'border-accent bg-accent text-on-accent'
                                : 'border-line-strong text-transparent hover:border-accent'
                        }`}
                        onPress={onToggleSelected}
                    >
                        <Icon name="check" className="size-icon-xs" />
                    </SymbolControl>
                ) : null}

                {proposal ? (
                    <span className="mt-0.5 shrink-0 rounded-sm bg-accent px-1.5 py-0.5 text-xs text-on-accent">
                        {translate(task.sourceMessageId === null ? 'tasks.agentTag' : 'tasks.authored')}
                    </span>
                ) : (
                    // Refused rather than disabled while the server is out of reach: a disabled box leaves the
                    // keyboard and says nothing, while this one stays where it is, unchanged by a press, and is named
                    // by why. A waiting task draws the design's round dashed hourglass over the same box.
                    <label className="relative mt-0.5 flex shrink-0">
                        <input
                            type="checkbox"
                            checked={completed}
                            aria-label={boxRefused ?? boxLabel}
                            aria-disabled={boxRefused === null ? undefined : true}
                            className={`size-5 accent-accent aria-disabled:cursor-not-allowed aria-disabled:opacity-60 ${
                                awaited ? 'peer absolute inset-0 opacity-0' : ''
                            }`}
                            onChange={() => {
                                if (boxRefused === null) {
                                    onToggleCompleted();
                                }
                            }}
                        />
                        {awaited ? (
                            <span
                                aria-hidden="true"
                                className={`flex size-5 items-center justify-center rounded-full peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-accent ${
                                    completed
                                        ? 'border border-accent bg-accent text-on-accent'
                                        : 'border border-dashed border-warning text-warning'
                                }`}
                            >
                                <Icon name={completed ? 'check' : 'hourglass_top'} className="size-icon-xs" />
                            </span>
                        ) : null}
                    </label>
                )}

                <div className="flex min-w-0 flex-col gap-1.5">
                    {onOpen === undefined ? (
                        <span className={`text-base text-pretty ${completed ? 'text-muted line-through' : ''}`}>
                            {task.title}
                        </span>
                    ) : (
                        // The line is the row's own way in from the keyboard, so the dialog a press opens is reached
                        // without one.
                        <button
                            type="button"
                            className={`text-start text-base text-pretty hover:underline ${completed ? 'text-muted line-through' : ''}`}
                            onClick={onOpen}
                        >
                            {task.title}
                        </button>
                    )}

                    {onOpenSource === undefined ? null : (
                        <button
                            type="button"
                            className="flex items-center gap-1.5 self-start rounded-md px-1.5 py-1 text-sm text-muted transition hover:bg-hover hover:text-text"
                            onClick={onOpenSource}
                        >
                            <Icon name="open_in_new" className="size-icon-sm" />
                            {translate('tasks.openSource')}
                        </button>
                    )}
                </div>
            </div>

            <div className="flex flex-wrap items-center gap-2">
                {/* One chip for what the task belongs to: the thread or event, or the person where it is anchored to
                    neither, which is one more thing the design lets a task belong to. */}
                {anchor !== null && onOpenAnchor !== undefined ? (
                    <AnchorChip
                        glyph={anchorGlyphs[anchor.kind]}
                        label={anchor.label}
                        hint={translate(anchor.kind === 'Thread' ? 'tasks.openThread' : 'tasks.openEvent')}
                        onOpen={onOpenAnchor}
                    />
                ) : anchor !== null || task.counterpart !== null ? (
                    <span className={contextChip}>
                        <Icon
                            name={anchor === null ? 'person' : anchorGlyphs[anchor.kind]}
                            className="size-icon-xs shrink-0"
                        />
                        <span className="truncate">
                            {anchor?.label ?? (task.counterpart === null ? '' : wordCounterpart(task.counterpart))}
                        </span>
                    </span>
                ) : null}

                {proposal ? (
                    <span className="text-sm whitespace-nowrap text-muted">
                        {translate(task.sourceMessageId === null ? 'tasks.fromAgent' : 'tasks.fromMail')}
                    </span>
                ) : null}

                {clash === null ? null : (
                    <span className="flex items-center gap-1.25 rounded-full border border-error bg-error-soft px-2 py-0.5 text-xs text-error-text">
                        <Icon name="warning" className="size-icon-xs shrink-0" />
                        {translate('tasks.clashesWith', {
                            title: clash.title,
                            time: wordInstant(clash.start, locale, 'time', timeZone) ?? '',
                        })}
                    </span>
                )}

                {awaited || task.plan !== null || task.estimatedMinutes === null ? null : (
                    <span className="text-sm whitespace-nowrap text-faint">
                        {wordEstimate(task.estimatedMinutes, locale)}
                    </span>
                )}

                {awaited || plan === null ? null : onPlan === undefined || proposal || completed ? (
                    <span className={planChip}>
                        <Icon name="schedule" className="size-icon-xs shrink-0 text-accent-deep" />
                        {plan}
                    </span>
                ) : (
                    <PlanChip plan={plan} hint={translate('tasks.moveInCalendar')} onMove={onPlan} />
                )}

                {dueWording()}

                {onAccept === undefined ? null : (
                    <Control
                        label={translate('tasks.accept')}
                        icon="check"
                        shape="primary"
                        {...refusedAs(translate('tasks.accept'))}
                        onPress={onAccept}
                    />
                )}

                {onDismiss === undefined ? null : (
                    <Control
                        label={translate('tasks.declineTask')}
                        icon="close"
                        shape="symbol"
                        {...refusedAs(translate('tasks.declineTask'))}
                        onPress={onDismiss}
                    />
                )}

                {proposal || awaited || completed || task.plan !== null || onPlan === undefined ? null : (
                    <Control
                        label={translate('tasks.plan')}
                        shape="accentPill"
                        {...refusedAs(translate('tasks.plan'))}
                        onPress={onPlan}
                    />
                )}

                {onReminders === undefined || proposal || replied ? null : (
                    <SymbolControl
                        label={remindersRefused ?? remindersLabel}
                        className={`flex items-center gap-1 rounded-md border px-2.25 py-1 text-xs font-semibold transition hover:border-accent hover:text-accent-deep ${
                            task.reminders.length === 0
                                ? 'border-line bg-sunken text-muted'
                                : 'border-accent-line bg-accent-soft text-accent-deep'
                        } ${remindersRefused === null ? '' : 'cursor-not-allowed opacity-60'}`}
                        onPress={() => {
                            if (remindersRefused === null) {
                                onReminders();
                            }
                        }}
                    >
                        <Icon
                            name={task.reminders.length === 0 ? 'notifications' : 'notifications_active'}
                            className="size-icon-sm"
                        />
                        {task.reminders.length > 1 ? counted.format(task.reminders.length) : null}
                    </SymbolControl>
                )}
            </div>
        </li>
    );

    // The day at the end of the row: when it is due, when to follow up on a wait — *today*, in the warning colour, once
    // that day has come or gone — or who replied and when, for a wait the reply closed.
    function dueWording() {
        if (answer !== null) {
            return (
                <span className="text-base whitespace-nowrap text-healthy-text">
                    {wordReply(answer.arrivedAt, task.counterpart, locale, timeZone, translate)}
                </span>
            );
        }

        if (task.dueOn === null) {
            return <span className="text-base whitespace-nowrap text-text-soft">{translate('tasks.noDay')}</span>;
        }

        const day = wordTaskDay(task.dueOn, locale, now, timeZone);

        return (
            <time
                dateTime={task.dueOn}
                className={`text-base whitespace-nowrap ${followUpCame ? 'text-warning' : 'text-text-soft'}`}
            >
                <span aria-hidden="true">
                    {awaited
                        ? followUpCame
                            ? translate('tasks.followUpToday')
                            : translate('tasks.followUpOn', { day })
                        : day}
                </span>
                <span className="sr-only">
                    {translate(awaited ? 'tasks.followUpDueOn' : 'tasks.dueOn', {
                        day: wordCalendarDay(task.dueOn, locale),
                    })}
                </span>
            </time>
        );
    }
}
