// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { actingGrants, holdTheClock, openSignedIn, openWithTheChannel, test } from './client.harness';
import * as outbox from './fixtures/outbox';

// Each account's Outbox, as journeys across the folder tree, the list, the preview, and the corner: the mark its Sent
// row carries, what opening it puts in the list's place and the pane's, that nothing in it is a mail act, and what a
// decision about a send does to the list and to the mark at once. A unit test of either surface proves what it draws
// for a page it was handed; whether the mark on another surface moves with it is the seam these stand on.

/** The mark on the corpus mailbox's Sent row, named by what it counts. */
function mark(page: Page, said: string) {
    return page.getByRole('tree', { name: 'Mailboxes and folders' }).getByRole('button', { name: said, exact: true });
}

/** Any Outbox mark at all, whatever it counts. */
function anyMark(page: Page) {
    return page.getByRole('tree', { name: 'Mailboxes and folders' }).getByRole('button', { name: /^Outbox - /u });
}

const resting = 'Outbox - 4 waiting to leave, 1 refused';

function sends(page: Page) {
    return page.getByRole('list', { name: 'Outbox' });
}

test('opens the Outbox from the mark on the Sent row and reads a send in it, offering no mail act anywhere', async ({
    page,
    deployment,
}) => {
    // Every act a message offers is granted, so an act missing from the Outbox is missing because it is the Outbox.
    deployment.grant(...actingGrants, 'mailfathom.mail.drafts.write', 'mailfathom.mail.send');
    await openSignedIn(page, '/#/mail');

    await mark(page, resting).click();

    await expect(page.getByRole('heading', { name: 'Outbox', level: 2 })).toBeVisible();
    await expect(page.getByText(/^Read-only\. What has not left yet waits here/u)).toBeVisible();
    await expect(sends(page).getByRole('listitem')).toHaveCount(5);
    await expect(mark(page, resting)).toHaveAttribute('aria-current', 'true');

    const refused = sends(page).getByRole('button', { name: new RegExp(outbox.refusedOutgoingSend.subject, 'u') });

    await refused.click({ button: 'right' });
    await expect(page.getByRole('menu')).toHaveCount(0);

    await refused.click();

    await expect(page.getByRole('heading', { name: outbox.refusedOutgoingSend.subject })).toBeVisible();
    await expect(page.getByText('Not sent - the server refused it')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Send again' })).toBeVisible();
    expect(deployment.requests('GET', `/outbox/${outbox.refusedOutgoingSend.outgoingEmail}`)).toHaveLength(1);

    for (const act of ['Reply', 'Reply all', 'Forward', 'Archive', 'Delete', 'Move', 'Flag']) {
        await expect(page.getByRole('button', { name: act, exact: true })).toHaveCount(0);
    }
    await expect(page.getByRole('checkbox')).toHaveCount(0);

    // Pressing the Sent row itself is choosing the folder, which is what closes the Outbox it was opened on.
    await page.getByRole('tree', { name: 'Mailboxes and folders' }).getByText('Sent', { exact: true }).click();

    await expect(sends(page)).toHaveCount(0);
    await expect(page.getByText('There is no mail in this folder.')).toBeVisible();
});

test('withdraws a waiting send from its preview, and the list and the mark both let it go', async ({
    page,
    deployment,
}) => {
    deployment.grant('mailfathom.mail.send');
    await page.clock.install();
    await openWithTheChannel(page, deployment);
    await holdTheClock(page);

    await mark(page, resting).click();
    await sends(page)
        .getByRole('button', { name: new RegExp(outbox.waitingSend.subject, 'u') })
        .click();
    await page.getByRole('button', { name: 'Cancel sending' }).click();

    await expect(page.getByRole('list', { name: 'Notices' })).toContainText('Sending cancelled');
    await expect(sends(page).getByRole('listitem')).toHaveCount(4);
    await expect(mark(page, 'Outbox - 3 waiting to leave, 1 refused')).toBeVisible();
    expect(deployment.requests('POST', '/outbox/cancellation').map(({ body }) => body)).toStrictEqual([
        { outgoingEmail: outbox.waitingSend.outgoingEmail },
    ]);
});

test('takes the mark off the Sent row once the last send has left, as the deployment says each one did', async ({
    page,
    deployment,
}) => {
    deployment.grant('mailfathom.mail.send');
    await page.clock.install();
    await openWithTheChannel(page, deployment);
    await holdTheClock(page);

    await expect(mark(page, resting)).toBeVisible();

    deployment.sendLeaves(outbox.refusedOutgoingSend.outgoingEmail);

    await expect(mark(page, 'Outbox - 4 waiting to leave')).toBeVisible();

    for (const send of [outbox.waitingSend, outbox.sendingSend, outbox.retryingSend, outbox.unknownSend]) {
        deployment.sendLeaves(send.outgoingEmail);
    }

    await expect(anyMark(page)).toHaveCount(0);
    await expect(
        page.getByRole('tree', { name: 'Mailboxes and folders' }).getByRole('treeitem', { name: 'Sent', exact: true }),
    ).toBeVisible();
});

test('leads from the notice a send leaves to that send, open in its account’s Outbox', async ({ page, deployment }) => {
    deployment.grant('mailfathom.mail.drafts.write', 'mailfathom.mail.send');
    await openSignedIn(page, '/#/mail');

    await page.getByRole('button', { name: 'New message', exact: true }).first().click();
    await page.getByRole('combobox', { name: 'To', exact: true }).fill('yard@example.invalid');
    await page.getByRole('combobox', { name: 'To', exact: true }).press('Enter');
    await page.getByLabel('Subject').fill('The yard on Tuesday');
    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

    await page.getByRole('list', { name: 'Notices' }).getByRole('button', { name: 'Show in Outbox' }).click();

    await expect(page.getByRole('heading', { name: 'The yard on Tuesday' })).toBeVisible();
    await expect(page.getByText('Waiting to send').first()).toBeVisible();
    await expect(anyMark(page)).toHaveAttribute('aria-current', 'true');
});
