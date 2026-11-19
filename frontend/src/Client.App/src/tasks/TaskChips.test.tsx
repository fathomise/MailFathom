// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { PersonalTask } from '@mailfathom/client-backend';
import { glyphOf } from '../controls/icons';
import { LocalizationProvider } from '../localization/Localization';
import { TaskChips } from './TaskChips';

function taskCalled(id: string, held: Partial<PersonalTask> = {}): PersonalTask {
    return {
        id,
        title: id,
        dueOn: null,
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

function drawChips(tasks: readonly PersonalTask[]) {
    const onOpen = vi.fn();

    render(
        <LocalizationProvider>
            <TaskChips tasks={tasks} onOpen={onOpen} />
        </LocalizationProvider>,
    );

    return { onOpen };
}

describe('TaskChips', () => {
    it('TaskChips draws a waiting chip with an hourglass', () => {
        const { onOpen } = drawChips([taskCalled('Reply from Anna', { direction: 'Awaited', dueOn: '2020-01-01' })]);

        const chip = screen.getByRole('button', { name: /Reply from Anna/u });

        expect(chip.textContent).toContain('follow up today');
        expect(chip.querySelector('path')?.getAttribute('d')).toBe(glyphOf('hourglass_top')?.outline);

        fireEvent.click(chip);
        expect(onOpen).toHaveBeenCalledWith('Reply from Anna');
    });

    it('counts the tasks past the first four quietly, and the count draws them', () => {
        drawChips(['one', 'two', 'three', 'four', 'five', 'six'].map((id) => taskCalled(id)));

        expect(screen.queryByRole('button', { name: /^five/u })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Show every task (+2 more)' }));

        expect(screen.getByRole('button', { name: /^six/u })).toBeDefined();
    });
});
