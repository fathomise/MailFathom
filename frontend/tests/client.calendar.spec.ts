// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { holdTheDay, openSignedInAsDeveloper, test } from './client.harness';
import { corpusEventId } from './fixtures/calendar';

// An event written, amended, and deleted through the calendar, and each of the four views read after every act. A
// view reads its own span again rather than correcting what another one held, so what each draws is what the next read
// of the deployment answered — and a view that drew from anything it kept would disagree with the other three.

// The corpus deployment states Greenwich as the reader's zone, and the reader sits in it. The views place an event on
// the browser's own day and word its time in the deployment's zone, so a browser standing on another day would file the
// event under a day the journey never wrote it on.
test.use({ timezoneId: 'UTC' });

const views = ['Week', 'Day', 'Month', 'Agenda'] as const;

/** The day the reader is on, as the listbox of one day's events is named in the three views drawing days. */
function dayNamed(day: Date): string {
    return new Intl.DateTimeFormat('en', {
        weekday: 'long',
        day: 'numeric',
        month: 'long',
        year: 'numeric',
        timeZone: 'UTC',
    }).format(day);
}

/** One hour of the day, as the listbox the day view draws that hour's events in is named. */
function hourNamed(hour: number): string {
    return new Intl.DateTimeFormat('en', { hour: 'numeric', minute: '2-digit', timeZone: 'UTC' }).format(
        Date.UTC(2026, 0, 1, hour),
    );
}

/** Today as the day field of the event form writes it, which is what an instant on it is composed from. */
function today(day: Date): string {
    return day.toISOString().slice(0, 10);
}

/**
 * Where one view draws the reader's day — the hour row in the day view, the day everywhere else — once that view's
 * own read has answered.
 *
 * What says the read answered is the corpus's first entry, which every window places on its first day at nine: the
 * views draw nothing while a span is being read, so an absence asserted before it would be an absence of anything.
 */
async function viewOfToday(page: Page, day: Date, view: (typeof views)[number], hour: number) {
    // Chosen from the keyboard, because the radio itself is drawn inside the label that names it and a pointer lands
    // on the label.
    await page.getByRole('radio', { name: view }).press('Space');
    await expect(page.getByRole('option', { name: /Warehouse handover$/u }).first()).toBeVisible();

    return page.getByRole('listbox', {
        name: view === 'Day' ? `Events at ${hourNamed(hour)}` : `Events on ${dayNamed(day)}`,
    });
}

