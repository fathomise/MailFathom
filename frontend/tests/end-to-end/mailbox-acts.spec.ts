// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, test, type Page } from '@playwright/test';
import { deliver } from './mailbox';
import {
    arrived,
    folder,
    fromTheMenu,
    messageRow,
    none,
    notice,
    one,
    onTheMailServer,
    openMail,
    settles,
    unreadCountsAgree,
} from './mailScreen';

// Every act the client offers on mail that is already in the mailbox, performed through the screen against the real
// deployment and held to three witnesses: what the screen draws once the act is done, what it draws after a reload —
// the service's answer rather than the client's optimistic one — and what the mail server holds, read over IMAP. The
// browser suite proves each of these against the fake deployment; this file is what proves the fake and the service
// agree, because a client and a fake written by one author can agree with each other and both be wrong.
//
// They wait on the service's own pace — a synchronization run, then the converger carrying a change to the mail server —
// so a spec is given minutes rather than the default half a minute, and every wait inside one is on a condition.
test.describe.configure({ timeout: 4 * 60_000 });

// Each spec acts on mail it delivered itself, under a subject nobody else wrote, so it can name its message exactly on
// the screen and on the server and leaves the corpus as the next spec expects it. `mailbox.ts` says why.

/** Reloads the page, which signs nobody out, and waits for the tree of folders to be drawn again from the deployment. */
async function reloaded(page: Page): Promise<void> {
    await page.reload();

    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
}

test('destroys a message deleted from the trash, after the question saying so, on the screen and on the mail server', async ({
    page,
}) => {
    await openMail(page);

    const message = await deliver('Deleted for good');

    await arrived(page, message);

    await fromTheMenu(messageRow(page, message.subject), 'Delete');
    await page
        .getByRole('dialog', { name: 'Delete 1 message?' })
        .getByRole('button', { name: 'Move to the trash' })
        .click();
    await onTheMailServer('Trash', message.subject, one());

    await folder(page, 'Trash').click();
    await fromTheMenu(messageRow(page, message.subject), 'Delete');

    const question = page.getByRole('dialog', { name: 'Delete 1 message?' });

    await expect(question).toContainText(
        'Already in the trash - deleting now removes 1 message together with attachments.',
    );
    await question.getByRole('button', { name: 'Delete', exact: true }).click();

    await expect(notice(page, 'Deleting…')).toContainText('1 message');
    await expect(messageRow(page, message.subject)).toHaveCount(0);

    // The way back stays offered for a few seconds, and the message leaves the mail server only once it has passed.
    await onTheMailServer('Trash', message.subject, none);
    await onTheMailServer('INBOX', message.subject, none);

    await reloaded(page);
    await folder(page, 'Trash').click();
    await expect(messageRow(page, message.subject)).toHaveCount(0);
});

test('files a message in another folder, and the folder holds it on the screen, after a reload, and on the mail server', async ({
    page,
}) => {
    await openMail(page);

    const message = await deliver('Filed in the archive');

    await arrived(page, message);

    await fromTheMenu(messageRow(page, message.subject), 'Move…');
    await page.getByRole('dialog', { name: 'File in another folder' }).getByRole('button', { name: 'Archive' }).click();

    await expect(notice(page, 'Filed in “Archive”')).toContainText('1 message');
    await expect(messageRow(page, message.subject)).toHaveCount(0);
    await unreadCountsAgree(page);

    await onTheMailServer('INBOX', message.subject, none);
    await onTheMailServer('Archive', message.subject, one({ without: ['\\Seen'] }));

    await folder(page, 'Archive').click();
    await expect(messageRow(page, message.subject)).toBeVisible();

    // A reload opens the folder that was open, so the archive is read first and the inbox after it.
    await reloaded(page);
    await expect(messageRow(page, message.subject)).toBeVisible(settles);
    await folder(page, 'Inbox').click();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible(settles);
    await expect(messageRow(page, message.subject)).toHaveCount(0);
});

test('flags a message and takes the flag off again, and the row, a reload, and the mail server agree both ways', async ({
    page,
}) => {
    await openMail(page);

    const message = await deliver('A flag set and taken off');
    const row = messageRow(page, message.subject);

    await arrived(page, message);

    await fromTheMenu(row, 'Flag');
    await expect(row).toHaveAccessibleName(/\bFlagged\b/u);
    await onTheMailServer('INBOX', message.subject, one({ carrying: ['\\Flagged'] }));

    await reloaded(page);
    await expect(row).toHaveAccessibleName(/\bFlagged\b/u, settles);

    await fromTheMenu(row, 'Remove the flag');
    await expect(row).not.toHaveAccessibleName(/\bFlagged\b/u);
    await onTheMailServer('INBOX', message.subject, one({ without: ['\\Flagged'] }));

    await reloaded(page);
    await expect(row).toBeVisible(settles);
    await expect(row).not.toHaveAccessibleName(/\bFlagged\b/u);
});

