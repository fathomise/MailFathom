// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    readCalendarWindow,
    readOwnTasks,
    readProposedTasks,
    type CalendarEvent,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTask,
} from '@mailfathom/client-backend';
import { calendarDayIn } from '../localization/instants';
import { useSignalledChanges } from '../signals/signalledChanges';
import { readEveryPage } from '../tasks/useTasksWhere';
import type { CalendarSpan, CalendarTask } from './calendarSpan';

// One span of the calendar as a screen holds it: what is on the calendar, what the mail proposed, and whether either
// has answered yet.
//
// **One read rather than two.** The deployment publishes the two halves as one window narrowed by an argument, and the
// screen draws both at once — the views draw what is on the calendar and the column beside them draws what was
// proposed — so asking twice would be two requests for one answer and two moments for the screen to be half drawn in.
// What follows from that is stated where it shows: the proposals a reader sees are the ones falling inside the span
// they are looking at, which is the honest reading of a window and not a sidebar that quietly ignores it.
//
// **Every write is answered by reading the span again rather than by correcting what is held.** Accepting a proposal
// moves it between the two halves, amending one moves it in time, and both are the deployment's account of the
// calendar rather than this client's — so a list edited in place would be this client's opinion of a record it does
// not own.
//
// **Tasks are read beside the events, from the task list's own routes**, where the credential keeps tasks at all: the
// ones due in the span or planned into it, which the views draw beside the events, and the proposals nobody has
// answered, which the column beside the views offers whatever their day — they are the same proposals the Tasks screen
// lists. A task is drawn where its plan stands, and on its due day too where it is planned on another: a task planned
// on Friday and due on Thursday is work on Friday and a deadline on Thursday, and neither is moved to meet the other.

/**
 * What the calendar draws for the tasks read: a planned entry for every task owed and planned, and a due marker for one
 * owed, open, and due on a day it is not planned on. A wait is somebody else's work and holds no hour of the reader's.
 *
 * The planned day is read in the zone the calendar draws its days in, which is the runtime's own.
 */
export function calendarTasksOf(tasks: readonly PersonalTask[]): readonly CalendarTask[] {
    return tasks
        .filter((task) => task.direction === 'Owed' && task.origin !== 'Proposed')
        .flatMap((task): CalendarTask[] => {
            const plannedOn = task.plan === null ? null : calendarDayIn(Date.parse(task.plan.startAt), null);
            const planned: CalendarTask[] = task.plan === null ? [] : [{ task, kind: 'planned' }];
            const due: CalendarTask[] =
                task.dueOn === null || task.completed || task.dueOn === plannedOn ? [] : [{ task, kind: 'due' }];

            return [...due, ...planned];
        });
}

/** What one span of the calendar holds, and the way a screen asks for it again. */
export interface CalendarInForce {
    /** What is on the calendar in this span, earliest first. */
    readonly events: readonly CalendarEvent[];

    /** What the reader's mail proposed in this span and nobody has answered yet, earliest first. */
    readonly proposals: readonly CalendarEvent[];

    /** The tasks due in this span or planned into it, as the views draw them, or none where tasks are not read. */
    readonly tasks: readonly CalendarTask[];

    /** The tasks proposed to the reader and not answered yet, whatever their day. */
    readonly proposedTasks: readonly PersonalTask[];

    /** Whether nothing has answered yet, which is the wait the screen says it is in. */
    readonly reading: boolean;

    /** Why the span did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Why the tasks of the span did not answer, or `null` where they did or are not read. */
    readonly tasksFailure: ClientFailureReason | null;

    /**
     * Reads the span and its tasks again: the way out of a failure, and what every write is followed by. It settles once
     * both have answered, which is how a write knows the screen now draws what it did.
     */
    readonly readAgain: () => Promise<void>;
}

interface HeldSpan {
    readonly from: string;
    readonly until: string;
    readonly session: ClientSession | null;
    readonly events: readonly CalendarEvent[];
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;
}

function nothingRead(from: string, until: string, session: ClientSession | null): HeldSpan {
    return { from, until, session, events: [], reading: session !== null, failure: null };
}

interface HeldTasks {
    readonly from: string;
    readonly until: string;
    readonly session: ClientSession | null;
    readonly tasks: readonly PersonalTask[];
    readonly proposed: readonly PersonalTask[];
    readonly failure: ClientFailureReason | null;
}

const noTasks = { tasks: [], proposed: [], failure: null } as const;

/**
 * Holds one span of the signed-in person's calendar.
 *
 * @param session Who is asking and where, or `null` where there is nothing to ask with — in which case nothing is read
 * and the screen draws a calendar that never answered rather than an empty one.
 * @param transport How a request reaches the deployment.
 * @param span The days being drawn, which is the window the deployment is asked for.
 * @param readsTasks Whether this credential keeps tasks, which are read beside the events only where it does.
 */
