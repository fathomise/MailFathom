// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    mostTasksPerPage,
    readOwnTasks,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTask,
    type PersonalTaskPage,
    type TaskFilter,
} from '@mailfathom/client-backend';
import { useSignalledChanges } from '../signals/signalledChanges';

// The tasks one thing carries — an event, a thread, a person — read for the surface that shows them beside it rather
// than for the Tasks screen. What is held names the credential and the filter it was read for, so a reading for
// another one is derived away in the render that observes it, and an answer to a question since replaced is dropped.

/** The tasks one filter names, and the way a surface asks for them again. */
export interface TasksWhere {
    readonly tasks: readonly PersonalTask[];

    /** Whether nothing has answered yet. */
    readonly reading: boolean;

    /** Why the tasks did not answer, or `null` where they did. */
    readonly failure: ClientFailureReason | null;

    /** Reads them again: the way out of a failure, and what a write is followed by. It settles once they answered. */
    readonly readAgain: () => Promise<void>;
}

// How many pages one filter is walked across. A filter names one thing, so a walk this long is an answer that was
// never one thing's; shortcut: tasks past the tenth page of 200 are not drawn, page on demand if anything carries more.
const mostPagesWalked = 10;

/**
 * Walks every page one half of the list answers, up to a bound.
 *
 * @param read Reads the page a cursor names, the leading one for `null`.
 * @returns Every task the pages held, or why one of them failed.
 */
export async function readEveryPage(
    read: (page: {
        readonly pageSize: number;
        readonly cursor: string | null;
    }) => Promise<ClientResult<PersonalTaskPage>>,
): Promise<ClientResult<readonly PersonalTask[]>> {
    const tasks: PersonalTask[] = [];
    let cursor: string | null = null;

    for (let walked = 0; walked < mostPagesWalked; walked += 1) {
        const answer: ClientResult<PersonalTaskPage> = await read({ pageSize: mostTasksPerPage, cursor });

        if (answer.outcome === 'failed') {
            return answer;
        }

        tasks.push(...answer.value.tasks);
        cursor = answer.value.nextCursor;

        if (cursor === null) {
            break;
        }
    }

    return { outcome: 'read', value: tasks };
}

// Several lists as one, each task once and where it was first named, so the walk named first wins.
function once(lists: readonly (readonly PersonalTask[])[]): readonly PersonalTask[] {
    const tasks = new Map<string, PersonalTask>();

    for (const task of lists.flat()) {
        if (!tasks.has(task.id)) {
            tasks.set(task.id, task);
        }
    }

    return [...tasks.values()];
}

interface HeldWhere {
    readonly session: ClientSession | null;
    readonly asked: string;
    readonly tasks: readonly PersonalTask[];
    readonly reading: boolean;
    readonly failure: ClientFailureReason | null;
}

function nothingRead(session: ClientSession | null, asked: string): HeldWhere {
    return { session, asked, tasks: [], reading: session !== null, failure: null };
}

/**
 * Holds the person's own tasks one filter names, every page of them.
 *
 * @param session Who is asking and where, or `null` where nothing is read.
 * @param transport How a request reaches the deployment.
 * @param filter What narrows the list: the thread, the event, or the person the tasks belong to.
 */
export function useTasksWhere(
    session: ClientSession | null,
    transport: MailFathomTransport,
    filter: TaskFilter,
): TasksWhere {
    return useTasksWhereAny(session, transport, [filter]);
}

/**
 * Holds the person's own tasks any of several filters names, each walked and the answers merged, a task named by two
 * of them held once — a person with several addresses being the one thing read this way.
 *
 * @param session Who is asking and where, or `null` where nothing is read.
 * @param transport How a request reaches the deployment.
 * @param filters What narrows the list, one walk each.
 */
export function useTasksWhereAny(
    session: ClientSession | null,
    transport: MailFathomTransport,
    filters: readonly TaskFilter[],
): TasksWhere {
    // The filters are held by what they say rather than by identity, since a caller states them afresh on every
    // render: filters saying something new replace the ones held, and only that is a new question.
    const asked = JSON.stringify(filters);
    const [stated, setStated] = useState(filters);

    if (JSON.stringify(stated) !== asked) {
        setStated(filters);
    }

    const [held, setHeld] = useState<HeldWhere>(() => nothingRead(session, asked));
    const latest = useRef(0);

    const read = useCallback(async (): Promise<void> => {
        if (session === null) {
            return;
        }

        const asking = ++latest.current;
        const answers = await Promise.all(
            stated.map((filter) => readEveryPage((page) => readOwnTasks(session, transport, { ...page, filter }))),
        );

        if (asking !== latest.current) {
            return;
        }

        const askedFor = JSON.stringify(stated);
        const answered = answers.flatMap((answer) => (answer.outcome === 'read' ? [answer.value] : []));
        const refused = answers.find((answer) => answer.outcome === 'failed');
        const failure = refused?.outcome === 'failed' ? refused.failure.reason : null;

        // A walk that failed leaves what it answered last standing beside what the others answer now, and the failure
        // is said beside them: a task it held may since have gone, which is what saying so is for.
        setHeld((standing) => ({
            session,
            asked: askedFor,
            tasks:
                failure === null
                    ? once(answered)
                    : once([
                          ...answered,
                          standing.session === session && standing.asked === askedFor ? standing.tasks : [],
                      ]),
            reading: false,
            failure,
        }));
    }, [session, transport, stated]);

    useEffect(() => {
        void read();
    }, [read]);

    const signalled = useSignalledChanges();

    useEffect(
        () =>
            signalled.listen((change) => {
                if (change.kind === 'refresh' || change.kind === 'tasks.changed') {
                    void read();
                }
            }),
        [signalled, read],
    );

    const standing = held.session === session && held.asked === asked ? held : nothingRead(session, asked);

    return {
        tasks: standing.tasks,
        reading: standing.reading,
        failure: standing.failure,
        readAgain: async () => {
            // With nothing to ask with, nothing is read, so nothing is said to be waiting either.
            if (session !== null) {
                setHeld({ ...standing, reading: true, failure: null });
                await read();
            }
        },
    };
}