test('marks a message read and unread again, and the row, the inbox count, a reload, and the mail server agree both ways', async ({
    page,
}) => {
    await openMail(page);

    const message = await deliver('Read and unread');
    const row = messageRow(page, message.subject);

    await arrived(page, message);
    await expect(row).toHaveAccessibleName(/\bUnread\b/u);

    await fromTheMenu(row, 'Mark as read');
    await expect(row).not.toHaveAccessibleName(/\bUnread\b/u);
    await unreadCountsAgree(page);
    await onTheMailServer('INBOX', message.subject, one({ carrying: ['\\Seen'] }));

    await reloaded(page);
    await expect(row).toBeVisible(settles);
    await expect(row).not.toHaveAccessibleName(/\bUnread\b/u);
    await unreadCountsAgree(page);

    await fromTheMenu(row, 'Mark as unread');
    await expect(row).toHaveAccessibleName(/\bUnread\b/u);
    await unreadCountsAgree(page);
    await onTheMailServer('INBOX', message.subject, one({ without: ['\\Seen'] }));

    await reloaded(page);
    await expect(row).toHaveAccessibleName(/\bUnread\b/u, settles);
    await unreadCountsAgree(page);
});

test('marks a message spam with the kind picked and takes it back, and the row, a reload, and the mail server agree both ways', async ({
    page,
}) => {
    await openMail(page);

    const message = await deliver('Spam and not spam');
    const row = messageRow(page, message.subject);

    await arrived(page, message);

    await fromTheMenu(row, 'Spam…');

    const chooser = page.getByRole('dialog', { name: 'Mark 1 message as spam' });

    await expect(chooser).toContainText('It stays in the folder it is in.');
    await chooser.getByRole('radio', { name: /^Marketing/u }).check();
    await chooser.getByRole('button', { name: 'Mark as spam' }).click();

    // Marked spam, the message stays in the folder it is in and is folded out of the way there, which the mail server
    // records as the keyword other mail clients read as junk.
    await expect(row).toHaveAccessibleName(/^Spam: Marketing, marked by you\./u);
    await onTheMailServer('INBOX', message.subject, one({ carrying: ['$Junk'], without: ['$NotJunk'] }));
    await onTheMailServer('Junk', message.subject, none);

    await reloaded(page);
    await expect(row).toHaveAccessibleName(/^Spam: Marketing, marked by you\./u, settles);

    await fromTheMenu(row, 'Not spam…');

    // Withdrawing the mark is asked first, though this message moves nowhere.
    const question = page.getByRole('dialog', { name: 'Mark this message not spam?' });

    await expect(question).toContainText('It is marked not spam and stays where it is.');
    await question.getByRole('button', { name: 'Not spam' }).click();

    await expect(row).not.toHaveAccessibleName(/^Spam:/u);
    await onTheMailServer('INBOX', message.subject, one({ carrying: ['$NotJunk'], without: ['$Junk'] }));

    await reloaded(page);
    await expect(row).toBeVisible(settles);
    await expect(row).not.toHaveAccessibleName(/^Spam:/u);
});

test('empties the trash of everything in it, and the trash is empty on the screen, after a reload, and on the mail server', async ({
    page,
}) => {
    await openMail(page);

    const first = await deliver('Trash emptied, first');
    const second = await deliver('Trash emptied, second');

    await arrived(page, first, second);

    await messageRow(page, first.subject).click({ modifiers: ['ControlOrMeta'] });
    await messageRow(page, second.subject).click({ modifiers: ['ControlOrMeta'] });

    const selection = page.getByRole('toolbar', { name: 'Actions on the messages selected' });

    await expect(selection.getByRole('status')).toHaveText('2 selected');
    await selection.getByRole('button', { name: 'Delete' }).click();
    await page
        .getByRole('dialog', { name: 'Delete 2 messages?' })
        .getByRole('button', { name: 'Move to the trash' })
        .click();
    await onTheMailServer('Trash', first.subject, one());
    await onTheMailServer('Trash', second.subject, one());

    // The client has no single control that empties the trash: a person selects everything in it and deletes that, which
    // is the act this spec performs.
    await folder(page, 'Trash').click();
    await messageRow(page, first.subject).click({ modifiers: ['ControlOrMeta'] });
    await selection.getByRole('button', { name: 'Select all' }).click();
    await selection.getByRole('button', { name: 'Delete' }).click();

    const question = page.getByRole('dialog', { name: /^Delete \d+ messages?\?$/u });

    await expect(question).toContainText(
        /^.*Already in the trash - deleting now removes \d+ messages? together with attachments\./u,
    );
    await question.getByRole('button', { name: 'Delete', exact: true }).click();

    await expect(page.getByText('There is no mail in this folder.')).toBeVisible(settles);
    await onTheMailServer('Trash', first.subject, none);
    await onTheMailServer('Trash', second.subject, none);

    await reloaded(page);
    await folder(page, 'Trash').click();
    await expect(page.getByText('There is no mail in this folder.')).toBeVisible(settles);
});

