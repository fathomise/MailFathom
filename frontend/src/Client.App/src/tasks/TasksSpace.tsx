// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useRef, useState } from 'react';
import {
    acceptTask,
    arrangesDays,
    dueDayOffsetMinutes,
    eraseTask,
    readMailThread,
    readTask,
    recordTask,
    reviseTask,
    setTaskCompletion,
    wentUnanswered,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTask,
    type PersonalTaskErasure,
    type TaskAnchor,
    type TaskPlan,
} from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import { Control } from '../controls/Control';
import { useTabSwipe } from '../controls/useTabSwipe';
import type { MessageKey } from '../localization/en';
import { calendarDayIn, wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { ReminderPanel } from '../reminders/ReminderPanel';
import { Departing } from '../shell/Departing';
import { useCoarsePointer, useWideWorkspace } from '../shell/useWideWorkspace';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { DayCapacity } from './DayCapacity';
import { DayPlan } from './DayPlan';
import { LayoutOffer, type ProposedPlan } from './LayoutOffer';
import { NewTask, type TaskDraft } from './NewTask';
import { PlanDialog } from './PlanDialog';
import { TaskDialog } from './TaskDialog';
import { TaskList } from './TaskList';
import { TaskSelectionBar } from './TaskSelectionBar';
import { TasksHead } from './TasksHead';
import { reachedTheRowsRead } from './rowsRead';
import { recordOfDraft } from './taskRecords';
import { clashOf, openIn, openOwedToday, plannedDay, taskViews, tasksIn, type TaskView } from './taskViews';
import { daysSpanned, wordTaskDay } from './taskWording';
import { useTasks } from './useTasks';
import { useTodayCalendar } from './useTodayCalendar';
import { usePlanWriter, withPlansPressed, type PlanPress } from './usePlanWriter';

// The Tasks space as the design composes it: the head naming the view and the four views to move between, the view's
// grouped list down the middle, the day's own calendar and the day's capacity beside it, the task dialog a row opens,
// and the acts that change the list held here rather than in any of them.
//
// **What a task belongs to is opened where it lives.** A thread is read for its first message, which the Mail space
// opens with the conversation around it; an event is the calendar's, and is opened only where the frame hands over a
// way to open one.
//
// **The sidebar stands beside the list where there is room and under it where there is not.** It is never dropped: a
// reader on a phone scrolls past the list to it, which is what `frontend/src/AGENTS.md` means by content that a narrow
// width cannot show at once being reached rather than hidden.
//
// **Every write is answered by reading the list again rather than by correcting what is held.** Where a task sorts is
// the deployment's, accepting one moves it between the two halves entirely, and completing one changes what the
// capacity panel counts — so a list edited in place would be this client's own opinion of an ordering it does not own.
// What a write leaves behind instead is one sentence saying what happened.
//
// **What the toolbar carries is three acts rather than the five the design draws.** *Find in mail* reaches the Agent
// space, which is still a placeholder; *Export* has no route on this surface, exactly as exporting the address book
// has none and for the same reason. Each is left out rather than drawn as a control that would do nothing, which is a
// correction owed to the design rather than a toolbar to fill.
//
// **A task is planned into the day rather than written into the calendar.** Its plan is part of the task, so planning
// one — from its row's *Plan*, by dragging its row onto an hour of the day beside the list, or from the laid-out day's
// *Plan these* — revises the task with the whole record restated and its plan beside it, and the calendar draws it from
// there. `usePlanWriter` writes every plan pressed on this screen: one write is out per task at a time, the latest plan
// pressed for it wins, and what was pressed is drawn at once and restated by every other write until the list is read
// back. The task dialog is the other writer of a plan — its *Plan* or *Move* is queued behind its own chip writes, the
// whole record restated — so a plan reaches the deployment through one of those two and never through a third. *Lay
// out today* is an offer the deployment composes, and nothing is planned until somebody accepts it. Nothing on this
// screen writes a calendar event of its own.

// What one write said, worded as what happened rather than as an outcome name.
interface WriteSaid {
    readonly said: MessageKey;
    readonly names?: Readonly<Record<string, string>>;
}

// How many writes in a batch the deployment refused, in the forms a language has for the noun, and how many tasks a
// question is about. Both count something a person is reading, which is what `Intl.PluralRules` is for.
const refusedCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.someRefused.other',
    one: 'tasks.someRefused.one',
    two: 'tasks.someRefused.other',
    few: 'tasks.someRefused.few',
    many: 'tasks.someRefused.many',
    other: 'tasks.someRefused.other',
};

