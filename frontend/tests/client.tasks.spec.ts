// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { holdTheDay, openSignedInAsDeveloper, switchDeveloperModeOn, test } from './client.harness';
import { corpusEventId } from './fixtures/calendar';
import { conversationId } from './fixtures/mail';

// The Tasks space as somebody works it: the four views over the corpus's commitments, a task written down, finished,
// shown again and deleted, a task's day, line and link changed in its dialog and still changed after a reload, a wait a
// reply closed kept waiting, what a task belongs to opened, a proposal dismissed, and a wait written down. The list
// reads itself again after a write rather than correcting what it holds, and the capacity counts off that same read —
// so the two agree only where both drew the deployment's answer.

// The corpus deployment states Greenwich as the reader's zone, and the reader sits in it, so the day the due-day field
// is filled with is the day the list files under *Today*.
test.use({ timezoneId: 'UTC' });

const written = 'Book the carrier for Friday';

/**
 * Moves to one of the four views, which the head draws as one choice among four. Chosen from the keyboard, because the
 * radio itself is drawn inside the label that names it and a pointer lands on the label.
 */
async function showView(page: Page, name: string): Promise<void> {
    await page.getByRole('radio', { name: new RegExp(`^${name}`, 'u') }).press('Space');
}

/** The titles one group of the view shown draws, in the order it draws them. */
function titlesIn(page: Page, group: string) {
    return page.getByRole('region', { name: group, exact: true }).getByRole('listitem');
}

