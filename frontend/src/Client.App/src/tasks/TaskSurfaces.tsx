// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    dueDayOffsetMinutes,
    eraseTask,
    reviseTask,
    setTaskCompletion,
    wentUnanswered,
    type CalendarEvent,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTask,
    type TaskAnchor,
    type TaskPlan,
} from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import type { MessageKey } from '../localization/en';
import { calendarDayIn, wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { ReminderPanel } from '../reminders/ReminderPanel';
import { Departing } from '../shell/Departing';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { PlanDialog } from './PlanDialog';
import { TaskDialog } from './TaskDialog';
import { daysSpanned, wordTaskDay } from './taskWording';
import { withPlansPressed, type PlanWriter } from './usePlanWriter';
import { useTodayCalendar } from './useTodayCalendar';

// One task opened from a screen other than Tasks — the calendar, an event, and later a thread and a person — and every
// surface it opens over that screen: the task dialog, the plan dialog, the menu a held task opens, what announces it,
// and the question its deletion stands behind. The screen holds which task was asked for and how; this holds the
// writes, so each screen opening a task gets the same answer to the same press.
//
// **A plan is written by the screen's `usePlanWriter`**, as on the Tasks screen, and by the task dialog's own chips;
// nothing here writes one any other way. The screen holds the writer so that what it draws lays every plan pressed over
// what it read. Every write tells the screen what it did, and the screen reads again — and the plans pressed are let go
// of only once that read has answered, so a plan pressed is drawn at once and a dialog opened again shows it.

/** How a task was asked for: opened, held for its menu at a point, or straight to choosing its plan. */
export type TaskAsked =
    | { readonly kind: 'open' | 'plan'; readonly task: PersonalTask }
    | { readonly kind: 'menu'; readonly task: PersonalTask; readonly at: MenuPoint };

function failedSaid(answer: ClientResult<unknown>): MessageKey | null {
    if (answer.outcome !== 'failed') {
        return null;
    }

    return wentUnanswered(answer.failure) ? 'tasks.writeUnknown' : 'tasks.writeFailed';
}

export function TaskSurfaces({
    session,
    transport,
    asked,
    events,
    tasks,
    writer,
    onAsk,
    onWritten,
    onOpenAnchor,
}: {
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;

    /** The task asked for and how, or `null` where none is. */
    readonly asked: TaskAsked | null;

    /** The calendar events the screen holds, which a planned task is read against for a clash. */
    readonly events: readonly CalendarEvent[];

    /** The other tasks the screen holds, which the plan dialog draws in the hours they are planned into. */
    readonly tasks: readonly PersonalTask[];

    /** The screen's writer of plans, whose plans pressed the screen draws. */
    readonly writer: PlanWriter;

    /** Asks for another surface over the same task, or for none. */
    readonly onAsk: (asked: TaskAsked | null) => void;

    /** Says what a write did, so the screen reads again, settling once that read has answered. */
    readonly onWritten: (said: MessageKey, names?: Readonly<Record<string, string>>) => Promise<void>;

    /** Opens what a task belongs to, or `undefined` where this screen reaches neither a thread nor the calendar. */
    readonly onOpenAnchor: ((anchor: TaskAnchor) => void) | undefined;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const writeRefused = useWriteRefusal();
    const [now] = useState(() => Date.now());
    const [announcing, setAnnouncing] = useState<PersonalTask | null>(null);
    const [erasing, setErasing] = useState<PersonalTask | null>(null);

    const opening = useRef<HTMLDialogElement | null>(null);
    const panel = useRef<HTMLDialogElement | null>(null);
    const erasure = useRef<HTMLDialogElement | null>(null);

    const today = calendarDayIn(now, timeZone);
    const choosing = asked !== null && asked.kind !== 'menu';

    // Every day the plan dialog offers is read while a dialog that chooses a plan is open, so a clash is drawn on
    // whichever of them is picked.
    const planningDays = useTodayCalendar(choosing ? session : null, transport, now, daysSpanned(today));

    const [pressed] = asked === null ? [] : withPlansPressed([asked.task], writer.pressed);
    const opened = asked?.kind === 'open' ? (pressed ?? null) : null;
    const planning = asked?.kind === 'plan' ? (pressed ?? null) : null;

    // The dialog is the platform's own, so opening it is an imperative call on an element that has to exist first.
    useEffect(() => {
        if (opened !== null && opening.current?.open === false) {
            opening.current.showModal();
        }
    }, [opened]);

    function toggleCompleted(task: PersonalTask): void {
        if (session === null) {
            return;
        }

        void setTaskCompletion(session, transport, task.id, !task.completed).then((answer) => {
            void onWritten(failedSaid(answer) ?? (task.completed ? 'tasks.markedNotDone' : 'tasks.markedDone'));
        });
    }

    function erase(task: PersonalTask): void {
        if (session === null) {
            return;
        }

        void eraseTask(session, transport, task.id).then((answer) => {
            if (answer.outcome !== 'failed') {
                opening.current?.close();
            }

            void onWritten(failedSaid(answer) ?? 'tasks.erased');
        });
    }

    // What announces the task, written as the panel answers it with the whole record restated beside the leads.
    function stateReminders(task: PersonalTask, reminders: readonly number[]): void {
        setAnnouncing({ ...task, reminders });

        if (session === null) {
            return;
        }

        void reviseTask(session, transport, task.id, {
            title: task.title,
            dueOn: task.dueOn,
            reminders,
            dueDayOffsetMinutes: dueDayOffsetMinutes(task.dueOn, timeZone),
            sourceMessageId: task.sourceMessageId,
            direction: task.direction,
            estimatedMinutes: task.estimatedMinutes,
            plan: task.plan,
            anchor: task.anchor,
            counterpart: task.counterpart,
        }).then((answer) => {
            void onWritten(failedSaid(answer) ?? 'tasks.remindersWritten');
        });
    }

    // A plan chosen is also how long the task is now thought to take, which is the design's.
    function planned(task: PersonalTask, chosen: TaskPlan | null): void {
        writer.plan(
            [[task, { plan: chosen, estimatedMinutes: chosen?.minutes ?? task.estimatedMinutes }]],
            (answers) => {
                const refused = answers.map(failedSaid).find((said) => said !== null);

                if (refused !== undefined) {
                    return onWritten(refused);
                }

                if (chosen === null) {
                    return onWritten('tasks.unplanned');
                }

                return onWritten('tasks.plannedOn', {
                    title: task.title,
                    day: wordTaskDay(calendarDayIn(Date.parse(chosen.startAt), timeZone), locale, now, timeZone),
                    time: wordInstant(chosen.startAt, locale, 'time', timeZone) ?? '',
                });
            },
        );
    }

    function menuOf(task: PersonalTask, at: MenuPoint) {
        const writing = (icon: ContextMenuItem['icon'], label: string, choose: () => void): ContextMenuItem => ({
            icon,
            label,
            refused: writeRefused(label),
            choose,
        });

        const items: ContextMenuItem[] = [
            {
                icon: 'open_in_full',
                label: translate('tasks.openTask'),
                choose: () => {
                    onAsk({ kind: 'open', task });
                },
            },
            writing(
                task.completed ? 'radio_button_unchecked' : 'check_circle',
                translate(task.completed ? 'tasks.markNotDone' : 'tasks.markDone'),
                () => {
                    toggleCompleted(task);
                },
            ),
        ];

        if (!task.completed) {
            items.push(
                writing('schedule', translate(task.plan === null ? 'tasks.planATime' : 'tasks.move'), () => {
                    onAsk({ kind: 'plan', task });
                }),
            );
        }

        return (
            <ContextMenu
                header={task.title}
                at={at}
                items={items}
                onClose={() => {
                    // A menu item that opens another surface has already asked for it; only a menu left asks for none.
                    if (asked?.kind === 'menu') {
                        onAsk(null);
                    }
                }}
            />
        );
    }

    return (
        <>
            {asked?.kind === 'menu' ? menuOf(asked.task, asked.at) : null}

            <Departing value={opened}>
                {(task) => (
                    <TaskDialog
                        key={task.id}
                        session={session}
                        transport={transport}
                        task={task}
                        asked={opening}
                        events={events}
                        planningEvents={planningDays.events}
                        tasks={tasks}
                        onWritten={(said) => {
                            void onWritten(said);
                        }}
                        onToggleCompleted={toggleCompleted}
                        onAskErasure={(erased) => {
                            setErasing(erased);
                            erasure.current?.showModal();
                        }}
                        onReminders={(announced) => {
                            setAnnouncing(announced);
                            panel.current?.showModal();
                        }}
                        onOpenAnchor={onOpenAnchor}
                        onLeft={() => {
                            onAsk(null);
                        }}
                    />
                )}
            </Departing>

            <Departing value={planning}>
                {(task) => (
                    <PlanDialog
                        key={task.id}
                        task={task}
                        events={planningDays.events}
                        tasks={tasks}
                        onPlan={(chosen) => {
                            planned(task, chosen);
                        }}
                        onLeft={() => {
                            onAsk(null);
                        }}
                    />
                )}
            </Departing>

            <ReminderPanel
                panel={panel}
                subject={announcing?.title ?? ''}
                anchor="taskDueDate"
                reminders={announcing?.reminders ?? []}
                onRemindersChanged={(reminders) => {
                    if (announcing !== null) {
                        stateReminders(announcing, reminders);
                    }
                }}
            />

            <Confirmation
                asked={erasure}
                mark="delete"
                question={translate('tasks.eraseQuestion.one', { count: '1' })}
                consequence={<span className="block truncate">{erasing?.title}</span>}
                reversal={{ kind: 'permanent', said: translate('tasks.erasePermanent') }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('tasks.eraseAct'),
                        manner: 'destroy',
                        run: () => {
                            if (erasing !== null) {
                                erase(erasing);
                            }
                        },
                    },
                ]}
            />
        </>
    );
}