export function useCalendarWindow(
    session: ClientSession | null,
    transport: MailFathomTransport,
    span: CalendarSpan,
    readsTasks = false,
): CalendarInForce {
    const from = span.from.toISOString();
    const until = span.until.toISOString();

    const [held, setHeld] = useState<HeldSpan>(() => nothingRead(from, until, session));
    const [heldTasks, setHeldTasks] = useState<HeldTasks>(() => ({ from, until, session, ...noTasks }));

    // Which read is the current one. A span moved or a credential replaced while a read is in flight leaves an answer
    // on its way that must not be drawn: the ordering is not guaranteed, and last week's events appearing under this
    // week's heading reads as a rendering defect rather than as a race.
    const latest = useRef(0);

    const read = useCallback(async (): Promise<void> => {
        if (session === null) {
            return;
        }

        const asked = ++latest.current;
        const answer = await readCalendarWindow(session, transport, { from, until });

        if (asked !== latest.current) {
            return;
        }

        setHeld((standing) => {
            if (answer.outcome !== 'failed') {
                return { from, until, session, events: answer.value.events, reading: false, failure: null };
            }

            // A read that failed says nothing about what was already drawn, so a span already on the screen stays
            // there behind the failure rather than emptying under it — which is the partial state § UX asks for, and
            // the case a re-read after a write is in.
            const drawn = standing.from === from && standing.until === until && standing.session === session;

            return {
                from,
                until,
                session,
                events: drawn ? standing.events : [],
                reading: false,
                failure: answer.failure.reason,
            };
        });
    }, [from, session, transport, until]);

    // The tasks of the span and the proposals, read as a pair of their own so a calendar answered is drawn whether or
    // not the task list has answered yet. Either half failing fails the pair, which the screen says on its own line.
    const latestTasks = useRef(0);

    const readTasks = useCallback(async (): Promise<void> => {
        if (session === null || !readsTasks) {
            return;
        }

        const asked = ++latestTasks.current;
        const [own, proposed] = await Promise.all([
            readEveryPage((page) => readOwnTasks(session, transport, { ...page, filter: { from, until } })),
            readEveryPage((page) => readProposedTasks(session, transport, page)),
        ]);

        if (asked !== latestTasks.current) {
            return;
        }

        setHeldTasks((standing) => {
            if (own.outcome === 'read' && proposed.outcome === 'read') {
                return { from, until, session, tasks: own.value, proposed: proposed.value, failure: null };
            }

            const drawn = standing.from === from && standing.until === until && standing.session === session;
            const failure =
                own.outcome === 'failed'
                    ? own.failure.reason
                    : proposed.outcome === 'failed'
                      ? proposed.failure.reason
                      : null;

            return { ...(drawn ? standing : { from, until, session, ...noTasks }), failure };
        });
    }, [from, session, transport, until, readsTasks]);

    // The read itself, which is a request going out and therefore the one thing an effect is for. It runs again when
    // the span moves or the credential changes; nothing is cleared here, because what is held names the span it was
    // read for and a reading for another one is derived away below.
    useEffect(() => {
        void read();
    }, [read]);

    useEffect(() => {
        void readTasks();
    }, [readTasks]);

    // A calendar written somewhere else — another device, an agent, an import, or mail proposing a date — is read again
    // in place when the deployment says so, and on every refresh, as every other screen is. Nothing is cleared first:
    // what is drawn stays until the answer replaces it.
    const signalled = useSignalledChanges();

    useEffect(
        () =>
            signalled.listen((change) => {
                if (change.kind === 'refresh' || change.kind === 'calendar.changed') {
                    void read();
                }

                if (change.kind === 'refresh' || change.kind === 'tasks.changed') {
                    void readTasks();
                }
            }),
        [signalled, read, readTasks],
    );

    // A span not answered yet keeps drawing what the last one held on the calendar for the same person, so moving
    // between views or weeks does not empty the grid for the moment the read takes and fill it again: each view draws
    // only the events falling on its own days, and the answer replaces the set as soon as it arrives. The proposals are
    // not held over, because they are listed under the new span's heading whatever their day and can be accepted there.
    const standing =
        held.from === from && held.until === until && held.session === session
            ? held
            : {
                  ...nothingRead(from, until, session),
                  events: held.session === session ? held.events.filter((event) => event.origin === 'Asserted') : [],
              };

    // The tasks are held over a span being read for the reason the events are, and the proposals with them: those are
    // not the span's, so the column offering them does not empty while the views move. Their failure is their own, so
    // a calendar that answered is not said to have failed because the task list did not.
    const tasksRead = readsTasks && heldTasks.session === session;
    const tasksOfSpan = tasksRead && heldTasks.from === from && heldTasks.until === until;

    return {
        events: standing.events.filter((event) => event.origin === 'Asserted'),
        proposals: standing.events.filter((event) => event.origin === 'Proposed'),
        tasks: tasksRead ? calendarTasksOf(heldTasks.tasks) : [],
        proposedTasks: tasksRead ? heldTasks.proposed : [],
        reading: standing.reading,
        failure: standing.failure,
        tasksFailure: tasksOfSpan ? heldTasks.failure : null,
        readAgain: async () => {
            setHeld({ ...standing, reading: true, failure: null });
            setHeldTasks((tasks) => ({ ...tasks, failure: null }));
            await Promise.all([read(), readTasks()]);
        },
    };
}
