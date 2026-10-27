// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect } from '@playwright/test';

import { workAccount } from './fixtures/deployment';
import { markupOnlySubject } from './fixtures/messages';
import { foldWindow, openSignedIn, phoneWindow, restingFirstRow, subjectOf, test } from './client.harness';

// A search run from a folder at the width of a phone, where the list and the pane take turns: the results replace the
// folder's list, a result replaces the results, and each way back returns to the screen it left rather than to the
// start — the results from the message, and the folder the search began in from the results.

test('searches from the inbox, opens a result, goes back to the results, and then back to the inbox', async ({
    page,
    deployment,
}) => {
    await page.setViewportSize(phoneWindow);
    await openSignedIn(page, '/#/mail');

    const folder = page.getByRole('listbox', { name: 'Messages' });
    const found = page.getByRole('listbox', { name: 'What this search found' });

    await expect(folder.getByRole('option', { name: /The racking quote/u })).toBeVisible();

    const search = page.getByRole('searchbox', { name: 'Find a message' });

    await search.fill('renewal');
    await search.press('Enter');

    await expect(found.getByRole('option')).toHaveCount(3);
    await expect(folder).toHaveCount(0);

    const [asked] = deployment.requests('GET', '/emails/search');

    expect(asked?.query.get('account')).toBe(workAccount.id);
    expect(asked?.query.get('folder')).toBe('INBOX');

    await found.getByRole('option', { name: new RegExp(markupOnlySubject, 'u') }).click();

    await expect(page.getByRole('heading', { name: markupOnlySubject, level: 2 })).toBeVisible();
    await expect(found).toHaveCount(0);

    await page.getByRole('button', { name: 'Back to the list' }).click();

    await expect(found.getByRole('option')).toHaveCount(3);
    await expect(search).toHaveValue('renewal');

    await page.getByRole('button', { name: 'Stop searching' }).click();

    await expect(folder.getByRole('option', { name: /The racking quote/u })).toBeVisible();
    await expect(found).toHaveCount(0);
    await expect(search).toHaveValue('');
});

// Unfolding a phone into a foldable mid-search is the window growing under the results, and the rows growing shorter
// with it — the phone draws a taller row than the wider composition does. The reader stays on the result they were
// reading rather than at the same offset, which over shorter rows would be further down the ranked list.
test.describe('driven by a finger', () => {
    test.use({ hasTouch: true });

    test('keeps the place in the results as a foldable unfolds', async ({ page, deployment }) => {
        deployment.findAcrossTheFolder();

        await page.setViewportSize(phoneWindow);
        await openSignedIn(page, '/#/mail');
        await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible();

        const search = page.getByRole('searchbox', { name: 'Find a message' });
        const found = page.getByRole('listbox', { name: 'What this search found' });

        await search.fill('message');
        await search.press('Enter');
        await expect(found.getByRole('option').first()).toBeVisible();

        // A third of the way down rather than a whole wheel's worth, which would reach the end of forty results — and
        // at the end of a list the place is wherever the last row leaves it, at any row height.
        await found.getByRole('option').first().hover();
        await page.mouse.wheel(0, 2_000);
        await expect(found.getByRole('option').first()).not.toContainText('Message 0');

        const place = subjectOf(await restingFirstRow(found));

        // Unfolded, which is known by the toolbar only the two-pane compositions draw.
        await page.setViewportSize(foldWindow);
        await expect(page.getByRole('toolbar', { name: 'Mail actions' })).toBeVisible();

        expect(subjectOf(await restingFirstRow(found))).toBe(place);
    });
});