const plannedIntoToday: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.plannedIntoToday.other',
    one: 'tasks.plannedIntoToday.one',
    two: 'tasks.plannedIntoToday.other',
    few: 'tasks.plannedIntoToday.few',
    many: 'tasks.plannedIntoToday.many',
    other: 'tasks.plannedIntoToday.other',
};

// How long a task nobody said the length of is planned for, which is the design's.
const defaultPlanMinutes = 30;

const erasureQuestions: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'tasks.eraseQuestion.other',
    one: 'tasks.eraseQuestion.one',
    two: 'tasks.eraseQuestion.other',
    few: 'tasks.eraseQuestion.few',
    many: 'tasks.eraseQuestion.many',
    other: 'tasks.eraseQuestion.other',
};

// What each of the four reasons a write can fail for is called where somebody reads it, which is the table every
// screen in this client declares for itself.
const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

// What one write became. A deployment that refused names the reason it gave; a write the screen found nothing to
// perform names none, which is a different thing from a refusal and must not be worded as one.
interface TaskWrite {
    readonly failed: boolean;
    readonly reason: ClientFailureReason | null;

    /**
     * Whether nothing answered the write — or a gateway answered for the server, which proves nothing about whether it
     * was handed on. Such a write may well have landed, so it is never said to have changed nothing.
     */
    readonly unanswered: boolean;
}

function wrote(answer: ClientResult<PersonalTask | PersonalTaskErasure>): TaskWrite {
    return answer.outcome === 'failed'
        ? { failed: true, reason: answer.failure.reason, unanswered: wentUnanswered(answer.failure) }
        : { failed: false, reason: null, unanswered: false };
}

// The one reason a whole batch was refused for, or `null` where there is none to give: a batch refused two different
// ways has no single answer, and one the screen itself found nothing to write for was never refused at all.
function reasonBehind(refused: readonly TaskWrite[]): ClientFailureReason | null {
    const reasons = new Set(refused.map((answer) => answer.reason));

    if (reasons.size !== 1) {
        return null;
    }

    const [only] = reasons;

    return only ?? null;
}

/** A task another screen asked Tasks to open, by its identity; each ask is a new value. */
export interface TaskOpenAsked {
    readonly taskId: string;
}

