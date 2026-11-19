// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import {
    readMailMessage,
    recordTask,
    wentUnanswered,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTaskRecord,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useToasts } from '../toasts/useToasts';
import { NewTask, type TaskContext, type TaskDraft } from './NewTask';
import { recordOfDraft } from './taskRecords';
import { TaskSurfaces, type TaskAsked } from './TaskSurfaces';
import { usePlanWriter } from './usePlanWriter';
import { MailTasksContext, taskAboutThread, type MailTasks } from './useMailTasks';
import { useTasksWhere } from './useTasksWhere';

// Mail's reach into the person's tasks, held once above every mail surface: the new-task dialog *Make a task* opens,
// the surfaces a chip opens a task in, and the one read of open tasks that tells the list which threads carry one.
// What each write did is said in a toast, since a mail surface has no line of its own to say it in.
//
// A write made here is read again at once, so the person's own write is drawn without waiting on anything; where the
// deployment also says `tasks.changed` for it, the walk that signal starts answers the same list, and the latest answer
// is the one held, so the second walk changes nothing on the screen.

// The sentences that say a write did not land, which a toast says as a warning rather than as done.
const unwritten: ReadonlySet<MessageKey> = new Set([
    'tasks.writeFailed',
    'tasks.writeFailedBecause',
    'tasks.writeUnknown',
]);

const noFilter = {};

export function MailTasksProvider({
    session,
    transport,
    children,
}: {
    /** Who is asking, or `null` where the person keeps no tasks, which offers Mail none of this. */
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly children: ReactNode;
}) {
    const { translate } = useLocalization();
    const toasts = useToasts();
    const timeZone = useReadingZone();
    const [context, setContext] = useState<TaskContext | undefined>(undefined);
    const [asked, setAsked] = useState<TaskAsked | null>(null);
    const asking = useRef<HTMLDialogElement | null>(null);
    const sought = useRef(0);

    // The surfaces that read tasks of their own — the chips of the thread being read — and are read again after every
    // write made here. A subscription, held for the provider's life, so its identity never changes.
    const following = useRef(new Set<() => Promise<void>>());
    const [follow] = useState(() => (readAgain: () => Promise<void>) => {
        following.current.add(readAgain);

        return () => {
            following.current.delete(readAgain);
        };
    });

    // shortcut: one walk of at most 10×200 tasks (completed ones included, soonest due first) per provider, re-walked on every tasks.changed; open tasks past that lose their row mark — replace with a hasTask flag on the thread row (or an open-only filter) when task lists outgrow 2000.
    const open = useTasksWhere(session, transport, noFilter);
    const writer = usePlanWriter(session, transport, timeZone);
    const openTasks = useMemo(() => open.tasks.filter((task) => !task.completed), [open.tasks]);
    const threadsWithTasks = useMemo(
        () => new Set(openTasks.flatMap((task) => (task.anchor?.kind === 'Thread' ? [task.anchor.id] : []))),
        [openTasks],
    );

    // What the writes below reach for when they are made rather than when the value was composed, so the value every
    // row reads keeps its identity across the renders that only moved one of these.
    const latest = useRef({ readAgainOpen: open.readAgain, translate, toasts });

    useEffect(() => {
        latest.current = { readAgainOpen: open.readAgain, translate, toasts };
    });

    // Everything here that reads tasks, read again: the marks' walk and every surface following the writes.
    const [readAgain] = useState(
        () => () =>
            Promise.all([latest.current.readAgainOpen(), ...[...following.current].map((read) => read())]).then(
                () => undefined,
            ),
    );

    // The task asked for, read back out of the latest read, so its dialog draws it as it now stands.
    const askedTask: TaskAsked | null =
        asked === null ? null : { ...asked, task: open.tasks.find((task) => task.id === asked.task.id) ?? asked.task };

    const tasks = useMemo<MailTasks | null>(() => {
        if (session === null) {
            return null;
        }

        function makeTask(about: TaskContext): void {
            setContext(about);
            asking.current?.showModal();
        }

        return {
            session,
            transport,
            threadsWithTasks,
            makeTask,

            // The message is read for the thread it stands in; a later press replaces an earlier one still being read.
            makeTaskOn: (storedEmailId) => {
                const seeking = ++sought.current;

                void readMailMessage(session, transport, storedEmailId).then((answer) => {
                    if (seeking !== sought.current) {
                        return;
                    }

                    const { translate: word, toasts: saying } = latest.current;

                    if (answer.outcome === 'failed' || answer.value.threadId === null) {
                        saying.raise({ kind: 'warning', title: word('tasks.threadUnopened') });

                        return;
                    }

                    const sender = answer.value.headers.participants.find((one) => one.role === 'From') ?? null;

                    makeTask(
                        taskAboutThread(
                            answer.value.threadId,
                            answer.value.headers.subject ?? word('message.noSubject'),
                            sender,
                        ),
                    );
                });
            },
            openTask: (task) => {
                setAsked({ kind: 'open', task });
            },
            record: (stated: PersonalTaskRecord) =>
                recordTask(session, transport, stated).then(async (answer) => {
                    await readAgain();

                    return answer;
                }),
            follow,
        };
    }, [session, transport, threadsWithTasks, follow, readAgain]);

    function tell(key: MessageKey, names?: Readonly<Record<string, string>>): void {
        toasts.raise({ kind: unwritten.has(key) ? 'warning' : 'success', title: translate(key, names) });
    }

    // What a task surface wrote, said and then read again.
    function said(key: MessageKey, names?: Readonly<Record<string, string>>): Promise<void> {
        tell(key, names);

        return readAgain();
    }

    // Recording reads again on its own, so what it came to is only said.
    function recordDrafted(draft: TaskDraft): void {
        void tasks?.record(recordOfDraft(draft)).then((answer) => {
            tell(
                answer.outcome === 'read'
                    ? 'tasks.written'
                    : wentUnanswered(answer.failure)
                      ? 'tasks.writeUnknown'
                      : 'tasks.writeFailed',
            );
        });
    }

    // Absent rather than withdrawn where the person keeps no tasks, in the same tree either way, so turning tasks on or
    // off never mounts the mail below again.
    return (
        <MailTasksContext value={tasks}>
            {children}

            {session === null ? null : (
                <>
                    <NewTask asked={asking} context={context} onSave={recordDrafted} />

                    <TaskSurfaces
                        session={session}
                        transport={transport}
                        asked={askedTask}
                        events={[]}
                        tasks={openTasks}
                        writer={writer}
                        onAsk={setAsked}
                        onWritten={said}
                        onOpenAnchor={undefined}
                    />
                </>
            )}
        </MailTasksContext>
    );
}
