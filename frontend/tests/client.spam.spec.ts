// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import { bodiesOf, folder, notice, openTheInbox, test } from './client.harness';
import { phishingSubject } from './fixtures/mail';
import { spamLinkHost } from './fixtures/messages';

// What a spam verdict does across the client: the Spam folder that lists it, the opened message it warns on, and the
// mark a selection takes — whose answer every later read of the list, and a reload, has to agree with.

function listed(page: Page, subject: string): Locator {
    return page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: new RegExp(`\\b${subject}\\b`, 'u') });
}

function selectionBar(page: Page): Locator {
    return page.getByRole('toolbar', { name: 'Actions on the messages selected' });
}

async function pickOut(page: Page, ...subjects: readonly string[]): Promise<void> {
    for (const subject of subjects) {
        await listed(page, subject).click({ modifiers: ['ControlOrMeta'] });
    }
}

test('warns on a phishing message opened from the Spam folder and draws its link as words that open nothing', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);
    await folder(page, 'Spam').click();

    await expect(listed(page, phishingSubject)).toHaveAccessibleName(/\bPhishing$/u);
    await listed(page, phishingSubject).click();

    const warning = page.getByRole('status').filter({ hasText: 'This message is spam and a security risk.' });

    await expect(warning).toContainText('Phishing. This message is spam and a security risk.');
    await expect(warning).toContainText(
        'It asks for a password, and it is written to look as if it came from an online service.',
    );
    await expect(page.getByText(`(a link to ${spamLinkHost}, not opened)`)).toBeVisible();
    await expect(page.getByText('Keep my current password', { exact: true })).toBeVisible();
    await expect(page.getByRole('link', { name: 'Keep my current password' })).toHaveCount(0);
    await expect(page.getByRole('button', { name: 'Reply', exact: true })).toHaveCount(0);
});

test('marks a selection spam with the kind picked, folds it where it stands, and a reload agrees', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    await pickOut(page, 'Message 1', 'Message 2');
    await selectionBar(page).getByRole('button', { name: 'Spam', exact: true }).click();

    const chooser = page.getByRole('dialog', { name: 'Mark 2 messages as spam' });

    await chooser.getByRole('radio', { name: /^Scam/u }).check();
    await chooser.getByRole('button', { name: 'Mark as spam' }).click();

    await expect(notice(page, 'Marked as spam - 2')).toContainText('They stay where they are, folded out of the way.');
    await expect(listed(page, 'Message 1')).toHaveAccessibleName(/Spam: Scam, marked by you\./u);
    await expect(listed(page, 'Message 2')).toHaveAccessibleName(/Spam: Scam, marked by you\./u);
    await expect(listed(page, 'Message 4')).not.toHaveAccessibleName(/Spam/u);

    expect(bodiesOf(deployment, '/mutations/spam')).toStrictEqual([
        { verdict: 'Spam', spamSubcategory: 'Scam', storedEmailIds: ['message-1', 'message-2'] },
    ]);

    await page.reload();

    await expect(listed(page, 'Message 1')).toHaveAccessibleName(/Spam: Scam, marked by you\./u);
    await expect(listed(page, 'Message 2')).toHaveAccessibleName(/Spam: Scam, marked by you\./u);
});

test('refuses a selection that mixes spam with other mail, and sends nothing for it', async ({ page, deployment }) => {
    await openTheInbox(page, deployment);

    await listed(page, 'Message 1').click({ button: 'right' });
    await page.getByRole('menuitem', { name: 'Spam…', exact: true }).click();

    const chooser = page.getByRole('dialog', { name: 'Mark 1 message as spam' });

    await chooser.getByRole('radio', { name: /^Marketing/u }).check();
    await chooser.getByRole('button', { name: 'Mark as spam' }).click();
    await expect(listed(page, 'Message 1')).toHaveAccessibleName(/Spam: Marketing, marked by you\./u);

    await pickOut(page, 'Message 1', 'Message 2');
    await selectionBar(page).getByRole('button', { name: 'Spam', exact: true }).click();

    await expect(notice(page, 'Nothing was marked')).toContainText(
        'The selection mixes spam with other mail. Select only one of the two.',
    );
    await expect(page.getByRole('dialog', { name: /as spam$/u })).toHaveCount(0);
    expect(bodiesOf(deployment, '/mutations/spam')).toHaveLength(1);
});
