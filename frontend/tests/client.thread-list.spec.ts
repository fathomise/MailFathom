// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect } from '@playwright/test';

import { openSignedIn, test } from './client.harness';

// The list read by conversation, as a journey across the switch, the mailbox column, the list, and the preference that
// remembers the choice: a unit test of each proves what it draws for what it was handed, and whether pressing one moves
// the other two — and whether the deployment keeps the choice for the next start — is the seam these stand on.

test('reads the list by conversation once the switch is pressed, and opens in it again after a reload', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page, '/#/mail');
    await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();

    await page.getByRole('button', { name: 'Messages, switch to threads', exact: true }).click();

    const threads = page.getByRole('list', { name: 'Threads' });

    await expect(threads.getByRole('listitem')).toHaveCount(4);
    await expect(threads.getByText('Contoso SLA addendum')).toBeVisible();
    await expect(
        page.getByRole('navigation', { name: 'Accounts' }).getByRole('button', { name: 'Work' }),
    ).toBeVisible();
    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toHaveCount(0);

    await expect
        .poll(() => deployment.requests('POST', '/preferences').at(-1)?.body)
        .toMatchObject({
            listMode: 'threads',
        });

    await page.reload();

    await expect(page.getByRole('list', { name: 'Threads' }).getByRole('listitem')).toHaveCount(4);
    await expect(page.getByRole('button', { name: 'Threads, switch to messages', exact: true })).toBeVisible();
});
