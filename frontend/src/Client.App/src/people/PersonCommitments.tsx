// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useId } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { TaskChips } from '../tasks/TaskChips';
import { useMailTasks, type MailTasks } from '../tasks/useMailTasks';
import { useTasksWhereAny } from '../tasks/useTasksWhere';

// What the reader and one person owe each other, as the design's *Commitments* card draws it: the open tasks with them
// on two sides — what the reader owes them and what they owe the reader, which is the reader waiting for it — each
// opening its task, and *Add a task* writing one down with them. A person may write from several addresses, and a task
// is with whichever one it names, so each address is read and the answers merged. Nothing is drawn where the person
// keeps no tasks.

// How many of a person's addresses are read, one walk each; shortcut: addresses past the eighth are not read, replace the
// per-address walks with one server-side filter over all of a person's addresses when records carry more.
const mostAddressesRead = 8;

export function PersonCommitments({
    addresses,
    preferredAddress,
    displayName,
}: {
    readonly addresses: readonly string[];

    /** The address a task written down from here is with, or `null` where the record names none. */
    readonly preferredAddress: string | null;

    readonly displayName: string;
}) {
    const tasks = useMailTasks();
    const withAddress = preferredAddress ?? addresses[0];

    return tasks === null || withAddress === undefined ? null : (
        <Commitments
            tasks={tasks}
            addresses={addresses.slice(0, mostAddressesRead)}
            withAddress={withAddress}
            displayName={displayName}
        />
    );
}

function Commitments({
    tasks,
    addresses,
    withAddress,
    displayName,
}: {
    readonly tasks: MailTasks;
    readonly addresses: readonly string[];

    /** The address a task written down from here is with. */
    readonly withAddress: string;

    readonly displayName: string;
}) {
    const { translate } = useLocalization();
    const labels = useId();
    const where = useTasksWhereAny(
        tasks.session,
        tasks.transport,
        addresses.map((counterpart) => ({ counterpart })),
    );
    const readAgain = useEffectEvent(() => where.readAgain());
    const { follow } = tasks;

    // Read again after a task is written down from here or opened and changed, which is a subscription to the writes.
    useEffect(() => follow(() => readAgain()), [follow]);

    const open = where.tasks.filter((task) => !task.completed);

    // The first word of the name, as the design writes it.
    const name = displayName.trim().split(/\s+/u, 1).join('');
    const sides = [
        { label: translate('people.youOwe', { name }), none: translate('people.nothingYouOwe'), direction: 'Owed' },
        {
            label: translate('people.owesYou', { name }),
            none: translate('people.nothingYouAwait'),
            direction: 'Awaited',
        },
    ] as const;

    // A side is said to be empty only once every address answered, so neither a read under way nor a failed one reads
    // as nothing being owed.
    const answered = !where.reading && where.failure === null;

    return (
        <section className="flex min-w-0 flex-col gap-2.5 rounded-lg border border-line bg-panel px-4.25 py-3.75">
            <div className="flex items-center gap-2.25">
                <h3 className="text-xs leading-natural font-semibold text-muted">{translate('people.commitments')}</h3>
                <button
                    type="button"
                    className="ms-auto flex items-center gap-1.5 rounded-md border border-accent-line bg-accent-soft px-2.5 py-1.25 text-sm text-accent-deep hover:bg-accent hover:text-on-accent"
                    onClick={() => {
                        tasks.makeTask({ counterpart: { address: withAddress, displayName } });
                    }}
                >
                    <Icon name="add_task" className="size-icon-sm" />
                    {translate('people.addTask')}
                </button>
            </div>

            {where.failure === null ? null : (
                <p className="text-sm text-muted">{translate('people.commitmentsUnread')}</p>
            )}

            <div className="flex flex-col gap-3 roomy:flex-row roomy:*:flex-1">
                {sides.map((side) => {
                    const listed = open.filter((task) => task.direction === side.direction);

                    return (
                        <div key={side.direction} className="flex min-w-0 flex-col gap-1">
                            <p id={`${labels}-${side.direction}`} className="text-sm font-semibold text-text-soft">
                                {side.label}
                            </p>
                            {listed.length === 0 ? (
                                answered ? (
                                    <p className="text-base text-faint">{side.none}</p>
                                ) : null
                            ) : (
                                <TaskChips
                                    tasks={listed}
                                    stacked
                                    whole
                                    labelledBy={`${labels}-${side.direction}`}
                                    onOpen={(taskId) => {
                                        const task = listed.find((held) => held.id === taskId);

                                        if (task !== undefined) {
                                            tasks.openTask(task);
                                        }
                                    }}
                                />
                            )}
                        </div>
                    );
                })}
            </div>
        </section>
    );
}
