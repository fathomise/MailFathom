// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, useState, type ReactNode } from 'react';
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
    // write made here, which no signal from the deployment may be there to say. A subscription, held for the provider's
    // life, so its identity never changes.
    const following = useRef(new Set<() => Promise<void>>());
    const [follow] = useState(() => (readAgain: () => Promise<void>) => {
        following.current.add(readAgain);

        return () => {
            following.current.delete(readAgain);
        };
    });

    // shortcut: one unfiltered read of open tasks per list render, replace with a hasTask flag on the thread row when lists outgrow 200 open tasks
    const open = useTasksWhere(session, transport, noFilter);
    const writer = usePlanWriter(session, transport, timeZone);
    const openTasks = open.tasks.filter((task) => !task.completed);
    const threadsWithTasks = new Set(
        openTasks.flatMap((task) => (task.anchor?.kind === 'Thread' ? [task.anchor.id] : [])),
    );

    // The task asked for, read back out of the latest read, so its dialog draws it as it now stands.
    const askedTask: TaskAsked | null =
        asked === null ? null : { ...asked, task: open.tasks.find((task) => task.id === asked.task.id) ?? asked.task };

    function readAgain(): Promise<void> {
        return Promise.all([open.readAgain(), ...[...following.current].map((read) => read())]).then(() => undefined);
    }

    function makeTask(about: TaskContext): void {
        setContext(about);
        asking.current?.showModal();
    }

    function said(key: MessageKey, names?: Readonly<Record<string, string>>): Promise<void> {
        toasts.raise({ kind: unwritten.has(key) ? 'warning' : 'success', title: translate(key, names) });

        return readAgain();
    }

    function record(asking: ClientSession, stated: PersonalTaskRecord) {
        return recordTask(asking, transport, stated).then(async (answer) => {
            await readAgain();

            return answer;
        });
    }

    function recordDrafted(draft: TaskDraft): void {
        if (session === null) {
            return;
        }

        void record(session, recordOfDraft(draft)).then((answer) =>
            said(
                answer.outcome === 'read'
                    ? 'tasks.written'
                    : wentUnanswered(answer.failure)
                      ? 'tasks.writeUnknown'
                      : 'tasks.writeFailed',
            ),
        );
    }

    // A surface holding only the message's identity reads it for the thread it stands in; a later press replaces an
    // earlier one still being read.
    function makeTaskOn(asking: ClientSession, storedEmailId: string): void {
        const seeking = ++sought.current;

        void readMailMessage(asking, transport, storedEmailId).then((answer) => {
            if (seeking !== sought.current) {
                return;
            }

            if (answer.outcome === 'failed' || answer.value.threadId === null) {
                toasts.raise({ kind: 'warning', title: translate('tasks.threadUnopened') });

                return;
            }

            const sender = answer.value.headers.participants.find((one) => one.role === 'From') ?? null;

            makeTask(
                taskAboutThread(
                    answer.value.threadId,
                    answer.value.headers.subject ?? translate('message.noSubject'),
                    sender,
                ),
            );
        });
    }

    // Absent rather than withdrawn where the person keeps no tasks, in the same tree either way, so turning tasks on or
    // off never mounts the mail below again.
    const tasks: MailTasks | null =
        session === null
            ? null
            : {
                  session,
                  transport,
                  threadsWithTasks,
                  makeTask,
                  makeTaskOn: (storedEmailId) => {
                      makeTaskOn(session, storedEmailId);
                  },
                  openTask: (task) => {
                      setAsked({ kind: 'open', task });
                  },
                  record: (stated) => record(session, stated),
                  follow,
              };

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
