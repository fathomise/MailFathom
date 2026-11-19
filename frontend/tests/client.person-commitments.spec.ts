// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { holdTheDay, openSignedInAsDeveloper, test } from './client.harness';

// What the reader and one person owe each other, read on that person's page: the open tasks with them on two sides,
// each opening its task, and *Add a task* writing one down with them. The address book's first person writes from two
// addresses, and the corpus holds a task with her under each, so both sides are reached without writing anything first.

test.use({ timezoneId: 'UTC' });

/** Anna Marlow's page, opened from the reader's own book. */
async function openAnnasPage(page: Page) {
    await page.getByRole('radio', { name: 'Own' }).press('Space');
    await page
        .getByRole('listbox', { name: 'Contacts' })
        .getByRole('option', { name: /Anna Marlow/u })
        .click();

    const person = page.getByRole('region', { name: 'Anna Marlow' });

    await expect(person.getByRole('heading', { name: 'Commitments' })).toBeVisible();

    return person;
}

test("a person's page lists what you owe them and what they owe you", async ({ page, deployment }) => {
    deployment.grant('mailfathom.contacts.read');
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/people');

    const person = await openAnnasPage(page);

    await expect(
        person
            .getByRole('list', { name: 'You owe Anna' })
            .getByRole('button', { name: /^Send Anna the Gdańsk stock count/u }),
    ).toBeVisible();
    await expect(
        person
            .getByRole('list', { name: 'Anna owes you' })
            .getByRole('button', { name: /^Racking quote from Anna Marlow/u }),
    ).toBeVisible();
    expect(deployment.requests('GET', '/tasks').map(({ query }) => query.get('counterpart'))).toEqual(
        expect.arrayContaining(['anna@nordwind.example', 'a.marlow@nordwind.example']),
    );

    await person.getByRole('button', { name: /^Racking quote from Anna Marlow/u }).click();

    await expect(page.getByRole('dialog', { name: 'Waiting for' }).getByLabel('What is it?')).toHaveValue(
        'Racking quote from Anna Marlow',
    );
});

test('"Add a task" on a person\'s page makes a task with them', async ({ page, deployment }) => {
    deployment.grant('mailfathom.contacts.read');
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/people');

    const person = await openAnnasPage(page);

    await person.getByRole('button', { name: 'Add a task' }).click();

    const writing = page.getByRole('dialog', { name: 'New task' });

    await expect(writing.getByText('Anna Marlow')).toBeVisible();

    await writing.getByLabel('Task description').fill('Send Anna the yard plan');
    await writing.getByRole('button', { name: 'Add task' }).click();

    await expect(
        person.getByRole('list', { name: 'You owe Anna' }).getByRole('button', { name: /^Send Anna the yard plan/u }),
    ).toBeVisible();
    expect(deployment.requests('POST', '/tasks').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            title: 'Send Anna the yard plan',
            direction: 'Owed',
            counterpart: { address: 'anna@nordwind.example', displayName: 'Anna Marlow' },
        }),
    ]);
});