export function TasksSpace({
    session,
    transport,
    asksDeployment,
    readsCalendar,
    onOpenMessage,
    onOpenEvent,
    openLayoutOffer = false,
    taskAsked = null,
}: {
    /** Who is asking and where, or `null` where there is nothing to ask with. */
    readonly session: ClientSession | null;

    readonly transport: MailFathomTransport;

    /** Whether this credential may ask the deployment anything, which the arranged day is reached under. */
    readonly asksDeployment: boolean;

    /**
     * Whether this credential may read the calendar, which is a record of its own beside the task list: the day beside
     * the list is read and drawn only under it.
     */
    readonly readsCalendar: boolean;

    /**
     * Opens the message a task cites, which is the Mail space's to draw and therefore the frame's to perform — or
     * `undefined` where this credential reads no mail.
     */
    readonly onOpenMessage: ((messageId: string) => void) | undefined;

    /** Opens one calendar event, which is the Calendar space's to draw, or `undefined` where the frame offers none. */
    readonly onOpenEvent?: ((eventId: string) => void) | undefined;

    /** Whether the screen opens on *Today* with the day being laid out, which is how another screen asks for it. */
    readonly openLayoutOffer?: boolean;

    /** A task another screen asked to be opened — a reminder's — or `null` where none is. */
    readonly taskAsked?: TaskOpenAsked | null;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const wide = useWideWorkspace();
    const coarse = useCoarsePointer();

    // Read once for the life of the screen, which is what `controls/ReceivedAt.tsx` does and for its reason: a heading
    // that recomputed on every render would move a task from *This week* to *Today* under a reader's cursor. Every act
    // that needs the actual moment reads it in the handler that runs, which is where a clock belongs.
    const [now] = useState(() => Date.now());

    const [view, setView] = useState<TaskView>('today');
    const [openedId, setOpenedId] = useState<string | null>(null);
    const [selected, setSelected] = useState<readonly string[]>([]);
    const [planningId, setPlanningId] = useState<string | null>(null);
    const [pressedInto, setPressedInto] = useState<ReadonlyMap<string, boolean>>(new Map());
    const [showDone, setShowDone] = useState(false);
    const [erasing, setErasing] = useState<readonly PersonalTask[]>([]);
    const [said, setSaid] = useState<WriteSaid | null>(null);
    const [arranges, setArranges] = useState<{ readonly asked: ClientSession | null; readonly answered: boolean }>({
        asked: null,
        answered: false,
    });

    // Each press of *Lay out the day* draws the offer afresh, already laying the day out; the screen opened to lay it
    // out has been pressed once, and so has a screen already standing that another one asks to lay the day out — every
    // space stays mounted while it is aside, so the ask arrives as the prop turning on rather than as a first render.
    const [layoutsAsked, setLayoutsAsked] = useState(openLayoutOffer ? 1 : 0);
    const [layoutOfferSeen, setLayoutOfferSeen] = useState(openLayoutOffer);

    if (openLayoutOffer !== layoutOfferSeen) {
        setLayoutOfferSeen(openLayoutOffer);

        if (openLayoutOffer) {
            setView('today');
            setLayoutsAsked((times) => times + 1);
        }
    }

    const [announcing, setAnnouncing] = useState<PersonalTask | null>(null);
    const [sought, setSought] = useState<PersonalTask | null>(null);

    const asked = useRef<HTMLDialogElement | null>(null);
    const asking = useRef<HTMLDialogElement | null>(null);
    const panel = useRef<HTMLDialogElement | null>(null);
    const opening = useRef<HTMLDialogElement | null>(null);

    // Which thread read is the latest one asked for; an earlier read, or one answering after the screen went, opens
    // nothing, so a slow answer cannot take the reader to a thread they have since moved away from.
    const threadAsked = useRef(0);

    useEffect(
        () => () => {
            threadAsked.current += 1;
        },
        [],
    );

    // A task another screen asked for is read and opened once per ask, which is a request going out when the ask
    // arrives — read even where the list holds it, so the dialog opens on the task as it now stands.
    const taskSought = useRef(0);
    const readAskedTask = useEffectEvent((taskId: string) => {
        if (session === null) {
            return;
        }

        const seeking = ++taskSought.current;

        void readTask(session, transport, taskId).then((answer) => {
            if (seeking !== taskSought.current) {
                return;
            }

            if (answer.outcome === 'failed') {
                setSaid({ said: answer.failure.reason === 'missing' ? 'tasks.taskGone' : 'tasks.taskUnopened' });

                return;
            }

            setSaid(null);
            setSought(answer.value);
            setOpenedId(answer.value.id);
        });
    });

    // A task the reader opens while a reminder's read is still out is the one they want, so that read opens nothing.
    function openByHand(taskId: string): void {
        taskSought.current += 1;
        setOpenedId(taskId);
    }

    useEffect(() => {
        if (taskAsked !== null) {
            readAskedTask(taskAsked.taskId);
        }

        return () => {
            taskSought.current += 1;
        };
    }, [taskAsked]);

    const reading = useTasks(session, transport);
    const day = useTodayCalendar(readsCalendar ? session : null, transport, now);
    const writer = usePlanWriter(session, transport, timeZone);
    const writeRefused = useWriteRefusal();

    // What the list holds with every plan still being written laid over it, which is what every part of the screen
    // draws and every write restates.
    const listed = withPlansPressed(reading.tasks, writer.pressed);
    const today = calendarDayIn(now, timeZone);

    // The days a plan is being chosen across, read only while a dialog that chooses one is open.
    const planningDays = useTodayCalendar(
        readsCalendar && (openedId !== null || planningId !== null) ? session : null,
        transport,
        now,
        daysSpanned(today),
    );

    // Whether this deployment arranges a day at all, which decides whether the panel offers the act rather than
    // whether the act then works. A request going out is the one thing an effect is for, and the answer is dropped
    // where the screen has stopped listening. Nothing is written here where there is nothing to ask with: what the
    // answer was asked under is kept beside it and read against the current credential below, so a screen that has
    // changed hands offers nothing until the new credential has answered for itself.
    useEffect(() => {
        if (session === null || !asksDeployment) {
            return;
        }

        let listening = true;

        void arrangesDays(session, transport).then((answer) => {
            if (listening && answer.outcome === 'read') {
                setArranges({ asked: session, answered: answer.value });
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, asksDeployment]);

    const arrangesThisDay = session !== null && asksDeployment && arranges.asked === session && arranges.answered;

    // What is done is out of the way unless somebody asks for it, which is what the design's own toolbar switch
    // decides. It is a filter over what is held rather than a second read: the surface serves one list and which of it
    // a reader wants to look at is a rendering decision.
    const shown = showDone ? listed : listed.filter((task) => !task.completed);

    // The tasks picked out, read back out of the list rather than held as records: the selection holds identities and
    // the list holds what each of them is, so a selection cannot outlive a read that no longer names one of them.
    const picked = shown.filter((task) => selected.includes(task.id));

    // The two halves the deployment walks, told apart again by where each came from, and the view's groups over them.
    const committed = listed.filter((task) => task.origin !== 'Proposed');
    const proposals = listed.filter((task) => task.origin === 'Proposed');
    const groups = tasksIn(view, committed, proposals, today, timeZone, showDone);
    const everythingDone =
        !showDone && groups.length === 0 && tasksIn(view, committed, proposals, today, timeZone, true).length > 0;

    // What the capacity panel counts: what is owed today and still open, read off the same reading the *Today* view
    // draws, so the number under the panel and the rows in that view can never disagree.
    const owedToday = openOwedToday(committed, today, timeZone);
    const dueToday = owedToday.length;
    const plannedToday = owedToday.filter((task) => plannedDay(task, timeZone) === today);
    const clashesToday = plannedToday.filter((task) => clashOf(task, day.events) !== null).length;
    const eventsToday = day.events.filter((event) => calendarDayIn(Date.parse(event.start), timeZone) === today).length;

    // The task open in its dialog, and the one being planned, read back out of the list so each draws the last read
    // with every plan pressed since. A task another screen asked for may be on no page the list read — done long ago,
    // or past the walk — so the one read for that ask stands in until the list holds it.
    const opened =
        openedId === null
            ? null
            : (listed.find((task) => task.id === openedId) ?? (sought?.id === openedId ? sought : null));
    const planningTask = planningId === null ? null : (listed.find((task) => task.id === planningId) ?? null);

    // The dialog is the platform's own, so opening it is an imperative call on an element that has to exist first —
    // which is the other thing an effect is for. A dialog already open is never opened again: `showModal` on one
    // throws rather than doing nothing.
    useEffect(() => {
        if (openedId !== null && opening.current?.open === false) {
            opening.current.showModal();
        }
    }, [openedId]);

    function chooseView(chosen: TaskView): void {
        setSelected([]);
        setView(chosen);
    }

    // A swipe across the list walks the views in the order the head draws them, as the calendar's does.
    const viewSwipe = useTabSwipe(taskViews, view, chooseView);

    // How what a task belongs to is opened from here, or `undefined` where this credential cannot reach it.
    function anchorOpener(anchor: TaskAnchor): (() => void) | undefined {
        if (anchor.kind === 'Event') {
            return onOpenEvent === undefined
                ? undefined
                : () => {
                      onOpenEvent(anchor.id);
                  };
        }

        return session === null || onOpenMessage === undefined
            ? undefined
            : () => {
                  threadAsked.current += 1;

                  const asking = threadAsked.current;

                  void readMailThread(session, transport, anchor.id, null).then((answer) => {
                      const first = answer.outcome === 'read' ? answer.value.messages[0] : undefined;

                      if (asking !== threadAsked.current) {
                          return;
                      }

                      if (first === undefined) {
                          setSaid({ said: 'tasks.threadUnopened' });
                      } else {
                          onOpenMessage(first.email.id);
                      }
                  });
              };
    }

    // What a batch of writes left behind, as one of four sentences rather than as a success and a generic failure:
    // every one went through, some of them did, every one was refused for one reason a person can act on, or every
    // one was refused with no single reason to name.
    function saidAbout(
        refused: readonly TaskWrite[],
        asked: number,
        done: MessageKey,
        names?: Readonly<Record<string, string>>,
    ): WriteSaid {
        if (refused.length === 0) {
            return names === undefined ? { said: done } : { said: done, names };
        }

        // A write nobody answered is not known to have failed, and the list read again after it is what says where it
        // stands — so the sentence says that much and claims nothing either way, however the rest of the batch went.
        if (refused.some((answer) => answer.unanswered)) {
            return { said: 'tasks.writeUnknown' };
        }

        if (refused.length < asked) {
            return {
                said: refusedCounted[new Intl.PluralRules(locale).select(refused.length)],
                names: { count: new Intl.NumberFormat(locale).format(refused.length) },
            };
        }

        const reason = reasonBehind(refused);

        return reason === null
            ? { said: 'tasks.writeFailed' }
            : { said: 'tasks.writeFailedBecause', names: { reason: translate(failureLabels[reason]) } };
    }

    function after(
        answers: readonly TaskWrite[],
        done: MessageKey,
        names?: Readonly<Record<string, string>>,
    ): Promise<void> {
        setSaid(
            saidAbout(
                answers.filter((answer) => answer.failed),
                answers.length,
                done,
                names,
            ),
        );

        return reading.readAgain();
    }

    // A box stays where it was pressed until the list read after the write has answered. The list is the deployment's
    // and is not corrected here, so without this the box would spring back the moment it was pressed and stand wrong for
    // as long as the write and the read take — which is a press that looks as if it did nothing. The selection bar's
    // *Mark as done* is the same press over several rows, and holds each of them the same way.
    function hold(tasks: readonly PersonalTask[], completed: boolean): void {
        setPressedInto((held) => {
            const pressed = new Map(held);

            for (const task of tasks) {
                pressed.set(task.id, completed);
            }

            return pressed;
        });
    }

    // Lets go of the rows once the read after their write has answered, leaving any a later press has since moved.
    function letGo(tasks: readonly PersonalTask[], completed: boolean): void {
        setPressedInto((held) => {
            const left = new Map(held);

            for (const task of tasks) {
                if (left.get(task.id) === completed) {
                    left.delete(task.id);
                }
            }

            return left;
        });
    }

    function toggleCompleted(task: PersonalTask): void {
        if (session === null) {
            return;
        }

        const completed = !(pressedInto.get(task.id) ?? task.completed);

        hold([task], completed);

        void setTaskCompletion(session, transport, task.id, completed)
            .then((answer) => after([wrote(answer)], completed ? 'tasks.markedDone' : 'tasks.markedNotDone'))
            .then(() => {
                letGo([task], completed);
            });
    }

    function complete(tasks: readonly PersonalTask[]): void {
        if (session === null || tasks.length === 0) {
            return;
        }

        hold(tasks, true);

        void Promise.all(tasks.map((task) => setTaskCompletion(session, transport, task.id, true).then(wrote)))
            .then((answers) => {
                setSelected([]);

                return after(answers, 'tasks.markedDone');
            })
            .then(() => {
                letGo(tasks, true);
            });
    }

    function accept(task: PersonalTask): void {
        if (session === null) {
            return;
        }

        void acceptTask(session, transport, task.id).then((answer) => {
            void after([wrote(answer)], 'tasks.accepted');
        });
    }

    // Erasing several tasks is several writes rather than one, so the answers are counted rather than reduced to
    // whether any of them failed: a batch in which one refusal stood beside four erasures would otherwise say nothing
    // was changed while the list came back four rows shorter.
    function erase(tasks: readonly PersonalTask[], done: MessageKey = 'tasks.erased'): void {
        if (session === null || tasks.length === 0) {
            return;
        }

        void Promise.all(tasks.map((task) => eraseTask(session, transport, task.id).then(wrote))).then((answers) => {
            setSelected([]);
            setErasing([]);
            void after(answers, done);
        });
    }

    // Plans each task named, or takes it out of the calendar, saying what happened once every plan pressed is written.
    function plan(plans: readonly PlanPress[], done: MessageKey, names?: Readonly<Record<string, string>>): void {
        setSaid(null);
        writer.plan(plans, (answers) => after(answers.map(wrote), done, names));
    }

    // A task dropped on an hour is planned from it for as long as it is thought to take, or half an hour.
    function dropped(taskId: string, startAt: string): void {
        const task = listed.find((held) => held.id === taskId);

        if (task !== undefined) {
            const minutes = task.estimatedMinutes ?? defaultPlanMinutes;

            plan([[task, { plan: { startAt, minutes }, estimatedMinutes: task.estimatedMinutes }]], 'tasks.plannedAt', {
                title: task.title,
                time: wordInstant(startAt, locale, 'time', timeZone) ?? '',
            });
        }
    }

    // A plan chosen in the plan dialog is also how long the task is now thought to take, which is the design's.
    function planned(task: PersonalTask, chosen: TaskPlan | null): void {
        if (chosen === null) {
            plan([[task, { plan: null, estimatedMinutes: task.estimatedMinutes }]], 'tasks.unplanned');

            return;
        }

        plan([[task, { plan: chosen, estimatedMinutes: chosen.minutes }]], 'tasks.plannedOn', {
            title: task.title,
            day: wordTaskDay(calendarDayIn(Date.parse(chosen.startAt), timeZone), locale, now, timeZone),
            time: wordInstant(chosen.startAt, locale, 'time', timeZone) ?? '',
        });
    }

    function laidOut(plans: readonly ProposedPlan[]): void {
        plan(
            plans.map(([task, placed]) => [task, { plan: placed, estimatedMinutes: task.estimatedMinutes }]),
            plannedIntoToday[new Intl.PluralRules(locale).select(plans.length)],
            { count: new Intl.NumberFormat(locale).format(plans.length) },
        );
    }

    // What announces one task, written as the panel answers it: there is no *Save* behind it, exactly as there is none
    // behind the calendar's, so what the reader saw on leaving it is what the deployment then holds. The route takes
    // the whole record, so the line, the day, the citation and the commitment (its direction, estimate, plan, anchor
    // and counterpart) are restated unchanged beside the leads — and the offset travels with them because this
    // deployment keeps no timezone for anybody.
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
            void after([wrote(answer)], 'tasks.remindersWritten');
        });
    }

    function record(draft: TaskDraft): void {
        if (session === null) {
            return;
        }

        void recordTask(session, transport, recordOfDraft(draft)).then((answer) => {
            void after([wrote(answer)], 'tasks.written');
        });
    }

    function askErasure(tasks: readonly PersonalTask[]): void {
        if (tasks.length === 0) {
            return;
        }

        setErasing(tasks);
        asked.current?.showModal();
    }

    return (
        <div className="relative flex min-h-0 flex-1 flex-col">
            {selected.length > 0 ? (
                <TaskSelectionBar
                    selected={picked}
                    onClear={() => {
                        setSelected([]);
                    }}
                    onComplete={() => {
                        complete(picked);
                    }}
                    onAskErasure={() => {
                        askErasure(picked);
                    }}
                />
            ) : (
                <div className="flex shrink-0 flex-wrap items-center gap-2.25 border-b border-line bg-panel px-3 py-2">
                    {wide ? (
                        <Control
                            label={translate('tasks.newTask')}
                            icon="add"
                            shape="primary"
                            onPress={() => {
                                setSaid(null);
                                asking.current?.showModal();
                            }}
                        />
                    ) : null}

                    {readsCalendar && arrangesThisDay ? (
                        <Control
                            label={translate('tasks.layOutTheDay')}
                            icon="event_available"
                            shape="labelled"
                            onPress={() => {
                                setSaid(null);
                                chooseView('today');
                                setLayoutsAsked((asked) => asked + 1);
                            }}
                        />
                    ) : null}

                    <Control
                        label={translate('tasks.showDone')}
                        icon="check_circle"
                        shape={showDone ? 'selected' : 'labelled'}
                        pressed={showDone}
                        onPress={() => {
                            setShowDone(!showDone);
                        }}
                    />
                </div>
            )}

            <TasksHead
                view={view}
                open={openIn(committed, proposals, today, timeZone)}
                planned={plannedToday.length}
                clashes={clashesToday}
                now={now}
                onView={chooseView}
            />

            {said === null ? null : (
                <p role="status" className="shrink-0 border-b border-line bg-sunken px-4 py-2 text-sm text-muted">
                    {translate(said.said, said.names)}
                </p>
            )}

            <div
                className="flex min-h-0 flex-1 touch-pan-y touch-pinch-zoom flex-col overflow-y-auto desktop:flex-row desktop:overflow-visible"
                {...viewSwipe}
                onScroll={(event) => {
                    if (reachedTheRowsRead(event.currentTarget)) {
                        reading.readMore();
                    }
                }}
            >
                <TaskList
                    groups={groups}
                    view={view}
                    events={day.events}
                    reading={reading.reading}
                    paging={reading.paging}
                    failure={reading.failure}
                    everythingDone={everythingDone}
                    now={now}
                    selected={selected}
                    pressedInto={pressedInto}
                    onSelected={setSelected}
                    onToggleCompleted={toggleCompleted}
                    onOpen={(task) => {
                        setSaid(null);
                        openByHand(task.id);
                    }}
                    anchorOpener={anchorOpener}
                    onAccept={accept}
                    onOpenSource={onOpenMessage}
                    onReminders={(task) => {
                        setSaid(null);
                        setAnnouncing(task);
                        panel.current?.showModal();
                    }}
                    onPlan={(task) => {
                        setSaid(null);
                        setPlanningId(task.id);
                    }}
                    dragsToPlan={readsCalendar && !coarse && writeRefused(translate('tasks.plan')) === null}
                    onAskErasure={askErasure}
                    onDismiss={(dismissed) => {
                        setSaid(null);
                        erase([dismissed], 'tasks.dismissed');
                    }}
                    onReadMore={reading.readMore}
                    onReadAgain={() => {
                        void reading.readAgain();
                    }}
                />

                {/* Beside the list where there is room; above it, in *Today* only, where there is not — the day being
                    what a narrow screen plans against first. It is the calendar's day, so a credential that reads no
                    calendar meets the list alone. */}
                {!readsCalendar ? null : (
                    <aside
                        className={`order-first mx-4 mt-4 shrink-0 flex-col gap-3.5 rounded-lg border border-line bg-sunken p-3.5 desktop:order-none desktop:m-0 desktop:flex desktop:w-tasks-side desktop:overflow-y-auto desktop:rounded-none desktop:border-y-0 desktop:border-e-0 desktop:px-5 desktop:py-5 ${
                            view === 'today' ? 'flex' : 'hidden'
                        }`}
                    >
                        <DayPlan
                            now={now}
                            day={day}
                            tasks={committed}
                            onDrop={dropped}
                            onOpenTask={(task) => {
                                setSaid(null);
                                openByHand(task.id);
                            }}
                            onOpenEvent={onOpenEvent}
                        />

                        <DayCapacity dueToday={dueToday} eventsToday={eventsToday}>
                            <LayoutOffer
                                key={layoutsAsked}
                                session={session}
                                transport={transport}
                                offered={arrangesThisDay}
                                tasks={listed}
                                events={day.events}
                                startsArranging={layoutsAsked > 0 && asksDeployment}
                                onPlan={laidOut}
                            />
                        </DayCapacity>
                    </aside>
                )}
            </div>

            {/* The narrow composition's own way to write a task down, which is where a thumb reaches rather than at the
                top of a column: the toolbar the wide shape carries has nowhere to stand once bottom navigation has the
                foot of the window. */}
            {!wide && selected.length === 0 ? (
                <Control
                    label={translate('tasks.newTask')}
                    icon="add_task"
                    shape="floating"
                    className="absolute end-4 bottom-4"
                    onPress={() => {
                        setSaid(null);
                        asking.current?.showModal();
                    }}
                />
            ) : null}

            <NewTask asked={asking} onSave={record} />

            <Departing value={opened}>
                {(task) => (
                    <TaskDialog
                        key={task.id}
                        session={session}
                        transport={transport}
                        task={task}
                        asked={opening}
                        events={day.events}
                        planningEvents={planningDays.events}
                        tasks={committed}
                        onWritten={(written) => {
                            setSaid({ said: written });
                            void reading.readAgain();
                        }}
                        onToggleCompleted={toggleCompleted}
                        onAskErasure={(erased) => {
                            askErasure([erased]);
                        }}
                        onReminders={(announced) => {
                            setAnnouncing(announced);
                            panel.current?.showModal();
                        }}
                        onOpenAnchor={(anchor) => {
                            anchorOpener(anchor)?.();
                        }}
                        onLeft={() => {
                            setOpenedId(null);
                        }}
                    />
                )}
            </Departing>

            <Departing value={planningTask}>
                {(task) => (
                    <PlanDialog
                        key={task.id}
                        task={task}
                        events={planningDays.events}
                        tasks={committed}
                        onPlan={(chosen) => {
                            planned(task, chosen);
                        }}
                        onLeft={() => {
                            setPlanningId(null);
                        }}
                    />
                )}
            </Departing>

            {/* One panel for the whole screen rather than one per row, which is what lets the element itself hold
                whether it is open: the task it is about is what the row that opened it put here. It is mounted from
                the start rather than with that task, because the act that names the task is also the act that opens
                the panel — a panel mounted by the same commit would not exist yet to be opened. */}
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
                asked={asked}
                mark="delete"
                question={translate(erasureQuestions[new Intl.PluralRules(locale).select(erasing.length)], {
                    count: new Intl.NumberFormat(locale).format(erasing.length),
                })}
                consequence={
                    <>
                        {erasing.map((task) => (
                            <span key={task.id} className="block truncate">
                                {task.title}
                            </span>
                        ))}
                    </>
                }
                reversal={{ kind: 'permanent', said: translate('tasks.erasePermanent') }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('tasks.eraseAct'),
                        manner: 'destroy',
                        run: () => {
                            erase(erasing);
                        },
                    },
                ]}
            />
        </div>
    );
}
