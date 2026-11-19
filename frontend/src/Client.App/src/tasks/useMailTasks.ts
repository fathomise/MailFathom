// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import type {
    ClientResult,
    ClientSession,
    MailFathomTransport,
    PersonalTask,
    PersonalTaskRecord,
} from '@mailfathom/client-backend';
import type { TaskContext } from './NewTask';
import { anchorLabel, taskLine } from './taskRecords';

// What Mail reaches of the person's tasks: the commitments a thread carries, the way to write one down about it, and
// the wait a sent message leaves. Context because the surfaces asking sit far apart — a list row, the head of a thread,
// the toolbar above it, the composer — and none of them is the parent of another. `MailTasks.tsx` provides it; it is
// absent where the person keeps no tasks, which is how each of those surfaces knows to draw nothing about them.

export interface MailTasks {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The threads an open task belongs to, which the list marks. */
    readonly threadsWithTasks: ReadonlySet<string>;

    /** Opens the new-task dialog on what it is about. */
    readonly makeTask: (about: TaskContext) => void;

    /** Opens the new-task dialog about the thread one message stands in, read first, for a surface holding only its identity. */
    readonly makeTaskOn: (storedEmailId: string) => void;

    /** Opens one task's dialog. */
    readonly openTask: (task: PersonalTask) => void;

    /** Writes a task down, and reads what Mail draws of tasks again once it has. */
    readonly record: (record: PersonalTaskRecord) => Promise<ClientResult<PersonalTask>>;

    /** Has a surface reading tasks of its own read them again after every write, until the returned function is called. */
    readonly follow: (readAgain: () => Promise<void>) => () => void;
}

export const MailTasksContext = createContext<MailTasks | null>(null);

/** Mail's reach into the person's tasks, or `null` where they keep none. */
export function useMailTasks(): MailTasks | null {
    return useContext(MailTasksContext);
}

/**
 * What a task written down from a thread starts from: it belongs to the thread, is about the person who wrote the
 * message being read, and starts with the thread's subject as its line.
 */
export function taskAboutThread(
    threadId: string,
    subject: string | null,
    sender: { readonly address: string; readonly displayName: string | null } | null,
    noSubject: string,
): TaskContext {
    return {
        anchor: { kind: 'Thread', id: threadId, label: anchorLabel(subject, noSubject) },
        title: taskLine(subject ?? ''),
        ...(sender === null ? {} : { counterpart: { address: sender.address, displayName: sender.displayName } }),
    };
}