test('the four views show the commitments the deployment holds', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');

    await expect(page.getByRole('heading', { level: 2, name: /^\w+day, /u })).toBeVisible();
    await expect(titlesIn(page, 'Planned')).toHaveText([
        /^Send the 5% CPI cap counter-proposal/u,
        /^Reply to Marta about the calculation/u,
    ]);
    await expect(titlesIn(page, 'Planned').first()).toContainText('Contract addendum - signatures');
    await expect(titlesIn(page, 'Planned').first()).toContainText(/Clashes with /u);
    await expect(titlesIn(page, 'Due today, not planned')).toHaveText([/^Book the meeting room/u]);
    await expect(titlesIn(page, 'Due today, not planned')).toContainText('10 min');
    await expect(titlesIn(page, 'Follow up')).toHaveText([/^Delivery confirmation from Tomasz Bąk/u]);

    await showView(page, 'Upcoming');

    await expect(page.getByRole('heading', { name: 'Coming up' })).toBeVisible();
    await expect(titlesIn(page, 'Tomorrow')).toHaveText([/^Review the addendum before the call/u]);
    await expect(titlesIn(page, 'This week')).toHaveText([
        /^Answer Adventure Works on weekend SLA/u,
        /^Confirm the Fabrikam delivery date/u,
    ]);
    await expect(titlesIn(page, 'Later')).toHaveText([
        /^Prepare the IT contract list for audit/u,
        /^Send Anna the Gdańsk stock count/u,
        /^Sign the Northwind NDA/u,
    ]);
    await expect(titlesIn(page, 'No day')).toHaveText([/^Draft the onboarding checklist for Q4 hires/u]);

    await showView(page, 'Waiting for');

    await expect(page.getByRole('heading', { name: 'Waiting for others' })).toBeVisible();
    await expect(titlesIn(page, 'Follow up today')).toHaveText([/^Delivery confirmation from Tomasz Bąk/u]);
    await expect(titlesIn(page, 'Waiting')).toHaveText([
        /^Signed addendum from Anna Kowalska/u,
        /^Racking quote from Anna Marlow/u,
    ]);
    await expect(titlesIn(page, 'Closed by a reply')).toHaveText([/Jacek Wrona replied at 9:12/u]);
    await expect(page.getByText('A waiting task closes itself when the reply arrives.')).toBeVisible();

    await showView(page, 'Proposed');

    await expect(page.getByRole('heading', { name: 'Proposed to you' })).toBeVisible();
    await expect(titlesIn(page, 'Today')).toHaveText([/^AISend Piotr the rollout risks/u]);
    await expect(titlesIn(page, 'Later')).toHaveText([/^AgentAsk Marta for the 2026 baseline figures/u]);
    await expect(titlesIn(page, 'No day')).toHaveText([/^AIBook the auditor's visit/u]);
    await expect(titlesIn(page, 'Later')).toContainText('From the Agent');
});

/** Opens a task's dialog from the line its row draws. */
async function openTask(page: Page, title: string) {
    await page.getByRole('button', { name: title, exact: true }).click();
}

/** Reads the screen again after a reload, which forgets developer mode and the space it stood on. */
async function reloadTasks(page: Page): Promise<void> {
    await page.reload();
    await switchDeveloperModeOn(page);
    await page.evaluate(`window.location.replace('/#/tasks')`);
}

test("a task's day applies as it is chosen, its line on Save, and both stay after reload", async ({
    page,
    deployment,
}) => {
    const today = await holdTheDay(page, deployment);
    const tomorrow = new Date(today.getTime() + 24 * 60 * 60 * 1000).toISOString().slice(0, 10);

    await openSignedInAsDeveloper(page, '/#/tasks');
    await openTask(page, 'Book the meeting room for the Fabrikam review');

    const dialog = page.getByRole('dialog', { name: 'Task' });

    await dialog.getByRole('radio', { name: 'Tomorrow', exact: true }).press('Space');

    // Written as it was pressed, with the dialog still open on the task.
    await expect.poll(() => deployment.requests('PUT', '/tasks/task-room').length).toBe(1);
    await expect(dialog).toBeVisible();

    await dialog.getByLabel('What is it?').fill('Book the large meeting room for the Fabrikam review');
    await dialog.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Saved.' })).toBeVisible();
    await expect(dialog).toHaveCount(0);

    const [chosen, saved] = deployment.requests('PUT', '/tasks/task-room');

    expect(chosen?.body).toMatchObject({
        title: 'Book the meeting room for the Fabrikam review',
        dueOn: tomorrow,
        estimatedMinutes: 10,
    });
    expect(saved?.body).toMatchObject({
        title: 'Book the large meeting room for the Fabrikam review',
        dueOn: tomorrow,
        direction: 'Owed',
        estimatedMinutes: 10,
    });

    await reloadTasks(page);
    await showView(page, 'Upcoming');

    await expect(titlesIn(page, 'Tomorrow')).toHaveText([
        /^Book the large meeting room for the Fabrikam review/u,
        /^Review the addendum before the call/u,
    ]);
});

test('a link removed in the dialog goes at once, and the person it is with is what it belongs to after reload', async ({
    page,
    deployment,
}) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');
    await openTask(page, 'Send the 5% CPI cap counter-proposal');

    const dialog = page.getByRole('dialog', { name: 'Task' });

    await dialog.getByRole('button', { name: 'Remove the link to Contract addendum - signatures' }).click();

    await expect(dialog.getByText('Anna Kowalska', { exact: true })).toBeVisible();
    expect(deployment.requests('PUT', '/tasks/task-tender').map(({ body }) => body)).toMatchObject([
        { anchor: null, counterpart: { address: 'anna@contoso.example' } },
    ]);

    await page.keyboard.press('Escape');
    await reloadTasks(page);

    const tender = titlesIn(page, 'Planned').first();

    await expect(tender).toContainText('Anna Kowalska');
    await expect(tender).not.toContainText('Contract addendum - signatures');
});

