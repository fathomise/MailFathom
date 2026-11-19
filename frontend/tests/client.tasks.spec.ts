// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { holdTheDay, openSignedInAsDeveloper, switchDeveloperModeOn, test } from './client.harness';

// The Tasks space as somebody works it: the four views over the corpus's commitments, a task written down, finished,
// shown again and deleted, a task's line and day changed in its dialog and still changed after a reload, and a wait
// written down and drawn with who it waits on. The list reads itself again after a write rather than correcting what
// it holds, and the capacity counts off that same read — so the two agree only where both drew the deployment's answer.

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
        /^Sign the Northwind NDA/u,
    ]);
    await expect(titlesIn(page, 'No day')).toHaveText([/^Draft the onboarding checklist for Q4 hires/u]);

    await showView(page, 'Waiting for');

    await expect(page.getByRole('heading', { name: 'Waiting for others' })).toBeVisible();
    await expect(titlesIn(page, 'Follow up today')).toHaveText([/^Delivery confirmation from Tomasz Bąk/u]);
    await expect(titlesIn(page, 'Waiting')).toHaveText([/^Signed addendum from Anna Kowalska/u]);
    await expect(titlesIn(page, 'Closed by a reply')).toHaveText([/Jacek Wrona replied at 9:12/u]);
    await expect(page.getByText('A waiting task closes itself when the reply arrives.')).toBeVisible();

    await showView(page, 'Proposed');

    await expect(page.getByRole('heading', { name: 'Proposed to you' })).toBeVisible();
    await expect(titlesIn(page, 'Today')).toHaveText([/^AISend Piotr the rollout risks/u]);
    await expect(titlesIn(page, 'Later')).toHaveText([/^AgentAsk Marta for the 2026 baseline figures/u]);
    await expect(titlesIn(page, 'No day')).toHaveText([/^AIBook the auditor's visit/u]);
    await expect(titlesIn(page, 'Later')).toContainText('From the Agent');
});

test("a task's title and day can be changed and stay changed after reload", async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');

    await page.getByRole('button', { name: 'Book the meeting room for the Fabrikam review', exact: true }).click();

    const dialog = page.getByRole('dialog', { name: 'Task' });

    await dialog.getByLabel('What is it?').fill('Book the large meeting room for the Fabrikam review');
    await dialog.getByRole('radio', { name: 'Tomorrow' }).press('Space');
    await dialog.getByRole('button', { name: 'Save' }).click();

    await expect(page.getByRole('status').filter({ hasText: 'Saved.' })).toBeVisible();
    await expect(dialog).toHaveCount(0);

    const revised = deployment.requests('PUT', '/tasks/task-room');

    expect(revised).toHaveLength(1);
    expect(revised[0]?.body).toMatchObject({
        title: 'Book the large meeting room for the Fabrikam review',
        direction: 'Owed',
        estimatedMinutes: 10,
    });

    await page.reload();
    await switchDeveloperModeOn(page);
    await page.evaluate(`window.location.replace('/#/tasks')`);
    await showView(page, 'Upcoming');

    await expect(titlesIn(page, 'Tomorrow')).toHaveText([
        /^Book the large meeting room for the Fabrikam review/u,
        /^Review the addendum before the call/u,
    ]);
});

// The design also draws when the message went (*sent Mon 24.08*); the task record carries no such instant, so the row
// draws who it waits on, what it belongs to and when to follow up.
test('a waiting task shows who it waits for and when to follow up', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/tasks');
    await showView(page, 'Waiting for');

    const waiting = titlesIn(page, 'Follow up today');

    await expect(waiting).toContainText('Tomasz Bąk');
    await expect(waiting).toContainText('Q4 delivery dates');
    await expect(waiting).toContainText('follow up today');
    await expect(
        waiting.getByRole('checkbox', { name: 'Mark Delivery confirmation from Tomasz Bąk as received' }),
    ).not.toBeChecked();

    await waiting.getByRole('button', { name: 'Delivery confirmation from Tomasz Bąk', exact: true }).click();

    const dialog = page.getByRole('dialog', { name: 'Waiting for' });

    await expect(dialog.getByText('Person', { exact: true })).toBeVisible();
    await expect(dialog.getByText('Tomasz Bąk')).toBeVisible();
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
    await writing.getByLabel('Duration (minutes)').fill('30');
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
