// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ProposedTasks } from './ProposedTasks';

const proposal: PersonalTask = {
    id: 'task-baseline',
    title: 'Ask Marta for the 2026 baseline figures',
    dueOn: null,
    reminders: [],
    remindsAt: [],
    origin: 'Proposed',
    completed: false,
    sourceMessageId: null,
    direction: 'Owed',
    estimatedMinutes: 10,
    plan: null,
    anchor: { kind: 'Thread', id: 'thread-1', label: 'Baseline 2026' },
    counterpart: null,
    answer: null,
};

function drawPanel(proposals: readonly PersonalTask[], onPlanDay?: () => void) {
    const onAccept = vi.fn();
    const onDismiss = vi.fn();

    render(
        <LocalizationProvider>
            <ProposedTasks proposals={proposals} onAccept={onAccept} onDismiss={onDismiss} onPlanDay={onPlanDay} />
        </LocalizationProvider>,
    );

    return { onAccept, onDismiss };
}

describe('ProposedTasks', () => {
    it('ProposedTasks shows "No proposals left" when nothing is proposed', () => {
        drawPanel([]);

        expect(
            within(screen.getByRole('region', { name: 'Proposed tasks' })).getByText(
                'No proposals left. Tasks keeps every one you accepted.',
            ),
        ).toBeDefined();
    });

    it('says where a proposal came from and when it is due, and answers it either way', () => {
        const { onAccept, onDismiss } = drawPanel([proposal]);

        expect(screen.getByText('From the Agent · Baseline 2026')).toBeDefined();
        expect(screen.getByText('no day')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Accept' }));
        fireEvent.click(screen.getByRole('button', { name: 'Dismiss' }));

        expect(onAccept).toHaveBeenCalledWith(proposal);
        expect(onDismiss).toHaveBeenCalledWith(proposal);
    });

    it('offers "Plan my day" only where there is a way to lay the day out', () => {
        const onPlanDay = vi.fn();

        drawPanel([], onPlanDay);
        fireEvent.click(screen.getByRole('button', { name: 'Plan my day' }));

        expect(onPlanDay).toHaveBeenCalledOnce();
    });
});
