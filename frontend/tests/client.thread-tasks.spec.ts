// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { holdTheDay, openSignedInAsDeveloper, test } from './client.harness';
import { conversationId } from './fixtures/mail';

// The commitments a thread carries, read where the thread is: one chip per open task under the head's address line,
// each opening its task; *Make a task* among the head's acts, writing a task that belongs to the thread and is about
// its sender; and the list's mark on the row of a thread that carries an open task. The corpus's conversation is the
// thread two of its tasks belong to, so every one of these is reached without writing anything first.

test.use({ timezoneId: 'UTC' });

const subject = 'The racking quote';

/** The conversation opened from its row in the list, which is where the thread's head is drawn. */
async function openTheConversation(page: Page) {
    await page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: new RegExp(subject, 'u') })
        .click();

    const conversation = page.getByRole('region', { name: 'Conversation' });

    await expect(conversation.getByRole('heading', { name: subject })).toBeVisible();

    return conversation;
}

test('a thread shows the tasks that live in it', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/mail');

    const conversation = await openTheConversation(page);

    await expect(conversation.getByRole('button', { name: /^Send the 5% CPI cap counter-proposal/u })).toBeVisible();
    await expect(conversation.getByRole('button', { name: /^Signed addendum from Anna Kowalska/u })).toContainText(
        'follow up tomorrow',
    );
    expect(deployment.requests('GET', '/tasks').map(({ query }) => query.get('threadId'))).toContain(conversationId);

    await conversation.getByRole('button', { name: /^Signed addendum from Anna Kowalska/u }).click();

    const dialog = page.getByRole('dialog', { name: 'Waiting for' });

    await expect(dialog.getByLabel('What is it?')).toHaveValue('Signed addendum from Anna Kowalska');
});

test('"Make a task" in a thread links the new task to that thread', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/mail');

    const conversation = await openTheConversation(page);

    await conversation.getByRole('button', { name: 'Make a task' }).click();

    const writing = page.getByRole('dialog', { name: 'New task' });

    await expect(writing.getByText('Belongs to the thread')).toBeVisible();
    await expect(writing.getByLabel('Task description')).toHaveValue(subject);

    await writing.getByLabel('Task description').fill('Confirm the yard for the ninth');
    await writing.getByRole('button', { name: 'Add task' }).click();

    await expect(conversation.getByRole('button', { name: /^Confirm the yard for the ninth/u })).toBeVisible();

    expect(deployment.requests('POST', '/tasks').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            title: 'Confirm the yard for the ninth',
            direction: 'Owed',
            anchor: { kind: 'Thread', id: conversationId, label: subject },
            counterpart: { address: 'sales@nordwind.example', displayName: 'Nordwind' },
        }),
    ]);
});

test('a thread with an open task is marked in the list', async ({ page, deployment }) => {
    await holdTheDay(page, deployment);
    await openSignedInAsDeveloper(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });

    await expect(list.getByRole('option', { name: new RegExp(subject, 'u') })).toContainText('Has an open task');
    await expect(list.getByRole('option').filter({ hasText: 'Has an open task' })).toHaveCount(1);
});
