// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, useRef, useState } from 'react';
import type { CalendarEvent, ClientFailureReason, PersonalTask, TaskAnchor } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { onlySelected, withToggled } from '../contextMenu/rowSelection';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { TaskRow } from './TaskRow';
import { TaskRowMenu } from './TaskRowMenu';
import { reachedTheRowsRead } from './rowsRead';
import { clashOf, type TaskGroup, type TaskGroupName, type TaskView } from './taskViews';
import { Departing } from '../shell/Departing';

// The list the design draws down the middle of the Tasks screen: the groups of the view being shown, each heading
// carrying how much stands under it, and the view's own empty state where nothing does.
//
// **A page is asked for from the scroll that reaches the end of what is read**, which is the design's own
// scrolling rather than a control at the foot. The wait for it is drawn under the rows instead of replacing them,
// because a reader who has scrolled to the end of what is read has not left what they were reading.
//
// Every row read so far is in the document. The two halves page fifty at a time and the headings are runs
// of one ordered walk, so windowing this means a window over headings and rows of two different heights rather than
// the one height `messageRows/rowWindow.ts` is arithmetic over. Window it when a list somebody actually holds passes
// the two hundred rows `frontend/src/AGENTS.md` names — a task list is tens of rows where a mailbox is hundreds of
// thousands, which is why the mail list was windowed on the day it was written and this is not.

const groupLabels: Readonly<Record<TaskGroupName, MessageKey>> = {
    planned: 'tasks.groupPlanned',
    dueNotPlanned: 'tasks.groupDueNotPlanned',
    followUp: 'tasks.groupFollowUp',
    tomorrow: 'tasks.groupTomorrow',
    thisWeek: 'tasks.groupThisWeek',
    later: 'tasks.groupLater',
    noDay: 'tasks.groupNoDay',
    followUpToday: 'tasks.groupFollowUpToday',
    waiting: 'tasks.groupWaiting',
    closedByReply: 'tasks.groupClosedByReply',
    received: 'tasks.groupReceived',
    today: 'tasks.groupToday',
};

// Each view says why it is empty in its own terms, as the design draws it.
const viewEmpties: Readonly<Record<TaskView, { readonly title: MessageKey; readonly text: MessageKey }>> = {
    today: { title: 'tasks.emptyTodayTitle', text: 'tasks.emptyTodayText' },
    upcoming: { title: 'tasks.emptyUpcomingTitle', text: 'tasks.emptyUpcomingText' },
    waiting: { title: 'tasks.emptyWaitingTitle', text: 'tasks.emptyWaitingText' },
    proposed: { title: 'tasks.emptyProposedTitle', text: 'tasks.emptyProposedText' },
};

// Why the list did not answer, each said as what it is with a next step rather than as a status code. A task the
// deployment no longer holds is not one of the five a paged read produces, so it is worded as the deployment not
// having answered — which is what a reader would do about it either way.
const listFailures: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'tasks.failedUnauthenticated',
    unauthorized: 'tasks.failedUnauthorized',
    unavailable: 'tasks.failedUnavailable',
    unreadable: 'tasks.failedUnreadable',
    missing: 'tasks.failedUnavailable',
};

const nothingPressed: ReadonlyMap<string, boolean> = new Map();

