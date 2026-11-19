// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CalendarEvent, PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { PlanDialog } from './PlanDialog';

const unplanned: PersonalTask = {
    id: 'task-a',
    title: 'Send the counter-proposal',
    dueOn: '2026-09-24',
    reminders: [],
    remindsAt: [],
    origin: 'Asserted',
    completed: false,
    sourceMessageId: null,
    direction: 'Owed',
    estimatedMinutes: 45,
    plan: null,
    anchor: null,
    counterpart: null,
    answer: null,
};

const meeting: CalendarEvent = {
    id: 'event-a',
    title: 'Rollout status with Piotr',
    start: '2026-09-24T14:30:00+00:00',
    end: '2026-09-24T15:00:00+00:00',
    isAllDay: false,
    reminders: [],
    remindsAt: [],
    notes: null,
    origin: 'Asserted',
    sourceMessage: null,
    recordedAt: '2026-09-20T08:00:00+00:00',
    amendedAt: '2026-09-20T08:00:00+00:00',
};

const machineZone = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'UTC';
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date('2026-09-24T07:00:00Z'));
});

afterEach(() => {
    vi.useRealTimers();
    process.env['TZ'] = machineZone;
});

function drawPlan(task: PersonalTask = unplanned, events: readonly CalendarEvent[] = []) {
    const planned = vi.fn();

    render(
        <LocalizationProvider>
            <PlanDialog task={task} events={events} tasks={[]} onPlan={planned} onLeft={vi.fn()} />
        </LocalizationProvider>,
    );

    return { planned, dialog: screen.getByRole('dialog', { name: 'Plan a time' }) };
}

function hour(dialog: HTMLElement, label: string): HTMLElement {
    return within(dialog).getByRole('button', { name: new RegExp(`^${label}`, 'u') });
}

describe('PlanDialog', () => {
    it('PlanDialog refuses to save without an hour', () => {
        const { planned, dialog } = drawPlan();
        const save = within(dialog).getByRole('button', { name: 'Plan' });

        expect(save.getAttribute('aria-disabled')).toBe('true');
        expect(save.getAttribute('aria-describedby')).not.toBeNull();
        expect(document.getElementById(save.getAttribute('aria-describedby') ?? '')?.textContent).toBe(
            'Pick an hour first',
        );
        expect(within(dialog).getByText('Pick an hour.')).toBeDefined();

        fireEvent.click(save);

        expect(planned).not.toHaveBeenCalled();

        fireEvent.click(hour(dialog, '2:00 PM'));

        expect(save.getAttribute('aria-disabled')).toBeNull();

        fireEvent.click(save);

        expect(planned).toHaveBeenCalledWith({ startAt: '2026-09-24T14:00:00.000Z', minutes: 45 });
    });

    it('PlanDialog warns of a clash with a meeting at the picked hour', () => {
        const { dialog } = drawPlan(unplanned, [meeting]);

        expect(within(dialog).getByText('Rollout status with Piotr')).toBeDefined();

        fireEvent.click(hour(dialog, '2:00 PM'));

        expect(
            within(dialog).getByText('Clashes with Rollout status with Piotr at 2:30 PM. You can still plan it.'),
        ).toBeDefined();

        fireEvent.click(hour(dialog, '10:00 AM'));

        expect(within(dialog).queryByText(/^Clashes with/u)).toBeNull();
    });

    it('plans the chosen day for the chosen length', () => {
        const { planned, dialog } = drawPlan();

        fireEvent.click(within(dialog).getByRole('radio', { name: 'Tomorrow' }));
        fireEvent.click(within(dialog).getByRole('radio', { name: '1 hr' }));
        fireEvent.click(hour(dialog, '9:00 AM'));

        expect(within(dialog).getByText(/^Planned tomorrow, /u)).toBeDefined();

        fireEvent.click(within(dialog).getByRole('button', { name: 'Plan' }));

        expect(planned).toHaveBeenCalledWith({ startAt: '2026-09-25T09:00:00.000Z', minutes: 60 });
    });

    it('opens on the plan a task holds, and takes it out of the calendar on request', () => {
        const { planned, dialog } = drawPlan({
            ...unplanned,
            plan: { startAt: '2026-09-24T11:00:00+00:00', minutes: 30 },
        });

        expect(hour(dialog, '11:00 AM').getAttribute('aria-pressed')).toBe('true');
        expect(within(dialog).getByRole<HTMLInputElement>('radio', { name: '30 min' }).checked).toBe(true);

        fireEvent.click(within(dialog).getByRole('button', { name: 'Take out of the calendar' }));

        expect(planned).toHaveBeenCalledWith(null);
    });

    it('offers no way to take out what is not in the calendar', () => {
        const { dialog } = drawPlan();

        expect(within(dialog).queryByRole('button', { name: 'Take out of the calendar' })).toBeNull();
    });
});
