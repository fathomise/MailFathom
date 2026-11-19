// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { TaskRowMenu } from './TaskRowMenu';

function taskOf(held: Partial<PersonalTask> = {}): PersonalTask {
    return {
        id: 'a',
        title: 'Answer the tender',
        dueOn: '2026-09-24',
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        direction: 'Owed',
        estimatedMinutes: null,
        plan: null,
        anchor: null,
        counterpart: null,
        answer: null,
        ...held,
    };
}

function menuOver({
    task = taskOf(),
    onSelect = vi.fn(),
    onToggleCompleted = vi.fn(),
    onOpen,
    onOpenAnchor,
    onAccept,
    onOpenSource,
    onReminders,
    onPlan = vi.fn(),
    onAskErasure = vi.fn(),
    onDismiss = vi.fn(),
}: {
    task?: PersonalTask;
    onSelect?: () => void;
    onToggleCompleted?: () => void;
    onOpen?: (() => void) | undefined;
    onOpenAnchor?: (() => void) | undefined;
    onAccept?: (() => void) | undefined;
    onOpenSource?: (() => void) | undefined;
    onReminders?: (() => void) | undefined;
    onPlan?: (() => void) | undefined;
    onAskErasure?: () => void;
    onDismiss?: () => void;
} = {}): void {
    render(
        <LocalizationProvider>
            <TaskRowMenu
                task={task}
                at={{ x: 20, y: 30 }}
                onSelect={onSelect}
                onToggleCompleted={onToggleCompleted}
                onOpen={onOpen}
                onOpenAnchor={onOpenAnchor}
                onAccept={onAccept}
                onOpenSource={onOpenSource}
                onReminders={onReminders}
                onPlan={onPlan}
                onAskErasure={onAskErasure}
                onDismiss={onDismiss}
                onClose={vi.fn()}
            />
        </LocalizationProvider>,
    );
}

function drawn(): (string | null)[] {
    return screen.getAllByRole('menuitem').map((item) => item.textContent);
}

describe('TaskRowMenu', () => {
    it('carries the acts the design draws over a task somebody already owes', () => {
        menuOver({ onOpenSource: vi.fn(), onReminders: vi.fn() });

        expect(drawn()).toStrictEqual([
            'Mark as done',
            'Plan in the calendar',
            'Reminders',
            'Open source thread',
            'Select tasks',
            'Delete task',
        ]);
    });

    it('opens the task and what it belongs to from items of their own', () => {
        const onOpen = vi.fn();
        const onOpenAnchor = vi.fn();

        menuOver({
            task: taskOf({
                anchor: { kind: 'Event', id: '0190f3c2-8a7b-7cde-9f01-23456789abcd', label: 'Call with Anna' },
            }),
            onOpen,
            onOpenAnchor,
        });

        expect(drawn()[0]).toBe('Open task');

        fireEvent.click(screen.getByRole('menuitem', { name: 'Open the event' }));

        expect(onOpenAnchor).toHaveBeenCalledOnce();
    });

    it('offers a proposal nothing but taking it and turning it down', () => {
        menuOver({ task: taskOf({ origin: 'Proposed' }), onAccept: vi.fn() });

        expect(drawn()).toStrictEqual(['Accept', 'Dismiss task']);
    });

    it('dismisses a proposal at once rather than asking about an erasure', () => {
        const onDismiss = vi.fn();
        const onAskErasure = vi.fn();

        menuOver({ task: taskOf({ origin: 'Proposed' }), onAccept: vi.fn(), onDismiss, onAskErasure });
        fireEvent.click(screen.getByRole('menuitem', { name: 'Dismiss task' }));

        expect(onDismiss).toHaveBeenCalledOnce();
        expect(onAskErasure).not.toHaveBeenCalled();
    });

    it('words the completion by what pressing it would do', () => {
        menuOver({ task: taskOf({ completed: true }) });

        expect(drawn()).toContain('Mark as not done');
    });

    it('moves a task already planned', () => {
        menuOver({ task: taskOf({ plan: { startAt: '2026-09-24T09:00:00+00:00', minutes: 30 } }) });

        expect(drawn()).toContain('Move in the calendar');
    });

    it('offers no plan for a task waited on', () => {
        menuOver({ task: taskOf({ direction: 'Awaited' }) });

        expect(drawn()).not.toContain('Plan in the calendar');
    });

    it('offers nothing to announce for a task nobody dated, a lead having no day to be measured back from', () => {
        menuOver({ task: taskOf({ dueOn: null }) });

        expect(drawn()).not.toContain('Reminders');
    });

    it('opens what announces the task from its own item', () => {
        const onReminders = vi.fn();

        menuOver({ onReminders });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Reminders' }));

        expect(onReminders).toHaveBeenCalledOnce();
    });

    it('offers no way back to a message where the task cites none', () => {
        menuOver();

        expect(drawn()).not.toContain('Open source thread');
    });

    it('starts the selection from its own first item, which is how somebody with no modifier key reaches one', () => {
        const onSelect = vi.fn();

        menuOver({ onSelect });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Select tasks' }));

        expect(onSelect).toHaveBeenCalledOnce();
    });

    it('raises the question the erasure stands behind rather than erasing from the menu', () => {
        const onAskErasure = vi.fn();

        menuOver({ onAskErasure });

        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete task' }));

        expect(onAskErasure).toHaveBeenCalledOnce();
    });

    it('names the task the menu is about', () => {
        menuOver();

        expect(screen.getByText('Answer the tender')).toBeDefined();
    });
});
