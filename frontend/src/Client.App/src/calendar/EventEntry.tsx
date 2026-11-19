// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { KeyboardEvent } from 'react';
import type { CalendarEvent } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import { wordInstantRange } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import type { CalendarTask, DayEntry } from './calendarSpan';
import type { EventActs } from './eventActs';
import { eventShapes, taskMarks, taskTones, timeShown, type EventShape, type TaskTone } from './eventShapes';

// One event wherever a view draws one, which is its own component for the reason a message row is: it is what carries
// the press, the keyboard path, and a test. Four views draw it and none of them draws a second one, which is what keeps
// picking an event out of a week the same gesture as picking one out of an agenda.
//
// **It answers a press exactly as every other list in this client does**, through `contextMenu/rowPress.ts` rather
// than through a gesture of its own — the design gives all six of its lists one behaviour.
//
// **Every entry is its own tab stop**, which is where this list differs from the address book's. That one is a single
// column with a roving tab stop walked by the arrow keys; a week is seven columns and a day is twenty-four rows, so
// there is no one order for an arrow key to walk and a roving stop would leave six of the seven columns unreachable.
// What a view holds is bounded by the span it draws rather than by the mailbox, so tabbing through it is a walk of a
// dozen entries rather than of a hundred thousand.
//
// **A task stands among the events in the same shape**, told apart by its border and its first line — *Task · 11:00–11:45*
// where it is planned, *Due · task* where it is due that day and planned nowhere — and pressed, it opens the task
// dialog rather than the event dialog. It is never picked out with the events: the selection is the calendar's, and
// what it acts on is events. A task done stays where it was planned, faded.

