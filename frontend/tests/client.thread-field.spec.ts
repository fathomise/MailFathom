// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { declaredVersion, openSignedIn, test } from './client.harness';
import * as deployment from './fixtures/deployment';

// The reply drafter under an open message, and what becomes of the reply it asks for: journeys across the components
// the frame composes around it — the list that opens a message, the card in the thread, and the composer.

const drafterName = 'Draft a reply to this correspondence';
const aiButton = 'Draft a reply or ask about this thread';

/** Unfolds the AI button at the foot of what is open and takes its *Draft a reply* choice. */
async function openTheDrafter(page: Page): Promise<void> {
    await page.getByRole('button', { name: aiButton }).click();
    await page.getByRole('button', { name: 'Draft a reply', exact: true }).click();
}

/**
 * Signs in to Mail as a credential that may write mail, which is what the field under a message is drawn for.
 *
 * The corpus grant reads and asks and writes nothing, so the session is answered here with the grant to write drafts
 * beside it; a route registered on the page takes precedence over the fake deployment's own.
 */
async function openMailWritingDrafts(page: Page): Promise<void> {
    await page.route('**/api/client/session', (route) =>
        route.fulfill({
            status: 200,
            contentType: 'application/json',
            body: JSON.stringify({
                ...deployment.sessionAnswer,
                version: declaredVersion,
                permissions: [...deployment.sessionAnswer.permissions, 'mailfathom.mail.drafts.write'],
            }),
        }),
    );

    await openSignedIn(page, '/#/mail');
}

// The seam only the whole client proves: the composer is handed the draft the card already holds rather than asking
// the deployment for a second one, and drafting takes the reader nowhere.
test('drafts a reply under the message it answers, and opens the composer holding it without asking again', async ({
    page,
    deployment: served,
}) => {
    await openMailWritingDrafts(page);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();

    await openTheDrafter(page);
    await page.getByRole('textbox', { name: drafterName }).fill('confirm the price');
    await page.getByRole('form', { name: drafterName }).getByRole('button', { name: 'Draft a reply' }).click();

    const card = page.getByRole('region', { name: 'Draft · local' });
    await expect(card).toContainText('4 200 net still stands');
    await expect(page).toHaveURL(/#\/mail$/u);

    await card.getByRole('button', { name: 'Open in composer' }).click();

    await expect(page.getByRole('textbox', { name: 'Message' })).toContainText('4 200 net still stands');
    await expect(card).toBeHidden();

    const draftings = served.requests('POST', '/replies/drafting');
    expect(draftings).toHaveLength(1);
    expect(draftings[0]?.body).toMatchObject({ instruction: 'Write a reply to this message. confirm the price' });
});

// What is typed under one message is about that message, so opening another row of the list closes the drafter and
// draws it empty when it is opened again: the list and the drafter are two components, and only the frame that
// composes them decides the drafter starts again.
test('closes the drafter on the next message opened, and opens it there empty', async ({ page }) => {
    await openMailWritingDrafts(page);

    const rows = page.getByRole('listbox', { name: 'Messages' }).getByRole('option');
    const field = page.getByRole('textbox', { name: drafterName });

    await rows.first().click();
    await openTheDrafter(page);
    await field.fill('confirm the price');
    await rows.nth(1).click();

    await expect(page.getByRole('form', { name: drafterName })).toBeHidden();

    await openTheDrafter(page);

    await expect(field).toHaveValue('');
});

// The two choices are a layer of their own, so the back gesture folds them into the button before it reaches the
// message they stand under — and the keyboard goes back to the button that unfolded them.
test('folds the two choices on the back gesture before it leaves the message', async ({ page }) => {
    await openMailWritingDrafts(page);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
    await page.getByRole('button', { name: aiButton }).click();

    await expect(page.getByRole('button', { name: 'Draft a reply', exact: true })).toBeVisible();

    await page.goBack();

    await expect(page.getByRole('button', { name: 'Draft a reply', exact: true })).toBeHidden();
    await expect(page.getByRole('button', { name: 'Ask', exact: true })).toBeHidden();
    await expect(page.getByRole('button', { name: aiButton })).toBeFocused();
});

// The drafter stands over the thread like any surface over the screen, so the back gesture folds it into the AI button
// before it leaves the message.
test('folds the drafter on the back gesture before it leaves the message', async ({ page }) => {
    await openMailWritingDrafts(page);
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
    await openTheDrafter(page);

    await expect(page.getByRole('form', { name: drafterName })).toBeVisible();

    await page.goBack();

    await expect(page.getByRole('form', { name: drafterName })).toBeHidden();
    await expect(page.getByRole('button', { name: aiButton })).toBeVisible();
});