test('a wait a reply answered cannot be turned round, and keeps waiting from its dialog', async ({
    page,
    deployment,
}) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');
    await showView(page, 'Waiting for');
    await openTask(page, 'Opinion on the indexation cap from Jacek Wrona');

    const dialog = page.getByRole('dialog', { name: 'Waiting for' });

    await dialog.getByRole('radio', { name: 'I owe this' }).press('Space');

    await expect(
        dialog.getByText('A reply already answered this task, so which way it runs cannot change.'),
    ).toBeVisible();
    await expect(dialog.getByRole('radio', { name: 'I am waiting for this' })).toBeChecked();

    await dialog.getByRole('button', { name: 'Keep waiting' }).click();

    // Still open, on a wait no reply has closed.
    await expect(dialog.getByRole('button', { name: 'Keep waiting' })).toHaveCount(0);
    await expect(dialog.getByText('Closes itself when the reply arrives in the thread.')).toBeVisible();
    expect(deployment.requests('POST', '/tasks/wait-opinion/completion').map(({ body }) => body)).toStrictEqual([
        { completed: false },
    ]);

    await page.keyboard.press('Escape');

    await expect(titlesIn(page, 'Follow up today')).toContainText([/^Opinion on the indexation cap/u]);
    await expect(page.getByRole('region', { name: 'Closed by a reply', exact: true })).toHaveCount(0);
});

test('what a task belongs to opens from its row', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');

    await titlesIn(page, 'Planned')
        .first()
        .getByRole('button', { name: 'Contract addendum - signatures', exact: true })
        .click();

    await expect(page).toHaveURL(/#\/mail/u);
    expect(deployment.requests('GET', `/threads/${conversationId}`)).not.toHaveLength(0);
});

test('an event a task belongs to opens in the calendar with its dialog open', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');
    await showView(page, 'Upcoming');

    await titlesIn(page, 'Tomorrow').first().getByRole('button', { name: 'Carrier review', exact: true }).click();

    await expect(page).toHaveURL(/#\/calendar/u);
    await expect(page.getByRole('dialog', { name: 'Carrier review' })).toBeVisible();
    expect(deployment.requests('GET', `/calendar/${corpusEventId(2)}`)).toHaveLength(1);
});

test('a proposal is dismissed at once', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');
    await showView(page, 'Proposed');

    await titlesIn(page, 'Today').first().getByRole('button', { name: 'Dismiss task' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Dismissed.' })).toBeVisible();
    await expect(page.getByRole('dialog', { name: 'Delete this task?' })).toHaveCount(0);
    await expect(page.getByRole('region', { name: 'Today', exact: true })).toHaveCount(0);
    expect(deployment.requests('DELETE', '/tasks/task-signatures')).toHaveLength(1);
});

// The design also draws when the message went (*sent Mon 24.08*); the task record carries no such instant, so the row
// draws what it waits on and when to follow up.
test('a waiting task shows what it waits on and when to follow up', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');
    await showView(page, 'Waiting for');

    const waiting = titlesIn(page, 'Follow up today');

    await expect(waiting).toContainText('Q4 delivery dates');
    await expect(waiting).toContainText('follow up today');
    await expect(
        waiting.getByRole('checkbox', { name: 'Mark Delivery confirmation from Tomasz Bąk as received' }),
    ).not.toBeChecked();

    await openTask(page, 'Delivery confirmation from Tomasz Bąk');

    const dialog = page.getByRole('dialog', { name: 'Waiting for' });

    await expect(dialog.getByText('Thread', { exact: true })).toBeVisible();
    await expect(dialog.getByText('Q4 delivery dates')).toBeVisible();
    await expect(dialog.getByRole('radio', { name: 'I am waiting for this' })).toBeChecked();
    await expect(dialog.getByText('Closes itself when the reply arrives in the thread.')).toBeVisible();
    await expect(dialog.getByRole('button', { name: 'Mark as received' })).toBeVisible();
});

