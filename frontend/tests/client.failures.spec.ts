// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import { openSignedIn, test } from './client.harness';
import * as drafts from './fixtures/drafts';
import * as notifications from './fixtures/notifications';
import * as outbox from './fixtures/outbox';

// The three failures that land after the request asking for them had already answered, followed from the notification
// each raised to the screen where it is decided: a refused send taken back to edit, a change the mailbox did not take
// let go of on its message, and a draft out of step with its server carried on with. Each is asserted by what the next
// screen holds and by what the page asked the deployment to do; what one row draws is the unit suite's.

function centre(page: Page): Locator {
    return page.getByRole('dialog', { name: 'Notifications' });
}

function row(page: Page, headline: string): Locator {
    return centre(page).getByRole('listitem').filter({ hasText: headline });
}

async function openCentre(page: Page): Promise<void> {
    await page.getByRole('button', { name: /^\d+ unread notifications?$/u }).click();
    await expect(centre(page)).toBeVisible();
}

test('takes a refused send back to edit from its notification, as the draft it was written in', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page);
    await openCentre(page);

    await row(page, 'Not sent - it waits in the Outbox').getByRole('button', { name: 'Edit' }).click();

    await expect(centre(page)).toBeHidden();
    await expect(page.getByText('Back in Drafts')).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Subject' })).toHaveValue(outbox.refusedOutgoingSend.subject);
    await expect
        .poll(() => deployment.requests('POST', '/outbox/reopening'))
        .toMatchObject([{ body: { outgoingEmail: outbox.refusedOutgoingSend.outgoingEmail } }]);
    await expect.poll(() => deployment.requests('GET', `/drafts/${drafts.reopenedDraft.draftId}`)).toHaveLength(1);
    await expect
        .poll(() =>
            deployment.requests('POST', `/notifications/${notifications.refusedSendNotification.id}/read-state`),
        )
        .toMatchObject([{ body: { read: true } }]);
});

test('lets go of a change the mailbox did not take on the message it was asked of, which reads its notification', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page);
    await openCentre(page);

    await row(page, 'A change did not reach your mailbox')
        .getByRole('button', { name: /A change did not reach your mailbox/u })
        .click();

    const notice = page.getByRole('status').filter({ hasText: 'Not moved - your mailbox did not take this change' });

    await expect(notice).toBeVisible();
    expect(
        deployment.requests('POST', `/notifications/${notifications.abandonedChangeNotification.id}/read-state`),
    ).toHaveLength(0);

    await notice.getByRole('button', { name: 'Let it go' }).click();

    await expect(notice).toBeHidden();
    await expect
        .poll(() =>
            deployment.requests('POST', `/notifications/${notifications.abandonedChangeNotification.id}/read-state`),
        )
        .toMatchObject([{ body: { read: true } }]);
});

test('opens a draft out of step with its server in the composer, saying so above its recipients', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page);
    await openCentre(page);

    await row(page, 'Draft out of step with your server')
        .getByRole('button', { name: /Draft out of step with your server/u })
        .click();

    await expect(page.getByText(/^Your mail server's copy of this draft may be missing or out of date/u)).toBeVisible();
    await expect(page.getByRole('textbox', { name: 'Subject' })).toHaveValue(drafts.outOfStepDraft.subject);
    await expect.poll(() => deployment.requests('GET', `/drafts/${drafts.outOfStepDraft.draftId}`)).toHaveLength(1);
});
