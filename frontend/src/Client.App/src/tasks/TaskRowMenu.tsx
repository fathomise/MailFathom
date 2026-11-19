// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { PersonalTask } from '@mailfathom/client-backend';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';
import { useWriteRefusal } from '../shell/useWriteRefusal';

// What a task's row offers, which is this list's own items handed to the one menu six lists share: where the menu
// stands, how it is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s.
//
// **The order is the design's**: *Open task*, the completion, putting it in the calendar, *Reminders*, what it belongs
// to, *Select tasks* and *Delete task* — and a proposal's menu holds *Accept* and *Dismiss task* alone, because a
// proposal is answered rather than worked on.
//
// **An item the client cannot yet perform is left out rather than drawn inert**, which is the rule
// `messageRows/MessageRowMenu.tsx` states: *reminders* on a task nobody dated, a lead having no day to be measured
// back from; *Plan in the calendar* or *Move in the calendar* on a task that is done or waited on; what it belongs to where that
// cannot be reached from here;
// and *Follow up*, which opens a reply and arrives with the composer's change.
//
// **Nothing here performs the erasure.** The question it stands behind outlives the menu — the menu is gone the moment
// an item is chosen — so what this does is ask the screen to raise it.

export function TaskRowMenu({
    task,
    at,
    onSelect,
    onOpen,
    onToggleCompleted,
    onOpenAnchor,
    onAccept,
    onReminders,
    onPlan,
    onOpenSource,
    onAskErasure,
    onDismiss,
    onClose,
}: {
    readonly task: PersonalTask;
    readonly at: MenuPoint;

    /** Puts this row into the selection the list holds, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    /** Opens the task in its dialog, or `undefined` for a proposal. */
    readonly onOpen?: (() => void) | undefined;

    readonly onToggleCompleted: () => void;

    /** Opens what the task belongs to, or `undefined` where that cannot be reached from here. */
    readonly onOpenAnchor?: (() => void) | undefined;

    /** Takes a task mail proposed on, or `undefined` for one the person already owes. */
    readonly onAccept: (() => void) | undefined;

    /** Opens what announces this task, or `undefined` for one nobody dated, which can announce nothing. */
    readonly onReminders: (() => void) | undefined;

    /** Opens the plan dialog for this task, or `undefined` where it cannot be planned from here. */
    readonly onPlan?: (() => void) | undefined;

    /** Opens the message this task cites, or `undefined` where it cites none. */
    readonly onOpenSource: (() => void) | undefined;

    /** Raises the question the erasure stands behind, over this one task. */
    readonly onAskErasure: () => void;

    /** Turns a proposal down at once, which asks nothing first: nobody had taken it on. */
    readonly onDismiss: () => void;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const writeRefused = useWriteRefusal();

    // Every item but selecting the row and opening the message it cites writes, so while the server is out of reach
    // each of those stays in the menu saying why it cannot act, rather than being taken and failing after.
    const writing = (icon: ContextMenuItem['icon'], label: string, choose: () => void): ContextMenuItem => ({
        icon,
        label,
        refused: writeRefused(label),
        choose,
    });

    if (task.origin === 'Proposed') {
        const answers: ContextMenuItem[] = [
            ...(onAccept === undefined ? [] : [writing('add_task', translate('tasks.accept'), onAccept)]),
            writing('close', translate('tasks.declineTask'), onDismiss),
        ];

        return <ContextMenu header={task.title} at={at} items={answers} onClose={onClose} />;
    }

    const items: ContextMenuItem[] = [];

    if (onOpen !== undefined) {
        items.push({ icon: 'open_in_full', label: translate('tasks.openTask'), choose: onOpen });
    }

    items.push(
        writing(
            task.completed ? 'radio_button_unchecked' : 'check_circle',
            translate(task.completed ? 'tasks.markNotDone' : 'tasks.markDone'),
            onToggleCompleted,
        ),
    );

    if (task.direction === 'Owed' && !task.completed && onPlan !== undefined) {
        items.push(
            writing(
                'schedule',
                translate(task.plan === null ? 'tasks.planInCalendar' : 'tasks.moveInCalendar'),
                onPlan,
            ),
        );
    }

    if (onReminders !== undefined && task.answer === null) {
        items.push(writing('notifications_active', translate('reminders.title'), onReminders));
    }

    if (task.anchor !== null && onOpenAnchor !== undefined) {
        items.push({
            icon: task.anchor.kind === 'Thread' ? 'mail' : 'event',
            label: translate(task.anchor.kind === 'Thread' ? 'tasks.openThread' : 'tasks.openEvent'),
            choose: onOpenAnchor,
        });
    }

    if (onOpenSource !== undefined) {
        items.push({ icon: 'open_in_new', label: translate('tasks.openSourceThread'), choose: onOpenSource });
    }

    items.push({ icon: 'check_box', label: translate('tasks.selectTasks'), choose: onSelect });
    items.push({ ...writing('delete', translate('tasks.deleteTask'), onAskErasure), destroys: true });

    return <ContextMenu header={task.title} detail={task.anchor?.label} at={at} items={items} onClose={onClose} />;
}