test('marks, flags, and archives every message of a selection, and the rows, the count, a reload, and the mail server agree', async ({
    page,
}) => {
    await openMail(page);

    const delivered = [await deliver('Selected, first'), await deliver('Selected, second')];

    await arrived(page, ...delivered);

    const selection = page.getByRole('toolbar', { name: 'Actions on the messages selected' });

    // Every act ends the selection it was performed over, which is the design's own, so each one picks the two again.
    async function selectBoth(): Promise<void> {
        for (const message of delivered) {
            await messageRow(page, message.subject).click({ modifiers: ['ControlOrMeta'] });
        }

        await expect(selection.getByRole('status')).toHaveText('2 selected');
    }

    await selectBoth();
    await selection.getByRole('button', { name: 'Mark read' }).click();
    await expect(selection).toHaveCount(0);
    await unreadCountsAgree(page);

    await selectBoth();
    await selection.getByRole('button', { name: 'Flag' }).click();

    for (const message of delivered) {
        await expect(messageRow(page, message.subject)).not.toHaveAccessibleName(/\bUnread\b/u);
        await expect(messageRow(page, message.subject)).toHaveAccessibleName(/\bFlagged\b/u);
        await onTheMailServer('INBOX', message.subject, one({ carrying: ['\\Seen', '\\Flagged'] }));
    }

    await selectBoth();
    await selection.getByRole('button', { name: 'Archive' }).click();

    await expect(notice(page, 'Archived')).toContainText('2 messages');

    for (const message of delivered) {
        await expect(messageRow(page, message.subject)).toHaveCount(0);
        await onTheMailServer('INBOX', message.subject, none);
        await onTheMailServer('Archive', message.subject, one({ carrying: ['\\Seen', '\\Flagged'] }));
    }

    await reloaded(page);
    await folder(page, 'Archive').click();

    for (const message of delivered) {
        await expect(messageRow(page, message.subject)).toBeVisible(settles);
        await expect(messageRow(page, message.subject)).toHaveAccessibleName(/\bFlagged\b/u);
    }

    await unreadCountsAgree(page);
});

test('marks a whole conversation read, deletes it and takes that back, and files it, and every message of it agrees', async ({
    page,
}) => {
    await openMail(page);

    const opening = await deliver('Conversation acted on');
    const answer = await deliver('Conversation acted on', { answering: opening });
    const conversation = [opening, answer];

    await arrived(page, opening, answer);

    // Reading by conversation is a preference the deployment keeps for the person, so it is put back whatever the spec
    // did in between: every other spec reads the list by message.
    await page.getByRole('button', { name: 'Messages, switch to threads', exact: true }).click();

    try {
        const thread = page
            .getByRole('list', { name: 'Threads' })
            .getByRole('listitem')
            .filter({ hasText: opening.subject });

        await expect(thread).toHaveCount(1, settles);
        await expect(thread).toContainText('2 messages in this conversation');

        await fromTheMenu(thread, 'Mark as read');

        // The opening subject is a part of the answer's, so one reading of it is the whole conversation.
        await onTheMailServer(
            'INBOX',
            opening.subject,
            (held) => held.length === 2 && held.every(({ flags }) => flags.includes('\\Seen')),
        );

        await fromTheMenu(thread, 'Delete');
        await page
            .getByRole('dialog', { name: 'Delete 2 messages?' })
            .getByRole('button', { name: 'Move to the trash' })
            .click();

        await expect(notice(page, 'Moved to the trash')).toContainText('2 messages');
        await expect(thread).toHaveCount(0);

        await notice(page, 'Moved to the trash').getByRole('button', { name: 'Undo' }).click();

        await expect(notice(page, 'Put back where it was')).toContainText('2 messages');
        await expect(thread).toHaveCount(1, settles);
        await onTheMailServer('Trash', opening.subject, none);
        await onTheMailServer('INBOX', opening.subject, (held) => held.length === 2);

        await fromTheMenu(thread, 'Move…');
        await page
            .getByRole('dialog', { name: 'File in another folder' })
            .getByRole('button', { name: 'Archive' })
            .click();

        // Read by conversation the list is every conversation outside Trash and Spam, and Archive is an ordinary folder,
        // so the conversation stays listed; where its messages are kept is read back by message below.
        await expect(notice(page, 'Filed in “Archive”')).toContainText('2 messages');
        await expect(thread).toHaveCount(1);
        await onTheMailServer('INBOX', opening.subject, none);
        await onTheMailServer('Archive', opening.subject, (held) => held.length === 2);

        // Read by conversation there is no tree of folders to wait for, so the conversation drawn again is the wait.
        await page.reload();
        await expect(thread).toHaveCount(1, settles);
    } finally {
        await page.getByRole('button', { name: 'Threads, switch to messages', exact: true }).click();
        await expect(page.getByRole('listbox', { name: 'Messages' })).toBeVisible();
    }

    // Read by conversation the side list draws accounts rather than folders, so the inbox's count is read back here.
    await unreadCountsAgree(page);
    await folder(page, 'Inbox').click();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first()).toBeVisible(settles);

    for (const message of conversation) {
        await expect(messageRow(page, message.subject)).toHaveCount(0);
    }

    await folder(page, 'Archive').click();

    for (const message of conversation) {
        await expect(messageRow(page, message.subject)).toBeVisible(settles);
    }
});
