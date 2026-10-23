// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect } from '@playwright/test';

import { openSignedIn, readOnward, restingFirstRow, test } from './client.harness';

// The message list as a window over a folder of two hundred and fourteen thousand rows: one row height, a bounded
// document however far a reader scrolls, and the reader put back where they were.

test('reads its mail exactly as it always did against a deployment serving no signal channel', async ({ page }) => {
    const asked: string[] = [];
    const logged: string[] = [];

    page.on('request', (request) => {
        asked.push(new URL(request.url()).pathname);
    });
    page.on('console', (message) => {
        logged.push(message.text());
    });

    // The fake deployment serves no signal channel, so it answers the ticket the way one built before the channel
    // existed does: it refuses it. That is the whole arrangement — what follows is every screen behaving as it does in
    // every other check of this suite.
    await openSignedIn(page);
    await page.getByRole('link', { name: 'Mail' }).click();

    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
    await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();
    await expect(page.getByRole('option', { name: /Message 0$/ })).toBeVisible();

    // The client did try, which is what makes the assertion above about a channel rather than about a client that
    // never reached for one — and it said nothing anywhere about having failed, because a channel that is down is not
    // a sentence a person reading their mail is owed.
    expect(asked).toContain('/api/client/signals/ticket');
    expect(logged.filter((line) => line.toLowerCase().includes('signal'))).toStrictEqual([]);
});

// What only a browser can say about the message list: every row is one height, the document holds a window of rows
// rather than the folder, and a reader who leaves and comes back is put back where they were. Everything else about
// it — the paging arithmetic, the states, the selection, and every sentence — is jsdom's and lives in the unit suite
// beside the source.

test('draws every row of the list at one height, which is what the window is arithmetic over', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    const heights = await Promise.all(
        (await list.getByRole('option').all()).map(async (row) => (await row.boundingBox())?.height),
    );

    // The measurement the choice of windowing was made against, kept as an assertion rather than as a number in a
    // pull request: a row whose height varied with its subject, its preview, or its marks would put every row below it
    // somewhere other than where the list drew the space for it — and would be the argument for a virtualizer that
    // measures rows, which this list deliberately does not carry.
    expect(new Set(heights).size).toBe(1);
    expect(heights[0]).toBeGreaterThan(0);
});

test('opens what MailFathom made of a message from its own row, with the passages it rests on', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    const enriched = list.getByRole('option').nth(1);

    await expect(enriched).toContainText('An answer is owed before the end of the week.');

    // The row's own menu, which is where checking a reading is reached from: a row is an `option` of a listbox and
    // holds no focusable descendant, so the sentence on it cannot be a control of its own.
    await enriched.click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'AI summary' }).click();

    const checking = page.getByRole('dialog', { name: 'What MailFathom read' });

    await expect(checking.getByText('Written by AI')).toBeVisible();
    await expect(checking.getByText('Please confirm the bays you want before the end of the week.')).toBeVisible();
});

test('holds a window of rows in the document however far down the folder it is scrolled', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    const drawnAtTheTop = await list.getByRole('option').count();

    for (let read = 1; read <= 6; read += 1) {
        await readOnward(page, list);
    }

    // Hundreds of rows further down a folder of two hundred and fourteen thousand, reached a screenful at a time the
    // way somebody scrolls — and the document holds no more rows than it did on the first screen. That is the whole
    // claim windowing makes, and only a browser laying the list out can answer it.
    await expect(list.getByRole('option').first()).toContainText(/Message [1-9]\d\d/);
    expect(await list.getByRole('option').count()).toBeLessThanOrEqual(drawnAtTheTop);
    await expect(list.getByRole('option', { name: /^Writer 0\D/ })).toHaveCount(0);
});

test('starts the list at its leading end when the order changes under a reader who had scrolled', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    const drawnAtTheTop = await list.getByRole('option').count();

    for (let read = 1; read <= 3; read += 1) {
        await readOnward(page, list);
    }

    // The order is behind the list's filters, opened from the control at the end of the column's head row.
    await page.getByRole('button', { name: 'Filters', exact: true }).click();
    await page.getByRole('group', { name: 'Order' }).getByText('Oldest first', { exact: true }).click();

    // Changing the order empties the list, which takes the scroller out of the document, so the one that comes back is
    // at the top however far down the reader had been. A window still computed from where they were draws the far end
    // of the first page under a screen of blank space, and no scroll is left to fire the event that would correct it —
    // which only a browser laying the list out can answer.
    await expect(list.getByRole('option').first()).toContainText('Message 0');
    expect(await list.getByRole('option').count()).toBeGreaterThanOrEqual(drawnAtTheTop);
});

test('puts a reader back where they were reading, across a reload', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const list = page.getByRole('listbox', { name: 'Messages' });
    await expect(list.getByRole('option').first()).toBeVisible();

    // Far enough that the row the reader is on is in a page the client had to ask for with a cursor, which is the
    // whole of what returning to a position means: a client that read the folder again from its leading end would land
    // on message zero and look identical to one that had never scrolled.
    for (let read = 1; read <= 3; read += 1) {
        await readOnward(page, list);
    }

    const before = await restingFirstRow(list);

    // A reload is a cold start, so where somebody was reading is kept where the credential is kept and read back the
    // same way. Only a real document reloaded proves it was written rather than held.
    await page.reload();

    const after = page.getByRole('listbox', { name: 'Messages' });
    await expect(after.getByRole('option').first()).toBeVisible();

    expect(await after.getByRole('option').first().textContent()).toBe(before);
});
