// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { CalendarEvent, PersonalTask } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { carriedTaskType } from './dayHours';
import { DayPlan } from './DayPlan';
import type { TodayCalendarInForce } from './useTodayCalendar';

const now = Date.parse('2026-09-24T07:00:00Z');

const standup: CalendarEvent = {
    id: 'event-a',
    title: 'Rollout status with Piotr',
    start: '2026-09-24T15:00:00+00:00',
    end: '2026-09-24T16:00:00+00:00',
    isAllDay: false,
    reminders: [],
    remindsAt: [],
    notes: null,
    origin: 'Asserted',
    sourceMessage: null,
    recordedAt: '2026-09-20T08:00:00+00:00',
    amendedAt: '2026-09-20T08:00:00+00:00',
};

const planned: PersonalTask = {
    id: 'task-a',
    title: 'Reply to Marta about the calculation',
    dueOn: '2026-09-24',
    reminders: [],
    remindsAt: [],
    origin: 'Asserted',
    completed: false,
    sourceMessageId: null,
    direction: 'Owed',
    estimatedMinutes: 20,
    plan: { startAt: '2026-09-24T11:00:00+00:00', minutes: 20 },
    anchor: null,
    counterpart: null,
    answer: null,
};

function dayHolding(events: readonly CalendarEvent[]): TodayCalendarInForce {
    return { events, reading: false, failure: null, readAgain: vi.fn() };
}

const machineZone = process.env['TZ'];

beforeEach(() => {
    process.env['TZ'] = 'UTC';
});

afterEach(() => {
    process.env['TZ'] = machineZone;
});

function drawDay(events: readonly CalendarEvent[] = [standup], tasks: readonly PersonalTask[] = [planned]) {
    const dropped = vi.fn();
    const opened = vi.fn();

    render(
        <LocalizationProvider>
            <DayPlan now={now} day={dayHolding(events)} tasks={tasks} onDrop={dropped} onOpenTask={opened} />
        </LocalizationProvider>,
    );

    return { dropped, opened };
}

function hourRow(label: string): HTMLElement {
    const row = screen.getAllByRole('listitem').find((item) => item.firstChild?.textContent === label);

    if (row === undefined) {
        throw new Error(`No hour ${label}`);
    }

    return row;
}

describe('DayPlan', () => {
    it('draws the hours from eight to five with the meetings and the planned tasks at their hours', () => {
        const { opened } = drawDay();

        expect(screen.getAllByRole('listitem').map((row) => row.firstChild?.textContent)).toStrictEqual([
            '8:00 AM',
            '9:00 AM',
            '10:00 AM',
            '11:00 AM',
            '12:00 PM',
            '1:00 PM',
            '2:00 PM',
            '3:00 PM',
            '4:00 PM',
            '5:00 PM',
        ]);
        expect(within(hourRow('3:00 PM')).getByText('Rollout status with Piotr')).toBeDefined();
        expect(within(hourRow('11:00 AM')).getByText('Reply to Marta about the calculation')).toBeDefined();

        fireEvent.click(within(hourRow('11:00 AM')).getByRole('button', { name: /Reply to Marta/u }));

        expect(opened).toHaveBeenCalledWith(planned);
    });

    it('says how a task is planned here, by dragging it, and how a finger plans one instead', () => {
        drawDay();

        expect(screen.getByText('Drag a task onto an hour to plan it.')).toBeDefined();
        expect(screen.getByText('Plan a task from its own Plan.')).toBeDefined();
    });

    it('hands over the task dropped on an hour and the instant that hour begins', () => {
        const { dropped } = drawDay();
        const carried = {
            types: [carriedTaskType],
            getData: (type: string) => (type === carriedTaskType ? 'task-b' : ''),
        };

        fireEvent.dragOver(hourRow('2:00 PM'), { dataTransfer: carried });
        fireEvent.drop(hourRow('2:00 PM'), { dataTransfer: carried });

        expect(dropped).toHaveBeenCalledWith('task-b', '2026-09-24T14:00:00.000Z');
    });

    it('takes no drop of anything but a task', () => {
        const { dropped } = drawDay();
        const text = { types: ['text/plain'], getData: () => 'hello' };

        fireEvent.drop(hourRow('2:00 PM'), { dataTransfer: text });

        expect(dropped).not.toHaveBeenCalled();
    });
});
