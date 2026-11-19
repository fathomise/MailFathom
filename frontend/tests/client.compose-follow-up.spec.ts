// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { holdTheDay, openSignedInAsDeveloper, test } from './client.harness';
import type { FakeDeployment } from './fakeDeployment';
import { conversationId } from './fixtures/mail';

// A message that wants an answer waits for one: chosen under *Sending options* › *If nobody replies*, a send writes a
// waiting task down for the person it went to, belonging to the thread a reply answers and to nothing for a message of
// its own, and the Tasks space lists it among what is waited for.

test.use({ timezoneId: 'UTC' });

const subject = 'The racking quote';
const recipient = 'sales@nordwind.example';

/** Signs in as a credential that may write and send mail, in developer mode so Tasks is offered beside Mail. */
async function openMailWriting(page: Page, deployment: FakeDeployment): Promise<Date> {
    deployment.grant('mailfathom.mail.drafts.write', 'mailfathom.mail.send');

    const today = await holdTheDay(page, deployment);

    // The channel is what tells the Tasks space, standing mounted beside Mail, that a task was written down.
    deployment.serveSignals();
    await openSignedInAsDeveloper(page, '/#/mail');
    await expect.poll(() => deployment.channelsOpen()).toBe(1);

    return today;
}

/** Chooses one day under *If nobody replies* and reads the line the composer then shows. */
async function waitForAReply(page: Page, day: RegExp): Promise<void> {
    await page.getByRole('button', { name: 'Sending options' }).click();
    await page.getByRole('menuitemradio', { name: day }).click();

    await expect(
        page.getByText(/^If nobody replies by .+, this waits in Tasks and reminds you that day\.$/u),
    ).toBeVisible();
}

async function writeAndSend(page: Page, words: string): Promise<void> {
    await page.getByRole('textbox', { name: 'Message' }).click();
    await page.keyboard.type(words);
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

    await expect(
        page.getByRole('list', { name: 'Notices' }).getByRole('listitem').filter({ hasText: 'Queued to go out.' }),
    ).toBeVisible();
}

function dayAfter(today: Date, days: number): string {
    return new Date(today.getTime() + days * 24 * 60 * 60 * 1000).toISOString().slice(0, 10);
}

test('a reply sent with "Remind me tomorrow" waits in Tasks for its recipient', async ({ page, deployment }) => {
    const today = await openMailWriting(page, deployment);

    await page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: new RegExp(subject, 'u') })
        .click();
    await expect(
        page.getByRole('region', { name: 'Conversation' }).getByRole('heading', { name: subject }),
    ).toBeVisible();
    await page
        .getByRole('toolbar', { name: 'Mail actions' })
        .getByRole('button', { name: 'Reply', exact: true })
        .click();
    await expect(page.getByRole('button', { name: `Remove ${recipient} from To` })).toBeVisible();

    await waitForAReply(page, /^Remind me tomorrow/u);
    await writeAndSend(page, 'The ninth works for the yard.');

    await expect.poll(() => deployment.requests('POST', '/tasks').length).toBe(1);
    expect(deployment.requests('POST', '/tasks').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            title: 'Reply from Nordwind',
            direction: 'Awaited',
            dueOn: dayAfter(today, 1),
            anchor: { kind: 'Thread', id: conversationId, label: subject },
            counterpart: { address: recipient, displayName: 'Nordwind' },
        }),
    ]);

    await page.evaluate(`window.location.replace('/#/tasks')`);
    await page.getByRole('radio', { name: /^Waiting for/u }).press('Space');

    await expect(page.getByRole('region', { name: 'Waiting', exact: true }).getByRole('listitem')).toContainText([
        /Reply from Nordwind/u,
    ]);
});

test('a new message sent with a follow-up waits in Tasks with no thread', async ({ page, deployment }) => {
    const today = await openMailWriting(page, deployment);

    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await page.getByRole('combobox', { name: 'To', exact: true }).fill(recipient);
    await page.getByRole('combobox', { name: 'To', exact: true }).press('Enter');
    await page.getByLabel('Subject').fill('Racking for the yard');

    await waitForAReply(page, /^Remind me in a week/u);
    await writeAndSend(page, 'The racking arrives on the ninth.');

    await expect.poll(() => deployment.requests('POST', '/tasks').length).toBe(1);
    expect(deployment.requests('POST', '/tasks').map(({ body }) => body)).toStrictEqual([
        expect.objectContaining({
            title: `Reply from ${recipient}`,
            direction: 'Awaited',
            dueOn: dayAfter(today, 7),
            anchor: null,
            counterpart: { address: recipient, displayName: null },
        }),
    ]);
});