export function TaskList({
    groups,
    view,
    events = [],
    reading,
    paging,
    failure,
    everythingDone,
    now,
    selected,
    scheduled,
    pressedInto = nothingPressed,
    onSelected,
    onToggleCompleted,
    onOpen,
    anchorOpener,
    onAccept,
    onOpenSource,
    onReminders,
    onSchedule,
    onAskErasure,
    onReadMore,
    onReadAgain,
}: {
    /** What to draw: the groups of the view being shown, narrowed by whatever the toolbar is filtering out. */
    readonly groups: readonly TaskGroup[];

    /** Which view the groups are of, which is what the empty state is worded for. */
    readonly view: TaskView;

    /** The day's calendar events, which a planned task is read against for a clash. */
    readonly events?: readonly CalendarEvent[];

    /** Whether nothing has answered yet, which is the wait the list says it is in. */
    readonly reading: boolean;

    /** Whether a further page is on its way, which is the wait drawn at the foot of the rows already there. */
    readonly paging: boolean;

    /** Why the list did not answer, or `null` where it did. */
    readonly failure: ClientFailureReason | null;

    /** Whether the list holds work that is all done and being filtered out, which is a different empty from no work. */
    readonly everythingDone: boolean;

    /** When the reader is reading, which decides which heading each task stands under. */
    readonly now: number;

    /** Which tasks are picked out, in the order this list draws them. The selection is this list's own. */
    readonly selected: readonly string[];

    /** Which tasks this screen has already put in the day. */
    readonly scheduled: readonly string[];

    /** The state each box was pressed into, by task, while the write and the read after it are on their way. */
    readonly pressedInto?: ReadonlyMap<string, boolean>;

    readonly onSelected: (selected: readonly string[]) => void;
    readonly onToggleCompleted: (task: PersonalTask) => void;

    /** Opens one task in its dialog. */
    readonly onOpen: (task: PersonalTask) => void;

    /** How what a task belongs to is opened, or `undefined` where that thread or event cannot be reached from here. */
    readonly anchorOpener: (anchor: TaskAnchor) => (() => void) | undefined;

    readonly onAccept: (task: PersonalTask) => void;

    /**
     * Opens the message a task cites, which is the Mail space's to draw and therefore the frame's to perform — or
     * `undefined` where this credential reads no mail, and a link to one would lead nowhere.
     */
    readonly onOpenSource: ((messageId: string) => void) | undefined;

    /** Opens what announces one task, which the screen holds the panel for. */
    readonly onReminders: (task: PersonalTask) => void;

    /** Puts tasks in the calendar, or `undefined` where this credential may not change one. */
    readonly onSchedule: ((tasks: readonly PersonalTask[]) => void) | undefined;

    /** Raises the question an erasure stands behind, over the tasks it is about. */
    readonly onAskErasure: (tasks: readonly PersonalTask[]) => void;

    /** Asks for the page after the ones held, which reaching the foot of the list is what calls. */
    readonly onReadMore: () => void;

    /** Reads the list again from the leading end, which is the way out of a failure. */
    readonly onReadAgain: () => void;
}) {
    const { locale, translate } = useLocalization();
    const headings = useId();
    const [menu, setMenu] = useState<{ readonly task: PersonalTask; readonly at: MenuPoint } | null>(null);

    // The rows by the task each draws, which is how the menu gives focus back to the row it was opened from. Closing a
    // menu that left focus on the element it has just taken out of the document is where keyboard use silently stops.
    const rows = useRef(new Map<string, HTMLLIElement>());

    const counted = new Intl.NumberFormat(locale);

    function scrolled(element: HTMLDivElement): void {
        if (reachedTheRowsRead(element)) {
            onReadMore();
        }
    }

    function acts(task: PersonalTask) {
        const source = task.sourceMessageId;
        const proposal = task.origin === 'Proposed';
        const anchor = task.anchor;

        return {
            onToggleCompleted: () => {
                onToggleCompleted(task);
            },
            onOpen: proposal
                ? undefined
                : () => {
                      onOpen(task);
                  },
            onOpenAnchor: anchor === null ? undefined : anchorOpener(anchor),
            onAccept: proposal
                ? () => {
                      onAccept(task);
                  }
                : undefined,
            onOpenSource:
                source === null || onOpenSource === undefined
                    ? undefined
                    : () => {
                          onOpenSource(source);
                      },
            onReminders:
                task.dueOn === null
                    ? undefined
                    : () => {
                          onReminders(task);
                      },
            onSchedule:
                onSchedule === undefined
                    ? undefined
                    : () => {
                          onSchedule([task]);
                      },
        };
    }

    return (
        <div
            className="flex shrink-0 flex-col gap-5 px-4 py-5 workspace:px-6 desktop:px-tasks-gutter desktop:only:px-tasks-gutter-alone panes:min-h-0 panes:flex-1 panes:shrink panes:overflow-y-auto"
            onScroll={(event) => {
                scrolled(event.currentTarget);
            }}
        >
            {groups.map((group) => (
                <section
                    key={group.name}
                    aria-labelledby={`${headings}-${group.name}`}
                    className="flex flex-col gap-2.25"
                >
                    <div className="flex flex-wrap items-center gap-2.25">
                        <h3 id={`${headings}-${group.name}`} className="text-xs font-semibold text-muted">
                            {translate(groupLabels[group.name])}
                        </h3>
                        <span className="rounded-md border border-line bg-rail px-1.75 text-xs text-muted">
                            {counted.format(group.tasks.length)}
                        </span>
                        {group.name === 'closedByReply' ? (
                            <p className="text-sm text-faint">{translate('tasks.closedByReplyNote')}</p>
                        ) : null}
                    </div>

                    <ul className="flex flex-col gap-2">
                        {group.tasks.map((task) => (
                            <TaskRow
                                key={task.id}
                                task={task}
                                now={now}
                                clash={clashOf(task, events)}
                                onDismiss={
                                    task.origin === 'Proposed'
                                        ? () => {
                                              onAskErasure([task]);
                                          }
                                        : undefined
                                }
                                selected={selected.includes(task.id)}
                                selecting={selected.length > 0}
                                scheduled={scheduled.includes(task.id)}
                                completed={pressedInto.get(task.id) ?? task.completed}
                                onToggleSelected={() => {
                                    onSelected(withToggled(selected, task.id));
                                }}
                                onPress={(at) => {
                                    setMenu({ task, at });
                                }}
                                onElement={(element) => {
                                    if (element === null) {
                                        rows.current.delete(task.id);
                                    } else {
                                        rows.current.set(task.id, element);
                                    }
                                }}
                                {...acts(task)}
                            />
                        ))}
                    </ul>
                </section>
            ))}

            {reading ? (
                <p role="status" className="text-sm text-muted">
                    {translate('tasks.reading')}
                </p>
            ) : null}

            {paging ? (
                <p role="status" className="text-sm text-muted">
                    {translate('tasks.readingMore')}
                </p>
            ) : null}

            {failure === null ? null : (
                <div className="flex flex-col items-start gap-2">
                    <p role="alert" className="text-sm text-warning text-pretty">
                        {translate(listFailures[failure])}
                    </p>
                    <SecondaryButton label={translate('tasks.readAgain')} shape="compact" onActivate={onReadAgain} />
                </div>
            )}

            {/* Two empties rather than one: a list with nothing in it and a list whose every row is done and being
                filtered out are different states, and a reader told the first when it is the second would go looking
                for work the deployment is holding. */}
            {!reading && failure === null && groups.length === 0 ? (
                everythingDone ? (
                    <p className="text-sm text-muted text-pretty">{translate('tasks.allDone')}</p>
                ) : (
                    <div className="flex flex-col items-center gap-2 px-5 py-14 text-center text-muted">
                        <Icon name="task_alt" className="size-icon-3xl text-faint" />
                        <p className="text-lg font-semibold text-text">{translate(viewEmpties[view].title)}</p>
                        <p className="max-w-95 text-base text-pretty">{translate(viewEmpties[view].text)}</p>
                    </div>
                )
            ) : null}

            <Departing value={menu}>
                {(menu) => (
                    <TaskRowMenu
                        task={menu.task}
                        at={menu.at}
                        onSelect={() => {
                            onSelected(onlySelected(menu.task.id));
                        }}
                        onAskErasure={() => {
                            onAskErasure([menu.task]);
                        }}
                        onClose={() => {
                            rows.current.get(menu.task.id)?.focus();
                            setMenu(null);
                        }}
                        {...acts(menu.task)}
                    />
                )}
            </Departing>
        </div>
    );
}
