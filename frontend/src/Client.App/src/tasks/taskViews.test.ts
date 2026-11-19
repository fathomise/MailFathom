// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { CalendarEvent, PersonalTask } from '@mailfathom/client-backend';
import { clashOf, openIn, tasksIn, type TaskView } from './taskViews';

// Every case names the zone rather than assigning one to the runtime, so a plan's instant is read against the day
// the reader's record states and not against wherever the suite happens to run.
const warsaw = 'Europe/Warsaw';
const today = '2026-09-21';

function task(id: string, held: Partial<PersonalTask> = {}): PersonalTask {
    return {
        id,
        title: id,
        dueOn: today,
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

function groupsOf(
    view: TaskView,
    tasks: readonly PersonalTask[],
    proposals: readonly PersonalTask[] = [],
    showDone = false,
) {
    return tasksIn(view, tasks, proposals, today, warsaw, showDone).map((group) => ({
        name: group.name,
        ids: group.tasks.map(({ id }) => id),
    }));
}

describe('tasksIn', () => {
    it('a task due today with no plan is in "Due today, not planned"', () => {
        expect(groupsOf('today', [task('a')])).toStrictEqual([{ name: 'dueNotPlanned', ids: ['a'] }]);
    });

    it('a task planned today but due tomorrow is in "Planned today"', () => {
        // Ten in the morning in Warsaw, which is eight in Greenwich.
        const planned = task('a', { dueOn: '2026-09-22', plan: { startAt: '2026-09-21T08:00:00+00:00', minutes: 45 } });

        expect(groupsOf('today', [planned])).toStrictEqual([{ name: 'planned', ids: ['a'] }]);
        expect(groupsOf('upcoming', [planned])).toStrictEqual([]);
    });

    it('draws what is planned today in the order it is planned', () => {
        const late = task('late', { plan: { startAt: '2026-09-21T13:00:00+00:00', minutes: 30 } });
        const early = task('early', { plan: { startAt: '2026-09-21T07:00:00+00:00', minutes: 30 } });

        expect(groupsOf('today', [late, early])).toStrictEqual([{ name: 'planned', ids: ['early', 'late'] }]);
    });

    it('an answered waiting task is in "Closed by a reply"', () => {
        const answered = task('a', {
            direction: 'Awaited',
            completed: true,
            answer: { messageId: 'reply', arrivedAt: '2026-09-21T07:12:00+00:00' },
        });

        expect(groupsOf('waiting', [answered])).toStrictEqual([{ name: 'closedByReply', ids: ['a'] }]);
        expect(groupsOf('today', [answered])).toStrictEqual([]);
    });

    it('a waiting task due today is followed up today', () => {
        const waiting = task('a', { direction: 'Awaited' });

        expect(groupsOf('waiting', [waiting])).toStrictEqual([{ name: 'followUpToday', ids: ['a'] }]);
        expect(groupsOf('today', [waiting])).toStrictEqual([{ name: 'followUp', ids: ['a'] }]);
    });

    it('keeps a waiting task whose day has not come under "Waiting", and out of Today', () => {
        const waiting = task('a', { direction: 'Awaited', dueOn: '2026-09-25' });

        expect(groupsOf('waiting', [waiting])).toStrictEqual([{ name: 'waiting', ids: ['a'] }]);
        expect(groupsOf('today', [waiting])).toStrictEqual([]);
    });

    it('draws what the person marked received only when done work is shown', () => {
        const received = task('a', { direction: 'Awaited', completed: true });

        expect(groupsOf('waiting', [received])).toStrictEqual([]);
        expect(groupsOf('waiting', [received], [], true)).toStrictEqual([{ name: 'received', ids: ['a'] }]);
    });

    it('groups what is owed after today by tomorrow, this week, later and no day', () => {
        const tasks = [
            task('tomorrow', { dueOn: '2026-09-22' }),
            task('week', { dueOn: '2026-09-27' }),
            task('later', { dueOn: '2026-09-28' }),
            task('undated', { dueOn: null }),
        ];

        expect(groupsOf('upcoming', tasks)).toStrictEqual([
            { name: 'tomorrow', ids: ['tomorrow'] },
            { name: 'thisWeek', ids: ['week'] },
            { name: 'later', ids: ['later'] },
            { name: 'noDay', ids: ['undated'] },
        ]);
    });

    it('stands overdue work under Today, which is when it has to be done', () => {
        expect(groupsOf('today', [task('a', { dueOn: '2026-09-18' })])).toStrictEqual([
            { name: 'dueNotPlanned', ids: ['a'] },
        ]);
    });

    it('leaves done work out unless it is asked for', () => {
        const done = task('a', { completed: true });

        expect(groupsOf('today', [done])).toStrictEqual([]);
        expect(groupsOf('today', [done], [], true)).toStrictEqual([{ name: 'dueNotPlanned', ids: ['a'] }]);
    });

    it('draws proposals by their day, and only in Proposed', () => {
        const proposals = [
            task('now', { origin: 'Proposed' }),
            task('soon', { origin: 'Proposed', dueOn: '2026-09-24' }),
            task('undated', { origin: 'Proposed', dueOn: null }),
        ];

        expect(groupsOf('proposed', [], proposals)).toStrictEqual([
            { name: 'today', ids: ['now'] },
            { name: 'later', ids: ['soon'] },
            { name: 'noDay', ids: ['undated'] },
        ]);
        expect(groupsOf('today', [], proposals)).toStrictEqual([]);
    });
});

describe('openIn', () => {
    it('counts what is still open in each view', () => {
        const tasks = [
            task('due'),
            task('later', { dueOn: '2026-10-01' }),
            task('done', { completed: true }),
            task('follow', { direction: 'Awaited' }),
            task('wait', { direction: 'Awaited', dueOn: '2026-09-30' }),
        ];

        expect(openIn(tasks, [task('p', { origin: 'Proposed' })], today, warsaw)).toStrictEqual({
            today: 2,
            upcoming: 1,
            waiting: 2,
            proposed: 1,
        });
    });
});

describe('clashOf', () => {
    function event(start: string, end: string | null, isAllDay = false): CalendarEvent {
        return {
            id: start,
            title: 'Rollout status',
            start,
            end,
            isAllDay,
            reminders: [],
            notes: null,
            remindsAt: [],
            origin: 'Asserted',
            sourceMessage: null,
            recordedAt: '2026-09-20T07:00:00+00:00',
            amendedAt: '2026-09-20T07:00:00+00:00',
        };
    }

    const planned = task('a', { plan: { startAt: '2026-09-21T13:00:00+00:00', minutes: 30 } });

    it('names the timed event a planned stretch overlaps', () => {
        const meeting = event('2026-09-21T13:15:00+00:00', '2026-09-21T14:00:00+00:00');

        expect(clashOf(planned, [meeting])).toBe(meeting);
    });

    it('finds no clash with an event that ends as the task begins, nor with a day-long one', () => {
        expect(
            clashOf(planned, [
                event('2026-09-21T12:00:00+00:00', '2026-09-21T13:00:00+00:00'),
                event('2026-09-21T00:00:00+00:00', '2026-09-22T00:00:00+00:00', true),
            ]),
        ).toBeNull();
    });

    it('finds no clash for a task nobody planned, or one already done', () => {
        const meeting = event('2026-09-21T13:15:00+00:00', '2026-09-21T14:00:00+00:00');

        expect(clashOf(task('b'), [meeting])).toBeNull();
        expect(clashOf({ ...planned, completed: true }, [meeting])).toBeNull();
    });
});
