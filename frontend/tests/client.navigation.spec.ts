// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect } from '@playwright/test';

import { declaredVersion, messageRegion, openSettings, openSignedIn, phoneWindow, test } from './client.harness';

// Moving between spaces the way a document with a history does, and finding each one — and the folder tree — where it
// was left after a reload.

test('opens in Discover, under the version it was built from and the one the deployment answered', async ({ page }) => {
    await openSignedIn(page);

    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    // The client's own is substituted into the bundle at build time, which is the half only a built bundle proves; the
    // deployment's arrives over the wire beside it. Both are read at the foot of the settings screen, which is where
    // the design draws them.
    await openSettings(page);
    await expect(page.getByText(`MailFathom v${declaredVersion} · deployment ${declaredVersion}`)).toBeVisible();
    await page.getByRole('button', { name: 'Close settings' }).click();

    // A first load at the root is written back to the address the space is actually reached at, which is what makes
    // the next assertion — reloading it — mean anything.
    await expect(page).toHaveURL(/#\/discover$/);
});

test('reaches each space by its own address, and reloads there', async ({ page }) => {
    await openSignedIn(page, '/#/agent');

    await expect(page.getByRole('textbox', { name: 'Tell the agent what to do' })).toBeVisible();

    // The whole reason the address is a fragment: nothing is asked of a server, so the bundle that answers `/` answers
    // this too and the client reads the space back out of the address it was reloaded at. A path would need a fallback
    // mapping every unmatched address onto the entry document, which the service deliberately does not serve.
    await page.reload();

    await expect(page.getByRole('textbox', { name: 'Tell the agent what to do' })).toBeVisible();
});

test('moves back and forward through its own spaces without leaving the application', async ({ page }) => {
    await openSignedIn(page);
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    await page.getByRole('link', { name: 'Mail' }).click();
    await expect(page.getByRole('main', { name: 'Mail' })).toBeVisible();

    await page.goBack();
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    await page.goForward();
    await expect(page.getByRole('main', { name: 'Mail' })).toBeVisible();
});

// What the back gesture of a phone does to a screen that replaced another: it goes back one screen, and only past the
// last of them does it leave the space. Android's own back is the browser's history here, so `goBack` is the gesture.
test('goes back one screen at a time on a phone, from the message to the list and only then to the space before', async ({
    page,
}) => {
    await page.setViewportSize(phoneWindow);
    await openSignedIn(page);
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();

    await page.getByRole('link', { name: 'Mail' }).click();
    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click();
    await expect(page.getByRole('article', messageRegion)).toBeVisible();

    await page.goBack();
    await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();
    await expect(page.getByRole('article', messageRegion)).toHaveCount(0);
    await expect(page).toHaveURL(/#\/mail$/u);

    await page.goBack();
    await expect(page.getByRole('heading', { name: 'Discover', level: 1 })).toBeVisible();
});

test('goes back from a person to the address book on a phone, rather than out of People', async ({
    page,
    deployment,
}) => {
    deployment.grant('mailfathom.contacts.read');
    await page.setViewportSize(phoneWindow);
    await openSignedIn(page, '/#/people');

    const book = page.getByRole('listbox', { name: 'Contacts' });

    await book.getByRole('option', { name: /Anna Marlow/u }).click();
    await expect(page.getByRole('heading', { name: 'Anna Marlow' })).toBeVisible();
    await expect(book).toBeHidden();

    await page.goBack();
    await expect(book).toBeVisible();
    await expect(page).toHaveURL(/#\/people$/u);
});

test('closes a dialog with the back gesture and leaves the screen it stood over where it was', async ({
    page,
    deployment,
}) => {
    deployment.grant('mailfathom.contacts.read', 'mailfathom.contacts.write');
    await openSignedIn(page, '/#/people');
    await expect(page.getByRole('listbox', { name: 'Contacts' })).toBeVisible();

    await page.getByRole('button', { name: 'New contact' }).click();
    await expect(page.getByRole('dialog', { name: 'New contact' })).toBeVisible();

    await page.goBack();
    await expect(page.getByRole('dialog', { name: 'New contact' })).toBeHidden();
    await expect(page.getByRole('listbox', { name: 'Contacts' })).toBeVisible();
    await expect(page).toHaveURL(/#\/people$/u);
});

// Discover's field is Discover's alone, so what is carried is the sentence surviving a visit elsewhere — and what is
// proven beside it is that no space visited shows it in a field of its own, nor puts its own text into Discover's.
test('keeps the question in Discover alone across a visit to other spaces', async ({ page }) => {
    await openSignedIn(page);

    const question = page.getByRole('searchbox', { name: 'Ask your mail' });
    await question.fill('the renewal Nordwind sent');

    await page.getByRole('link', { name: 'Mail' }).click();
    await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();
    await expect(question).toBeHidden();

    await page.getByRole('link', { name: 'Agent' }).click();

    const instruction = page.getByRole('textbox', { name: 'Tell the agent what to do' });
    await expect(instruction).toHaveValue('');
    await instruction.fill('Plan my day');

    await page.getByRole('link', { name: 'Discover' }).click();

    await expect(question).toHaveValue('the renewal Nordwind sent');
});

test('keeps the folder tree as it was left, across a reload', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const tree = page.getByRole('tree', { name: 'Mailboxes and folders' });

    // The level the corpus nests a folder under, which arrives shut: a mailbox filed three levels deep would
    // otherwise open as everything it has ever held. The corpus holds one mailbox, which is a user the tree offers
    // no row spanning every mailbox to, so what unfolds here is a level of a folder's own path. It is the one of the two
    // rows named *Archive* that unfolds at all: the other is the folder the account files by that role, which is drawn
    // under the role's name and holds nothing beneath it.
    await expect(tree.getByRole('treeitem', { name: 'Archive', expanded: false })).toBeVisible();

    await tree.getByRole('treeitem', { name: 'Archive', expanded: false }).click();
    await page.keyboard.press('ArrowRight');
    await expect(tree.getByRole('treeitem', { name: 'Archive', expanded: true })).toBeVisible();

    const nested = tree.getByRole('treeitem', { name: /^2024/ });

    await nested.click();
    await expect(nested).toHaveAttribute('aria-selected', 'true');

    await page.reload();

    // A reload is a cold start, so what somebody was looking at is kept where the credential is kept and read back the
    // same way. Only a real document reloaded proves it was written rather than held: a remount in jsdom re-reads the
    // same process's storage, and what this asks is that a browser wrote it. What was written is the move away from
    // the fold the row opens at rather than the fold itself, so a level opened by hand comes back open.
    await expect(tree.getByRole('treeitem', { name: 'Archive', expanded: true })).toBeVisible();

    // The scope outlives the reload beside the fold, which is the other half of what was left here.
    await expect(tree.getByRole('treeitem', { name: /^2024/ })).toHaveAttribute('aria-selected', 'true');
});