test('draws an event it wrote, amended, and deleted the same in the week, the day, the month, and the agenda', async ({
    page,
    deployment,
}) => {
    const day = await holdTheDay(page, deployment);

    await openSignedInAsDeveloper(page, '/#/calendar');

    await page.getByRole('button', { name: 'New event' }).click();

    const writing = page.getByRole('dialog', { name: 'New event' });

    await writing.getByLabel('Title').fill('Dentist appointment');
    await writing.getByLabel('Description').fill('Bring the referral.');
    await writing.getByLabel('Start time').fill('13:00');
    await writing.getByLabel('End time').fill('13:30');
    await writing.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Saved.' })).toBeVisible();

    for (const view of views) {
        await expect(
            (await viewOfToday(page, day, view, 13)).getByRole('option', { name: /Dentist appointment$/u }),
        ).toBeVisible();
    }

    expect(deployment.requests('POST', '/calendar').map(({ body }) => body)).toStrictEqual([
        {
            title: 'Dentist appointment',
            start: `${today(day)}T13:00:00.000Z`,
            end: `${today(day)}T13:30:00.000Z`,
            isAllDay: false,
            reminders: [],
            notes: 'Bring the referral.',
            sourceMessage: null,
        },
    ]);

    // Amended from the agenda, which is the view the loop above ended on, and moved two hours later on the same day.
    await (await viewOfToday(page, day, 'Agenda', 13)).getByRole('option', { name: /Dentist appointment$/u }).click();

    const opened = page.getByRole('dialog', { name: 'Dentist appointment' });

    await expect(opened.getByText('Bring the referral.')).toBeVisible();
    await opened.getByRole('button', { name: 'Edit' }).click();
    await opened.getByLabel('Title').fill('Dental check-up');
    await opened.getByLabel('Start time').fill('15:00');
    await opened.getByLabel('End time').fill('15:45');
    await opened.getByRole('button', { name: 'Save' }).click();

    const amended = page.getByRole('dialog', { name: 'Dental check-up' });

    await expect(amended.getByRole('heading', { name: 'Dental check-up' })).toBeVisible();
    await amended.getByRole('button', { name: 'Close' }).click();

    for (const view of views) {
        const drawn = await viewOfToday(page, day, view, 15);

        await expect(drawn.getByRole('option', { name: /Dental check-up$/u })).toBeVisible();
        await expect(page.getByRole('option', { name: /Dentist appointment$/u })).toHaveCount(0);
    }

    // The hour it was moved out of holds nothing of it any more, which the day view is the only one to say.
    await expect(
        (await viewOfToday(page, day, 'Day', 13)).getByRole('option', { name: /Dental check-up$/u }),
    ).toHaveCount(0);

    const [amendment] = deployment.issued.filter(
        ({ method, route }) => method === 'PUT' && route.startsWith('/calendar/'),
    );

    expect(amendment?.body).toStrictEqual({
        title: 'Dental check-up',
        start: `${today(day)}T15:00:00.000Z`,
        end: `${today(day)}T15:45:00.000Z`,
        isAllDay: false,
        reminders: [],
        notes: 'Bring the referral.',
    });

    await (await viewOfToday(page, day, 'Week', 15)).getByRole('option', { name: /Dental check-up$/u }).click();
    await page
        .getByRole('dialog', { name: 'Dental check-up' })
        .getByRole('button', { name: 'Delete the event' })
        .click();
    await page.getByRole('button', { name: 'Delete from the calendar' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Deleted.' })).toBeVisible();

    for (const view of views) {
        await viewOfToday(page, day, view, 15);
        await expect(page.getByRole('option', { name: /Dental check-up$/u })).toHaveCount(0);
    }

    // The event deleted is the one amended, which is the one the write created: one identity from start to end.
    expect(deployment.issued.filter(({ method }) => method === 'DELETE').map(({ route }) => route)).toStrictEqual([
        amendment?.route,
    ]);
});

// The tasks the corpus holds for the reader's day: one planned at eleven over three quarters of an hour, and one due
// today and planned nowhere, which the calendar draws as a mark at the top of the day.
const plannedToday = /^Task · 11:00.*Send the 5% CPI cap counter-proposal/u;
const dueToday = /^Due · task\s?Book the meeting room for the Fabrikam review/u;

test('planned tasks appear in the week, day, month and agenda views', async ({ page, deployment }) => {
    const day = await holdTheDay(page, deployment);

    await openSignedInAsDeveloper(page, '/#/calendar');

    for (const view of views) {
        const drawn = await viewOfToday(page, day, view, 11);

        await expect(drawn.getByRole('option', { name: plannedToday })).toBeVisible();

        // The day view stands a task due that day above its hours rather than in any one of them.
        const dueIn = view === 'Day' ? page.getByRole('listbox', { name: 'Due on this day' }) : drawn;

        await expect(dueIn.getByRole('option', { name: dueToday })).toBeVisible();
    }

    // Pressed, a task opens the task dialog rather than the event dialog.
    await (await viewOfToday(page, day, 'Week', 11)).getByRole('option', { name: plannedToday }).click();

    await expect(page.getByRole('dialog', { name: 'Task' })).toBeVisible();
    await expect(page.getByRole('dialog', { name: 'Task' }).getByRole('textbox')).toHaveValue(
        'Send the 5% CPI cap counter-proposal',
    );
});

test('accepting a proposed task from the calendar makes it a task', async ({ page, deployment }) => {
    const day = await holdTheDay(page, deployment);

    await openSignedInAsDeveloper(page, '/#/calendar');

    const proposed = page.getByRole('region', { name: 'Proposed tasks' });
    const card = proposed
        .getByRole('listitem')
        .filter({ hasText: 'Send Piotr the rollout risks before the status call' });

    await expect(card).toContainText('From mail · Re: rollout schedule');
    await card.getByRole('button', { name: 'Accept' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Added to your tasks.' })).toBeVisible();
    await expect(card).toHaveCount(0);
    expect(deployment.requests('POST', '/tasks/task-signatures/acceptance')).toHaveLength(1);

    // Accepted, it stands on its day as a task due there.
    await expect(
        (await viewOfToday(page, day, 'Week', 11)).getByRole('option', {
            name: /^Due · task\s?Send Piotr the rollout risks before the status call/u,
        }),
    ).toBeVisible();
});

test('"Plan my day" opens the laid-out day in Tasks', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/calendar');

    await page.getByRole('button', { name: 'Plan my day' }).click();

    await expect(page).toHaveURL(/#\/tasks/u);
    await expect(page.getByRole('radio', { name: /^Today/u })).toBeChecked();
    await expect(page.getByRole('region', { name: 'Day capacity' })).toContainText(
        'Here is a way to fit the rest of today',
    );
});

test('an event shows its tasks and adds one prepared for it', async ({ page, deployment }) => {
    const day = await holdTheDay(page, deployment);

    await openSignedInAsDeveloper(page, '/#/calendar');

    // The corpus places its first entry on the first day of the week asked for, at nine.
    const monday = new Date(day);

    monday.setUTCDate(monday.getUTCDate() - ((monday.getUTCDay() + 6) % 7));

    await page
        .getByRole('option', { name: /Warehouse handover$/u })
        .first()
        .click();

    const opened = page.getByRole('dialog', { name: 'Warehouse handover' });
    const carried = opened.getByRole('region', { name: 'Tasks for this event' });

    await expect(carried).toContainText(
        'Nothing to prepare yet. A task added here belongs to this event and is due on its day.',
    );

    await carried.getByRole('button', { name: 'Add a task' }).click();

    const writing = page.getByRole('dialog', { name: 'New task' });

    await expect(writing.getByLabel('Task description')).toHaveValue('Prepare for Warehouse handover');
    await expect(writing.getByLabel('Due date')).toHaveValue(today(monday));
    await writing.getByRole('button', { name: 'Add task' }).click();

    await expect(carried.getByRole('button', { name: /^Prepare for Warehouse handover/u })).toBeVisible();
    expect(deployment.requests('POST', '/tasks').map(({ body }) => body)).toMatchObject([
        {
            title: 'Prepare for Warehouse handover',
            dueOn: today(monday),
            anchor: { kind: 'Event', id: corpusEventId(0), label: 'Warehouse handover' },
        },
    ]);

    // Each task the event carries opens its task.
    await carried.getByRole('button', { name: /^Prepare for Warehouse handover/u }).click();

    await expect(page.getByRole('dialog', { name: 'Task' })).toBeVisible();
});
