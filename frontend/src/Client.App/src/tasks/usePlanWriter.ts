// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, useState } from 'react';
import {
    dueDayOffsetMinutes,
    reviseTask,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type PersonalTask,
    type TaskPlan,
} from '@mailfathom/client-backend';

// The writer of a plan pressed outside the task dialog — on the Tasks screen, and over a task opened from another
// screen. The task dialog's own chips are the other writer, queuing its *Plan* or *Move* behind its other writes. A plan
// is written as the whole record with the plan beside it, so two writes restating what each read would erase each
// other's plan: this one keeps a single write out per task, writes the latest plan pressed once that one answers — with
// the task as that press read it — and holds every plan pressed over the list until the list read after the writes has
// answered, so a row, the day beside the list, and a dialog opened meanwhile all draw and restate the plan just pressed
// rather than the one last read.

/** What planning a task presses: where it stands in the day, and how long it is now thought to take. */
export interface PressedPlan {
    readonly plan: TaskPlan | null;
    readonly estimatedMinutes: number | null;
}

/** What one task and the plan pressed for it are. */
export type PlanPress = readonly [PersonalTask, PressedPlan];

export interface PlanWriter {
    /** The plan pressed for each task that the list has not yet been read back with. */
    readonly pressed: ReadonlyMap<string, PressedPlan>;

    /**
     * Writes each plan, and hands every answer since nothing was last out to `settled` once nothing is — the latest
     * press's `settled` being the one that says what happened, so a sentence names the plan that was written last.
     */
    readonly plan: (
        plans: readonly PlanPress[],
        settled: (answers: readonly ClientResult<PersonalTask>[]) => Promise<void>,
    ) => void;
}

/**
 * Lays the plans pressed over the tasks read.
 *
 * @param tasks What the list holds.
 * @param pressed What {@link PlanWriter.pressed} holds.
 * @returns The tasks as they stand once every plan pressed is written.
 */
export function withPlansPressed(
    tasks: readonly PersonalTask[],
    pressed: ReadonlyMap<string, PressedPlan>,
): readonly PersonalTask[] {
    return pressed.size === 0 ? tasks : tasks.map((task) => ({ ...task, ...pressed.get(task.id) }));
}

/**
 * Holds the plans pressed on the Tasks screen and writes them one task at a time.
 *
 * @param session Who is asking and where, or `null` where nothing is written.
 * @param transport How a request reaches the deployment.
 * @param timeZone The zone the reader reads in, which the due day's offset is stated in, or `null` for the runtime's.
 * @returns The plans pressed, and the way to press more.
 */
export function usePlanWriter(
    session: ClientSession | null,
    transport: MailFathomTransport,
    timeZone: string | null,
): PlanWriter {
    const [pressed, setPressed] = useState<ReadonlyMap<string, PressedPlan>>(new Map());

    // The latest press for each task whose write is out, every answer since nothing was out, and what says so.
    const out = useRef(new Map<string, PlanPress>());
    const answers = useRef<ClientResult<PersonalTask>[]>([]);
    const saying = useRef<((answers: readonly ClientResult<PersonalTask>[]) => Promise<void>) | null>(null);

    function write(asking: ClientSession, press: PlanPress): Promise<void> {
        const [task, written] = press;

        return reviseTask(asking, transport, task.id, {
            title: task.title,
            dueOn: task.dueOn,
            reminders: task.reminders,
            dueDayOffsetMinutes: dueDayOffsetMinutes(task.dueOn, timeZone),
            sourceMessageId: task.sourceMessageId,
            direction: task.direction,
            estimatedMinutes: written.estimatedMinutes,
            plan: written.plan,
            anchor: task.anchor,
            counterpart: task.counterpart,
        }).then(async (answer) => {
            const latest = out.current.get(task.id);

            if (answer.outcome === 'read' && latest !== undefined && latest !== press) {
                return write(asking, latest);
            }

            out.current.delete(task.id);
            answers.current.push(answer);

            if (out.current.size > 0) {
                return;
            }

            const settled = answers.current;
            const say = saying.current;

            answers.current = [];
            saying.current = null;

            await say?.(settled);

            // What a read after the writes answered stands on its own; a plan pressed since is still out and kept.
            setPressed((held) => new Map([...held].filter(([id]) => out.current.has(id))));
        });
    }

    return {
        pressed,
        plan: (plans, settled) => {
            if (session === null || plans.length === 0) {
                return;
            }

            saying.current = settled;

            setPressed((held) => new Map([...held, ...plans.map(([task, plan]) => [task.id, plan] as const)]));

            for (const press of plans) {
                const writing = out.current.has(press[0].id);

                out.current.set(press[0].id, press);

                if (!writing) {
                    void write(session, press);
                }
            }
        },
    };
}