test('a new task made in Tasks can wait for somebody', async ({ page, deployment }) => {
    const today = (await holdTheDay(page, deployment)).toISOString().slice(0, 10);

    await openSignedInAsDeveloper(page, '/#/tasks');
    await page.getByRole('button', { name: 'New task' }).click();

    const writing = page.getByRole('dialog', { name: 'New task' });

    await writing.getByRole('radio', { name: 'I am waiting for this' }).press('Space');

    const waiting = page.getByRole('dialog', { name: 'New waiting task' });

    await waiting.getByLabel('Task description').fill('Signed quote from Nordwind');
    await waiting.getByLabel('Due date').fill(today);
    await waiting.getByRole('button', { name: 'Add task' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Written down.' })).toBeVisible();
    await expect(titlesIn(page, 'Follow up')).toHaveText([
        /^Delivery confirmation from Tomasz Bąk/u,
        /^Signed quote from Nordwind/u,
    ]);

    expect(deployment.requests('POST', '/tasks').map(({ body }) => body)).toStrictEqual([
        {
            title: 'Signed quote from Nordwind',
            dueOn: today,
            reminders: [],
            dueDayOffsetMinutes: null,
            sourceMessageId: null,
            direction: 'Awaited',
            estimatedMinutes: null,
            plan: null,
            anchor: null,
            counterpart: null,
        },
    ]);

    await showView(page, 'Waiting for');

    await expect(titlesIn(page, 'Follow up today')).toContainText([/^Signed quote from Nordwind/u]);
});

test('moves a task it wrote, finished, and deleted between the groups, and the day beside them agrees', async ({
    page,
    deployment,
}) => {
    const today = (await holdTheDay(page, deployment)).toISOString().slice(0, 10);

    await openSignedInAsDeveloper(page, '/#/tasks');

    const dueToday = page.getByRole('region', { name: 'Due today, not planned' });
    const capacity = page.getByRole('region', { name: 'Day capacity' });

    await expect(
        dueToday.getByRole('checkbox', { name: 'Mark Book the meeting room for the Fabrikam review as done' }),
    ).toBeVisible();
    await expect(capacity).toContainText('3 tasks are due today.');

    await page.getByRole('button', { name: 'New task' }).click();

    const writing = page.getByRole('dialog', { name: 'New task' });

    await writing.getByLabel('Task description').fill(written);
    await writing.getByLabel('Due date').fill(today);
    await writing.getByLabel('Duration (e.g. 30 min)').fill('30 min');
    await writing.getByRole('button', { name: 'Add task' }).click();

    await expect(dueToday.getByRole('checkbox', { name: `Mark ${written} as done` })).toBeVisible();
    await expect(dueToday).toContainText('30 min');
    await expect(capacity).toContainText('4 tasks are due today.');

    expect(deployment.requests('POST', '/tasks').map(({ body }) => body)).toStrictEqual([
        {
            title: written,
            dueOn: today,
            reminders: [],
            dueDayOffsetMinutes: null,
            sourceMessageId: null,
            direction: 'Owed',
            estimatedMinutes: 30,
            plan: null,
            anchor: null,
            counterpart: null,
        },
    ]);

    // Finished work leaves the list while done tasks are hidden, and stops counting against the day.
    // Pressed rather than checked: `check` reads the box again afterwards, and by then the row may have left the list.
    await dueToday.getByRole('checkbox', { name: `Mark ${written} as done` }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Marked as done.' })).toBeVisible();
    await expect(capacity).toContainText('3 tasks are due today.');
    await expect(page.getByRole('checkbox', { name: new RegExp(`^Mark ${written} as`, 'u') })).toHaveCount(0);

    const [completion] = deployment.issued.filter(
        ({ method, route }) => method === 'POST' && route.endsWith('/completion'),
    );

    expect(completion?.body).toStrictEqual({ completed: true });

    // Shown again under the day it is due on, marked as done, and still not counted.
    await page.getByRole('button', { name: 'Show done' }).click();

    await expect(dueToday.getByRole('checkbox', { name: `Mark ${written} as not done` })).toBeChecked();
    await expect(capacity).toContainText('3 tasks are due today.');

    await dueToday
        .getByRole('listitem')
        .filter({ has: page.getByRole('checkbox', { name: `Mark ${written} as not done` }) })
        .click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Delete task' }).click();
    await page.getByRole('dialog', { name: 'Delete this task?' }).getByRole('button', { name: 'Delete' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Deleted.' })).toBeVisible();
    await expect(page.getByRole('checkbox', { name: new RegExp(`^Mark ${written} as`, 'u') })).toHaveCount(0);
    await expect(capacity).toContainText('3 tasks are due today.');

    // The task deleted is the one finished, which is the one the write created.
    expect(deployment.issued.filter(({ method }) => method === 'DELETE').map(({ route }) => route)).toStrictEqual([
        completion?.route.replace(/\/completion$/u, ''),
    ]);
});

/** The hour of the day beside the list a task is dropped on, named by the clock time it opens at. */
function hourOfTheDay(page: Page, label: string) {
    return page
        .getByRole('complementary')
        .getByRole('listitem')
        .filter({ hasText: new RegExp(`^${label}`, 'u') });
}

test('a task planned from its dialog is still planned after reload', async ({ page, deployment }) => {
    const today = (await holdTheDay(page, deployment)).toISOString().slice(0, 10);

    await openSignedInAsDeveloper(page, '/#/tasks');
    await openTask(page, 'Book the meeting room for the Fabrikam review');

    const dialog = page.getByRole('dialog', { name: 'Task' });

    await expect(dialog).toContainText('Not planned');
    await dialog.getByRole('button', { name: 'Plan', exact: true }).click();

    const planning = page.getByRole('dialog', { name: 'Plan a time' });

    await planning.getByRole('button', { name: /^2:00 PM/u }).click();
    await expect(planning).toContainText(/Planned today, 2:00/u);
    await planning.getByRole('button', { name: 'Plan', exact: true }).click();

    await expect(planning).toHaveCount(0);
    await expect(dialog).toContainText(/2:00\s?–\s?2:10\sPM today/u);
    await expect.poll(() => deployment.requests('PUT', '/tasks/task-room').length).toBe(1);
    expect(deployment.requests('PUT', '/tasks/task-room')[0]?.body).toMatchObject({
        title: 'Book the meeting room for the Fabrikam review',
        estimatedMinutes: 10,
        plan: { startAt: `${today}T14:00:00.000Z`, minutes: 10 },
    });

    await dialog.getByRole('button', { name: 'Close' }).click();
    await reloadTasks(page);

    await expect(titlesIn(page, 'Planned')).toHaveText([
        /^Send the 5% CPI cap counter-proposal/u,
        /^Book the meeting room for the Fabrikam review/u,
        /^Reply to Marta about the calculation/u,
    ]);
    await expect(page.getByRole('region', { name: 'Due today, not planned' })).toHaveCount(0);
});

test('dragging a task onto an hour plans it there', async ({ page, deployment }) => {
    const today = (await holdTheDay(page, deployment)).toISOString().slice(0, 10);

    await openSignedInAsDeveloper(page, '/#/tasks');

    await expect(page.getByText('Drag a task onto an hour to plan it.')).toBeVisible();
    await titlesIn(page, 'Due today, not planned').first().dragTo(hourOfTheDay(page, '2:00 PM'));

    await expect(page.getByRole('status').filter({ hasText: /planned at 2:00 PM/u })).toBeVisible();
    expect(deployment.requests('PUT', '/tasks/task-room').map(({ body }) => body)).toMatchObject([
        { plan: { startAt: `${today}T14:00:00.000Z`, minutes: 10 } },
    ]);
    await expect(hourOfTheDay(page, '2:00 PM')).toContainText('Book the meeting room for the Fabrikam review');
    await expect(titlesIn(page, 'Planned')).toContainText([/^Book the meeting room for the Fabrikam review/u]);
});

test('"Plan these" plans each proposed placement and "Not now" plans nothing', async ({ page, deployment }) => {
    const today = (await holdTheDay(page, deployment)).toISOString().slice(0, 10);

    await openSignedInAsDeveloper(page, '/#/tasks');

    const capacity = page.getByRole('region', { name: 'Day capacity' });

    await capacity.getByRole('button', { name: 'Lay out today' }).click();
    await expect(capacity).toContainText('Here is a way to fit the rest of today');
    await expect(capacity).toContainText('Book the meeting room for the Fabrikam review');
    await expect(capacity).toContainText(/moved from 3:00 PM: it clashed with /u);
    await expect(capacity).toContainText('One task did not fit today: Prepare the IT contract list for audit.');

    await capacity.getByRole('button', { name: 'Not now' }).click();

    await expect(capacity.getByRole('button', { name: 'Plan these' })).toHaveCount(0);
    expect(deployment.requests('PUT', '/tasks/task-room')).toHaveLength(0);
    expect(deployment.requests('PUT', '/tasks/task-calculation')).toHaveLength(0);

    await capacity.getByRole('button', { name: 'Lay out today' }).click();
    await capacity.getByRole('button', { name: 'Plan these' }).click();

    await expect(page.getByRole('status').filter({ hasText: '2 tasks planned into today.' })).toBeVisible();
    expect(deployment.requests('PUT', '/tasks/task-room').map(({ body }) => body)).toMatchObject([
        { plan: { startAt: `${today}T10:00:00+00:00`, minutes: 10 } },
    ]);
    expect(deployment.requests('PUT', '/tasks/task-calculation').map(({ body }) => body)).toMatchObject([
        { plan: { startAt: `${today}T16:00:00+00:00`, minutes: 20 } },
    ]);
    expect(deployment.requests('POST', '/calendar')).toHaveLength(0);
    await expect(titlesIn(page, 'Planned')).toContainText([/^Book the meeting room for the Fabrikam review/u]);
});

test('"Lay out the day" in the toolbar brings Today up with the day already laid out', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');

    await showView(page, 'Upcoming');
    await page.getByRole('button', { name: 'Lay out the day' }).click();

    await expect(page.getByRole('radio', { name: /^Today/u })).toBeChecked();
    await expect(page.getByRole('region', { name: 'Day capacity' })).toContainText(
        'Here is a way to fit the rest of today',
    );
    expect(deployment.requests('PUT', '/tasks/task-room')).toHaveLength(0);
});

test.describe('driven by a finger', () => {
    test.use({ hasTouch: true });

    test('on a touch screen a task is planned from "Plan…" instead of dragging', async ({ page, deployment }) => {
        const today = (await holdTheDay(page, deployment)).toISOString().slice(0, 10);

        await openSignedInAsDeveloper(page, '/#/tasks');

        const row = titlesIn(page, 'Due today, not planned').first();

        await expect(page.getByText('Plan a task from its own Plan.')).toBeVisible();
        await expect(page.getByText('Drag a task onto an hour to plan it.')).toBeHidden();
        await expect(row).not.toHaveAttribute('draggable', 'true');

        await row.getByRole('button', { name: 'Plan', exact: true }).tap();

        const planning = page.getByRole('dialog', { name: 'Plan a time' });

        await planning.getByRole('button', { name: /^9:00 AM/u }).tap();
        await planning.getByRole('button', { name: 'Plan', exact: true }).tap();

        await expect(planning).toHaveCount(0);
        await expect.poll(() => deployment.requests('PUT', '/tasks/task-room').length).toBe(1);
        expect(deployment.requests('PUT', '/tasks/task-room')[0]?.body).toMatchObject({
            plan: { startAt: `${today}T09:00:00.000Z`, minutes: 10 },
        });
        await expect(titlesIn(page, 'Planned')).toContainText([/^Book the meeting room for the Fabrikam review/u]);
    });
});