export function EventEntry({
    event,
    shape,
    selected,
    picking,
    onOpen,
    onToggle,
    onPress,
}: {
    readonly event: CalendarEvent;

    /** How the view around it draws one, which decides what it has already said. */
    readonly shape: EventShape;

    /** Whether this entry is one of those picked out. */
    readonly selected: boolean;

    /** Whether the screen is holding a selection at all, which is what makes a plain press pick an entry out. */
    readonly picking: boolean;

    readonly onOpen: () => void;

    /** Puts this entry into the selection or takes it out, which a modifier-held press and a plain press both reach. */
    readonly onToggle: () => void;

    /** Opens this entry's own menu at the point the gesture happened. */
    readonly onPress: (at: MenuPoint) => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const press = useRowPress(onPress);
    const look = eventShapes[shape];

    const when = wordInstantRange(event.start, event.end, locale, 'time', timeZone);

    return (
        <li
            role="option"
            aria-selected={selected}
            tabIndex={0}
            className={`${look.shape} ${selected ? look.selected : look.unselected}`}
            onContextMenu={press.onContextMenu}
            onPointerDown={press.onPointerDown}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
            onClick={(clicking) => {
                // The tap that follows a press which has already opened a menu does nothing, or the menu would be
                // closed by the same finger that asked for it.
                if (press.tapSuppressed()) {
                    return;
                }

                if (clicking.ctrlKey || clicking.metaKey || clicking.shiftKey || picking) {
                    onToggle();

                    return;
                }

                onOpen();
            }}
            onKeyDown={(typing: KeyboardEvent) => {
                if (typing.key !== 'Enter' && typing.key !== ' ') {
                    return;
                }

                typing.preventDefault();

                // The same two gestures the pointer has, said with the keyboard: a modifier held picks the entry out,
                // and pressing one plainly while a selection is held picks it out as well — which is the whole of how
                // a selection is worked without a mouse.
                if (typing.ctrlKey || typing.metaKey || typing.shiftKey || picking) {
                    onToggle();

                    return;
                }

                onOpen();
            }}
        >
            {timeShown(shape) && !event.isAllDay && when !== null ? (
                <span className="text-xs text-muted">{when}</span>
            ) : null}

            <span className={`truncate ${shape === 'cell' ? 'text-xs' : 'text-sm'} text-pretty`}>{event.title}</span>

            {/* The message the event was read out of, said as a mark rather than as a second line: which message it was
                is the event's own surface to say, and a title beside a title would compete with the one the reader is
                scanning for. */}
            {event.sourceMessage === null || shape === 'cell' ? null : (
                <span className="flex items-center gap-1 text-xs text-accent-deep">
                    <Icon name="open_in_new" className="size-icon-xs" />
                    {translate('calendar.fromMail')}
                </span>
            )}
        </li>
    );
}

/** One thing a view draws for a day, an event or a task, with what the screen does with either. */
export function DayEntryView({
    entry,
    shape,
    acts,
}: {
    readonly entry: DayEntry;
    readonly shape: EventShape;
    readonly acts: EventActs;
}) {
    if (entry.event === undefined) {
        const { task } = entry.task;

        return (
            <TaskEntry
                entry={entry.task}
                shape={shape}
                clashes={acts.clashes(task)}
                onOpen={() => {
                    acts.onOpenTask(task);
                }}
                onPress={(at) => {
                    acts.onPressTask(task, at);
                }}
            />
        );
    }

    const { event } = entry;

    return (
        <EventEntry
            event={event}
            shape={shape}
            selected={acts.selected.includes(event.id)}
            picking={acts.selected.length > 0}
            onOpen={() => {
                acts.onOpen(event);
            }}
            onToggle={() => {
                acts.onToggle(event);
            }}
            onPress={(at) => {
                acts.onPress(event, at);
            }}
        />
    );
}

export function TaskEntry({
    entry,
    shape,
    clashes,
    onOpen,
    onPress,
}: {
    readonly entry: CalendarTask;
    readonly shape: EventShape;

    /** Whether it is planned over a meeting. */
    readonly clashes: boolean;

    readonly onOpen: () => void;

    /** Opens this task's own menu at the point the gesture happened. */
    readonly onPress: (at: MenuPoint) => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const press = useRowPress(onPress);
    const { task, kind } = entry;
    const tone: TaskTone = kind === 'due' ? 'due' : clashes ? 'clash' : 'planned';
    const agenda = shape === 'agenda';

    const planned =
        task.plan === null
            ? null
            : wordInstantRange(
                  task.plan.startAt,
                  new Date(Date.parse(task.plan.startAt) + task.plan.minutes * 60_000).toISOString(),
                  locale,
                  'time',
                  timeZone,
              );
    const when = kind === 'due' ? translate('calendar.taskDue') : translate('calendar.taskAt', { time: planned ?? '' });
    const thread = task.anchor?.kind === 'Thread' ? task.anchor.label : null;

    return (
        <li
            role="option"
            tabIndex={0}
            className={`${eventShapes[shape].shape} ${agenda ? 'border-transparent hover:bg-hover' : taskTones[tone]} ${
                task.completed ? 'opacity-55' : ''
            }`}
            onContextMenu={press.onContextMenu}
            onPointerDown={press.onPointerDown}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
            onClick={() => {
                if (!press.tapSuppressed()) {
                    onOpen();
                }
            }}
            onKeyDown={(typing: KeyboardEvent) => {
                if (typing.key === 'Enter' || typing.key === ' ') {
                    typing.preventDefault();
                    onOpen();
                }
            }}
        >
            {/* A cell has room for the line alone, so what kind of task it is is said to whoever is listening. */}
            {timeShown(shape) ? (
                <span className="flex items-center gap-1.75 text-xs text-muted">
                    {agenda ? (
                        <span aria-hidden="true" className={`size-2 shrink-0 rounded-xs ${taskMarks[tone]}`} />
                    ) : null}
                    {when}
                </span>
            ) : (
                <span className="sr-only">{when}</span>
            )}

            <span className={`truncate ${shape === 'cell' ? 'text-xs' : 'text-sm'} text-pretty`}>{task.title}</span>

            {thread === null || (shape !== 'column' && shape !== 'hour') ? null : (
                <span className="flex min-w-0 items-center gap-1 text-xs text-accent-deep">
                    <Icon name="open_in_new" className="size-icon-xs shrink-0" />
                    <span className="truncate">{thread}</span>
                </span>
            )}
        </li>
    );
}
