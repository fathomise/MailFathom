// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { mailboxAddress } from './mailbox';
import { folder, messageRow, none, notice, one, onTheMailServer, openMail, settles } from './mailScreen';

// What a person writes, against the real deployment: a draft kept and opened again, a message sent and found both in
// Sent and — delivered, since it is addressed to this mailbox — in the inbox, and a send taken back while the Outbox
// still holds it. Each is held to the screen, the screen after a reload, and the mail server read over IMAP, on the rule
// `mailbox-acts.spec.ts` states for the acts on mail already there.
//
// Every message here is addressed to the mailbox the deployment reads, which is the one address this run's mail server
// delivers to: sending anywhere else would be mail the run cannot read back. The subject carries a mark drawn for the
// spec, so it names one message on both sides.

// Paced by the service rather than by the page, as `mailbox-acts.spec.ts` says.
test.describe.configure({ timeout: 4 * 60_000 });

/** A subject nobody else wrote. */
function subjectFor(words: string): string {
    return `${words} ${randomUUID().slice(0, 8)}`;
}

/** Opens a new message addressed to this mailbox, under the subject and with the words given. */
async function writeToThisMailbox(page: Page, subject: string, words: string): Promise<void> {
    await page.getByRole('button', { name: 'New message', exact: true }).first().click();

    const recipient = page.getByRole('combobox', { name: 'To', exact: true });

    await recipient.fill(mailboxAddress);
    await recipient.press('Enter');
    await expect(page.getByRole('button', { name: `Remove ${mailboxAddress} from To` })).toBeVisible();

    await page.getByLabel('Subject').fill(subject);
    await page.getByRole('textbox', { name: 'Message' }).click();
    await page.keyboard.type(words);
}

/** Reloads the page and waits for the tree of folders to be drawn again from the deployment. */
async function reloaded(page: Page): Promise<void> {
    await page.reload();

    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
}

test('keeps a draft in Drafts, on the mail server, and across a reload, and opens it again with what was written', async ({
    page,
}) => {
    const subject = subjectFor('A draft kept');
    const words = 'Words kept in a draft for later.';

    await openMail(page);
    await writeToThisMailbox(page, subject, words);

    const composer = page.getByRole('region', { name: 'New message' });

    await composer.getByRole('button', { name: 'Save draft' }).click();
    await expect(composer.getByText('Draft filed in your own drafts.')).toBeVisible(settles);
    await onTheMailServer('Drafts', subject, one({ carrying: ['\\Draft'] }));

    await composer.getByRole('button', { name: 'Close the message' }).click();
    await page.getByRole('dialog', { name: 'Discard this message?' }).getByRole('button', { name: 'Save draft' }).click();
    await expect(composer).toHaveCount(0);

    await reloaded(page);
    await folder(page, 'Drafts').click();
    await messageRow(page, subject).click();

    const draft = page.getByRole('region', { name: 'Draft' });

    await expect(draft.getByRole('button', { name: `Remove ${mailboxAddress} from To` })).toBeVisible(settles);
    await expect(draft.getByLabel('Subject')).toHaveValue(subject);
    await expect(draft.getByRole('textbox', { name: 'Message' })).toHaveText(words);

    // Opened again and left, the draft is still the one draft the mail server holds rather than a second copy of it.
    await onTheMailServer('Drafts', subject, one({ carrying: ['\\Draft'] }));
});

test('sends a message that reaches Sent and, delivered, the inbox, on the screen, after a reload, and on the mail server', async ({
    page,
}) => {
    const subject = subjectFor('A message sent');

    await openMail(page);
    await writeToThisMailbox(page, subject, 'Words that went out and came back.');

    await page.getByRole('button', { name: 'Send', exact: true }).click();
    await page.getByRole('dialog', { name: 'Send this message?' }).getByRole('button', { name: /^Send/u }).click();

    await expect(notice(page, 'Queued to go out.')).toBeVisible();
    await expect(page.getByRole('region', { name: 'New message' })).toHaveCount(0);

    await onTheMailServer('Sent', subject, one({ carrying: ['\\Seen'] }));
    await onTheMailServer('INBOX', subject, one({ without: ['\\Seen'] }));

    await expect(messageRow(page, subject)).toBeVisible(settles);
    await expect(messageRow(page, subject)).toHaveAccessibleName(/\bUnread\b/u);
    await folder(page, 'Sent').click();
    await expect(messageRow(page, subject)).toBeVisible(settles);

    await reloaded(page);
    await expect(messageRow(page, subject)).toBeVisible(settles);
    await folder(page, 'Sent').click();
    await expect(messageRow(page, subject)).toBeVisible(settles);
});

test('takes back a send the Outbox still holds, so nothing leaves and nothing reaches the mail server', async ({ page }) => {
    const subject = subjectFor('A send taken back');

    await openMail(page);
    await writeToThisMailbox(page, subject, 'Words that never left.');

    // Written to leave tomorrow, so it is still in the Outbox however long the spec takes to reach it there.
    await page.getByRole('button', { name: 'Sending options' }).click();
    await page.getByRole('menuitemradio', { name: /^Tomorrow morning\b/u }).click();
    await page.getByRole('region', { name: 'New message' }).getByRole('button', { name: /^Send \S/u }).click();
    await page
        .getByRole('dialog', { name: 'Schedule this message?' })
        .getByRole('button', { name: /^Schedule/u })
        .click();

    await expect(notice(page, 'Message scheduled')).toBeVisible();

    const outbox = page.getByRole('tree', { name: 'Mailboxes and folders' }).getByRole('button', { name: /^Outbox - /u });
    const sends = page.getByRole('list', { name: 'Outbox' });
    const waiting = sends.getByRole('button', { name: new RegExp(subject, 'u') });

    await outbox.click();
    await expect(waiting).toBeVisible(settles);

    await reloaded(page);
    await outbox.click();
    await waiting.click();
    await page.getByRole('button', { name: 'Cancel sending' }).click();

    await expect(notice(page, 'Sending cancelled')).toBeVisible();
    await expect(waiting).toHaveCount(0);

    await reloaded(page);
    await expect(waiting).toHaveCount(0);

    await onTheMailServer('Sent', subject, none);
    await onTheMailServer('INBOX', subject, none);
});
