// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect } from '@playwright/test';

import { folder, fromTheMenu, openTheInbox, row, test } from './client.harness';

// A change made while the deployment stopped answering, followed from the act to the moment it lands. Nothing answered
// it, so the client cannot know it did not land: what is read back is that it is drawn as made throughout, that it is
// held in the tab and sent again once the deployment answers — under the key it first went with, so it lands once —
// and that the row, the folder's count, and a reload then agree it happened.

/** How long a write nobody answered waits once the server answers: `unansweredWriteRetryDelay` in `pendingChanges/`. */
const unansweredWriteRetryDelay = 5_000;

/** The key each attempt at the flag route carried, in the order the page sent them. */
function keysSent(deployment: { requests: (method: string, route: string) => { body: unknown }[] }): unknown[] {
    return deployment
        .requests('POST', '/mutations/flags')
        .map(({ body }) => (typeof body === 'object' && body !== null && 'requestId' in body ? body.requestId : null));
}

test('sends a read mark nobody answered again under the key it first went with, and the row, the count, and a reload agree it landed once', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    deployment.stopAnswering();
    await fromTheMenu(page, 'Message 12', 'Mark as read');

    await expect(row(page, 'Message 12')).not.toHaveAccessibleName(/\bUnread\b/u);
    await expect(page.getByText('Not known whether this reached the server')).toBeVisible();
    await expect(page.getByRole('button', { name: 'Try again' })).toHaveCount(0);

    deployment.answerAgain();

    // The client learns the deployment answers again from its own reads, then waits before sending the write again.
    await expect
        .poll(async () => {
            await page.clock.runFor(unansweredWriteRetryDelay);

            return deployment.requests('POST', '/mutations/flags').length;
        })
        .toBeGreaterThan(1);

    const keys = keysSent(deployment);

    expect(new Set(keys).size).toBe(1);
    expect(keys[0]).toEqual(expect.any(String));
    await expect(row(page, 'Message 12')).not.toHaveAccessibleName(/\bUnread\b/u);
    await expect(folder(page, 'Inbox 11 unread')).toBeVisible();

    await page.reload();

    await expect(row(page, 'Message 12')).not.toHaveAccessibleName(/\bUnread\b/u);
    await expect(folder(page, 'Inbox 11 unread')).toBeVisible();
});
