// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import {
    actingGrants,
    bodiesOf,
    folder,
    fromTheMenu,
    messageRegion,
    notice,
    openSignedIn,
    openTheArchive,
    openTheInbox,
    phoneWindow,
    refreshInterval,
    row,
    subjectOf,
    test,
} from './client.harness';
import type { FakeDeployment } from './fakeDeployment';

// The acts that change a mailbox from the Mail space — delete, purge, move, drag, flag, read, and undo — each performed
// the way a person performs it and read back off every pane it should have moved: the list the message left, the counts
// the folder column draws, the folder it went to, the notice offering the way back, and what a reload draws. A unit test
// of the control that issued an act sees the answer it was handed and nothing of what the other panes hold afterwards,
// which is where the client's defects with these acts have been.
//
// Each act is reached from the row's own menu and from the toolbar over the message that is open, because the two are
// different controls onto the same acts and a client can wire one of them to the wrong message. The selection bar is
// the third, and `client.selection.spec.ts` drives it.

/**
 * The row of one message whatever its reserved line says, which is gone only once the row has left the list.
 *
 * `row` stops matching the moment an act is asked of the message, because the row's name then ends in what the act is
 * doing — while the row stays where it was through its leaving animation, and the rows below it move up only after.
 * A pointer gesture aimed at a row below one still leaving lands on whichever row has slid under it by then.
 */
function leaving(page: Page, subject: string): Locator {
    return page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: new RegExp(`\\b${subject}\\b`, 'u') });
}

/** Every record the page asked to take back, in the order it asked. */
function recordsWithdrawn(deployment: FakeDeployment): string[] {
    return bodiesOf(deployment, '/mutations/deletes/withdrawals').flatMap((body) =>
        typeof body === 'object' && body !== null && 'recordIds' in body && Array.isArray(body.recordIds)
            ? body.recordIds.map(String)
            : [],
    );
}

test('files a deleted message into the trash and puts it back where it was on undo, out of the trash it was shown in', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    await fromTheMenu(page, 'Message 0', 'Delete');

    const question = page.getByRole('dialog', { name: 'Delete 1 message?' });

    await expect(question).toContainText('Each one is filed in the trash folder of the account it is in.');
    await question.getByRole('button', { name: 'Move to the trash' }).click();

    await expect(notice(page, 'Moved to the trash')).toContainText('1 message');
    await expect(row(page, 'Message 0')).toHaveCount(0);

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        { moves: [{ storedEmailId: 'message-0', destinationFolder: 'TRASH' }] },
    ]);

    await folder(page, 'Trash').click();
    await expect(row(page, 'Message 0')).toBeVisible();

    await notice(page, 'Moved to the trash').getByRole('button', { name: 'Undo' }).click();

    await expect(notice(page, 'Put back where it was')).toContainText('1 message');
    await expect(page.getByText('There is no mail in this folder.')).toBeVisible();

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        { moves: [{ storedEmailId: 'message-0', destinationFolder: 'TRASH' }] },
        { moves: [{ storedEmailId: 'message-0', destinationFolder: 'INBOX' }] },
    ]);

    await folder(page, 'Inbox 12 unread').click();
    await expect(row(page, 'Message 0')).toBeVisible();
});

test('destroys a message deleted from the trash only after the question saying so, and puts it back in the trash on undo', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    await fromTheMenu(page, 'Message 2', 'Delete');
    await page
        .getByRole('dialog', { name: 'Delete 1 message?' })
        .getByRole('button', { name: 'Move to the trash' })
        .click();
    await folder(page, 'Trash').click();
    await expect(row(page, 'Message 2')).toBeVisible();

    await fromTheMenu(page, 'Message 2', 'Delete');

    const question = page.getByRole('dialog', { name: 'Delete 1 message?' });

    await expect(question).toContainText('You can take this back for 5 seconds afterwards.');

    const deleting = page.waitForResponse(
        (response) =>
            response.request().method() === 'POST' && response.url().endsWith('/api/client/mutations/deletes'),
    );

    await question.getByRole('button', { name: 'Delete', exact: true }).click();

    await expect(notice(page, 'Deleting…')).toContainText('1 message');
    await expect(page.getByText('There is no mail in this folder.')).toBeVisible();

    // Asked in the trash, the delete takes the route of its own rather than a move into the folder the message is
    // already in, and nothing tells the deployment to stop holding it: the way back is on the notice.
    expect(bodiesOf(deployment, '/mutations/deletes')).toStrictEqual([{ deletes: [{ storedEmailId: 'message-2' }] }]);
    expect(bodiesOf(deployment, '/mutations/moves')).toHaveLength(1);

    await notice(page, 'Deleting…').getByRole('button', { name: 'Undo' }).click();

    await expect(notice(page, 'Put back where it was')).toContainText('1 message');
    await expect(row(page, 'Message 2')).toBeVisible();
    expect(bodiesOf(deployment, '/mutations/deletes/releases')).toHaveLength(0);

    // What is taken back is the record the delete answered with, rather than any record the page happened to be holding.
    const written: unknown = await (await deleting).json();

    expect(written).toMatchObject({
        results: [
            { storedEmailId: 'message-2', outcome: 'recorded', change: { recordId: recordsWithdrawn(deployment)[0] } },
        ],
    });

    // The message is still in the trash on a read made afresh, which is what says the delete was withdrawn rather
    // than merely no longer drawn.
    await page.reload();
    await folder(page, 'Trash').click();
    await expect(row(page, 'Message 2')).toBeVisible();
});

test('files a message by the menu and another by dragging it onto a folder, and the folder holds both', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    await fromTheMenu(page, 'Message 4', 'Move…');

    const choice = page.getByRole('dialog', { name: 'File in another folder' });

    await choice.getByRole('button', { name: 'Archive / 2024' }).click();

    await expect(notice(page, 'Filed in “Archive / 2024”')).toContainText('1 message');
    await expect(leaving(page, 'Message 4')).toHaveCount(0);

    // The level the corpus files that folder under arrives shut, so it is opened the way a reader opens it.
    await page.getByRole('treeitem', { name: 'Archive', expanded: false }).click();
    await page.keyboard.press('ArrowRight');

    await row(page, 'Message 7').dragTo(folder(page, '2024'));

    await expect(notice(page, 'Filed in “Archive / 2024”').filter({ hasText: '1 message' })).toHaveCount(2);
    await expect(leaving(page, 'Message 7')).toHaveCount(0);
    await expect(row(page, 'Message 6')).toBeVisible();

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        { moves: [{ storedEmailId: 'message-4', destinationFolder: 'ARCHIVE/2024' }] },
        { moves: [{ storedEmailId: 'message-7', destinationFolder: 'ARCHIVE/2024' }] },
    ]);

    await folder(page, '2024').click();
    await expect(row(page, 'Message 4')).toBeVisible();
    await expect(row(page, 'Message 7')).toBeVisible();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option')).toHaveCount(2);
});

test('draws a flag and a read mark on the row, in the count, and again after a reload, both ways', async ({
    page,
    deployment,
}) => {
    deployment.grant(...actingGrants);

    await openSignedIn(page, '/#/mail');
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();

    await fromTheMenu(page, 'Message 8', 'Flag');
    await expect(row(page, 'Message 8')).toHaveAccessibleName(/\bFlagged\b/u);

    await fromTheMenu(page, 'Message 12', 'Mark as read');
    await expect(row(page, 'Message 12')).not.toHaveAccessibleName(/\bUnread\b/u);

    // The count is the deployment's, and this one serves no signal channel, so the reload is what reads it again.
    await page.reload();

    await expect(folder(page, 'Inbox 11 unread')).toBeVisible();
    await expect(row(page, 'Message 8')).toHaveAccessibleName(/\bFlagged\b/u);
    await expect(row(page, 'Message 12')).not.toHaveAccessibleName(/\bUnread\b/u);

    await fromTheMenu(page, 'Message 8', 'Remove the flag');
    await expect(row(page, 'Message 8')).not.toHaveAccessibleName(/\bFlagged\b/u);

    await fromTheMenu(page, 'Message 12', 'Mark as unread');
    await expect(row(page, 'Message 12')).toHaveAccessibleName(/\bUnread\b/u);

    await page.reload();

    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();
    await expect(row(page, 'Message 8')).not.toHaveAccessibleName(/\bFlagged\b/u);
    await expect(row(page, 'Message 12')).toHaveAccessibleName(/\bUnread\b/u);

    expect(bodiesOf(deployment, '/mutations/flags')).toStrictEqual([
        { changes: [{ storedEmailId: 'message-8', flags: { flagged: true } }] },
        { changes: [{ storedEmailId: 'message-12', flags: { seen: true } }] },
        { changes: [{ storedEmailId: 'message-8', flags: { flagged: false } }] },
        { changes: [{ storedEmailId: 'message-12', flags: { seen: false } }] },
    ]);
});

test('files every message of a selection into the trash, and the counts move by all of them', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();

    await row(page, 'Message 6').click({ modifiers: ['ControlOrMeta'] });
    await row(page, 'Message 12').click({ modifiers: ['ControlOrMeta'] });

    const selection = page.getByRole('toolbar', { name: 'Actions on the messages selected' });

    await expect(selection.getByRole('status')).toHaveText('2 selected');
    await selection.getByRole('button', { name: 'Delete' }).click();
    await page
        .getByRole('dialog', { name: 'Delete 2 messages?' })
        .getByRole('button', { name: 'Move to the trash' })
        .click();

    await expect(notice(page, 'Moved to the trash')).toContainText('2 messages');
    await expect(row(page, 'Message 6')).toHaveCount(0);
    await expect(row(page, 'Message 12')).toHaveCount(0);

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        {
            moves: [
                { storedEmailId: 'message-6', destinationFolder: 'TRASH' },
                { storedEmailId: 'message-12', destinationFolder: 'TRASH' },
            ],
        },
    ]);

    // The count is the deployment's, and this one serves no signal channel, so it moves when the client reads
    // everything again on its own. The inbox is the one folder the column counts.
    await page.clock.runFor(refreshInterval);

    await expect(folder(page, 'Inbox 10 unread')).toBeVisible();
    await folder(page, 'Trash').click();
    await expect(row(page, 'Message 6')).toBeVisible();
    await expect(row(page, 'Message 12')).toBeVisible();
});

test('marks the open message unread and flags it from the toolbar, and the row and the count agree, then and after the next read', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();

    // Opening it is what marks it read, so the toolbar is asked to undo something the client did rather than something
    // the corpus stated — which is the case where the count and the row have two opinions to reconcile.
    await row(page, 'Message 6').click();
    await expect(folder(page, 'Inbox 11 unread')).toBeVisible();

    const toolbar = page.getByRole('toolbar', { name: 'Mail actions' });

    await toolbar.getByRole('button', { name: 'Mark unread' }).click();
    await expect(row(page, 'Message 6')).toHaveAccessibleName(/\bUnread\b/u);
    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();

    // Flagging it next writes the other flag, and the row goes on saying it is unread as well as flagged.
    await toolbar.getByRole('button', { name: 'Flag' }).click();
    await expect(row(page, 'Message 6')).toHaveAccessibleName(/\bFlagged\b/u);
    await expect(row(page, 'Message 6')).toHaveAccessibleName(/\bUnread\b/u);
    await expect(toolbar.getByRole('button', { name: 'Unflag' })).toBeVisible();

    // The opening says it is one, which is what tells the deployment it may report the reading back; the toolbar's two
    // changes are not openings and say nothing.
    expect(bodiesOf(deployment, '/mutations/flags')).toStrictEqual([
        { changes: [{ storedEmailId: 'message-6', flags: { seen: true } }], opened: true },
        { changes: [{ storedEmailId: 'message-6', flags: { seen: false } }] },
        { changes: [{ storedEmailId: 'message-6', flags: { flagged: true } }] },
    ]);

    // The client reads everything again on its own, and what the deployment answers then agrees with what was drawn —
    // with the message still open, which marks nothing read a second time.
    await page.clock.runFor(refreshInterval);

    await expect(folder(page, 'Inbox 12 unread')).toBeVisible();
    await expect(row(page, 'Message 6')).toHaveAccessibleName(/\bUnread\b/u);
    await expect(row(page, 'Message 6')).toHaveAccessibleName(/\bFlagged\b/u);
    expect(bodiesOf(deployment, '/mutations/flags')).toHaveLength(3);
});

test('archives the open message from the toolbar and puts it back where it was on undo', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    await row(page, 'Message 7').click();
    await page.getByRole('toolbar', { name: 'Mail actions' }).getByRole('button', { name: 'Archive' }).click();

    await expect(notice(page, 'Archived')).toContainText('1 message');
    await expect(row(page, 'Message 7')).toHaveCount(0);

    await openTheArchive(page);
    await expect(row(page, 'Message 7')).toBeVisible();

    await notice(page, 'Archived').getByRole('button', { name: 'Undo' }).click();

    await expect(notice(page, 'Put back where it was')).toContainText('1 message');
    await expect(page.getByText('There is no mail in this folder.')).toBeVisible();

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        { moves: [{ storedEmailId: 'message-7', destinationFolder: 'FILED' }] },
        { moves: [{ storedEmailId: 'message-7', destinationFolder: 'INBOX' }] },
    ]);

    // By its name alone: the message opened here was counted read on opening, which is not what this check is about.
    await page.getByRole('treeitem', { name: /^Inbox\b/u }).click();
    await expect(row(page, 'Message 7')).toBeVisible();
});

test('deletes one open message and files another from the toolbar, and each folder holds the one it was sent', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    const toolbar = page.getByRole('toolbar', { name: 'Mail actions' });

    await row(page, 'Message 7').click();
    await toolbar.getByRole('button', { name: 'Delete' }).click();
    await page
        .getByRole('dialog', { name: 'Delete 1 message?' })
        .getByRole('button', { name: 'Move to the trash' })
        .click();

    await expect(notice(page, 'Moved to the trash')).toContainText('1 message');
    await expect(row(page, 'Message 7')).toHaveCount(0);

    await row(page, 'Message 8').click();
    await toolbar.getByRole('button', { name: 'Move' }).click();
    await page
        .getByRole('dialog', { name: 'File in another folder' })
        .getByRole('button', { name: 'Archive / 2024' })
        .click();

    await expect(notice(page, 'Filed in “Archive / 2024”')).toContainText('1 message');
    await expect(row(page, 'Message 8')).toHaveCount(0);

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        { moves: [{ storedEmailId: 'message-7', destinationFolder: 'TRASH' }] },
        { moves: [{ storedEmailId: 'message-8', destinationFolder: 'ARCHIVE/2024' }] },
    ]);

    await folder(page, 'Trash').click();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option')).toHaveCount(1);
    await expect(row(page, 'Message 7')).toBeVisible();

    await page.getByRole('treeitem', { name: 'Archive', expanded: false }).click();
    await page.keyboard.press('ArrowRight');
    await folder(page, '2024').click();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option')).toHaveCount(1);
    await expect(row(page, 'Message 8')).toBeVisible();
});

// Which message is open is the workspace's, the row drawn as the open one is the list's, and the act is asked from a
// toolbar, a menu, or a folder of the tree — so a message filed elsewhere while it is open is three components
// agreeing on what the reader is handed next: the row after it, the row before it at the foot of the list, and
// nothing once nothing stays. The corpus answers every message of the generated folder with one body, so which
// message the pane holds is read off the request the page made for it rather than off words all of them share.

/** That the list draws this row as the open one, and that the pane asked the deployment for the message it is. */
async function isTheOpenOne(page: Page, deployment: FakeDeployment, subject: string): Promise<void> {
    const storedEmailId = subject.toLowerCase().replace(' ', '-');

    await expect(leaving(page, subject)).toHaveAttribute('aria-current', 'true');
    await expect.poll(() => deployment.requests('GET', `/messages/${storedEmailId}`).length).toBeGreaterThan(0);
    await expect(page.getByRole('article', messageRegion)).toBeVisible();
}

test('opens the next message when the open one is archived from the toolbar or deleted from its menu, and leaves it open when another row goes', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    await row(page, 'Message 7').click();
    await isTheOpenOne(page, deployment, 'Message 7');

    await page.getByRole('toolbar', { name: 'Mail actions' }).getByRole('button', { name: 'Archive' }).click();

    await expect(notice(page, 'Archived')).toContainText('1 message');
    await expect(leaving(page, 'Message 7')).toHaveCount(0);
    await isTheOpenOne(page, deployment, 'Message 8');

    // The message the list opened by itself is acted on from its own row, and the list moves on again.
    await fromTheMenu(page, 'Message 8', 'Delete');
    await page
        .getByRole('dialog', { name: 'Delete 1 message?' })
        .getByRole('button', { name: 'Move to the trash' })
        .click();

    await expect(notice(page, 'Moved to the trash')).toContainText('1 message');
    await expect(leaving(page, 'Message 8')).toHaveCount(0);
    await isTheOpenOne(page, deployment, 'Message 9');

    // A row that is not the open one leaves, and the reader stays on the message they were reading.
    await fromTheMenu(page, 'Message 6', 'Archive');

    await expect(notice(page, 'Archived').filter({ hasText: '1 message' })).toHaveCount(2);
    await expect(leaving(page, 'Message 6')).toHaveCount(0);
    await expect(leaving(page, 'Message 9')).toHaveAttribute('aria-current', 'true');
    await expect(leaving(page, 'Message 10')).not.toHaveAttribute('aria-current');
    await expect(leaving(page, 'Message 5')).not.toHaveAttribute('aria-current');

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        { moves: [{ storedEmailId: 'message-7', destinationFolder: 'FILED' }] },
        { moves: [{ storedEmailId: 'message-8', destinationFolder: 'TRASH' }] },
        { moves: [{ storedEmailId: 'message-6', destinationFolder: 'FILED' }] },
    ]);
});

test('opens the row before it when the open message leaves from the foot of the list, and nothing once the list holds none', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    // A folder of two messages, so the list has a foot a reader can stand on.
    for (const subject of ['Message 7', 'Message 8']) {
        await fromTheMenu(page, subject, 'Archive');
        await expect(leaving(page, subject)).toHaveCount(0);
    }

    await openTheArchive(page);

    const listed = page.getByRole('listbox', { name: 'Messages' }).getByRole('option');
    const toolbar = page.getByRole('toolbar', { name: 'Mail actions' });
    const question = page.getByRole('dialog', { name: 'Delete 1 message?' });

    await expect(listed).toHaveCount(2);

    const above = subjectOf((await listed.first().textContent()) ?? '');
    const atTheFoot = subjectOf((await listed.last().textContent()) ?? '');

    await listed.last().click();
    await isTheOpenOne(page, deployment, atTheFoot);

    await toolbar.getByRole('button', { name: 'Delete' }).click();
    await question.getByRole('button', { name: 'Move to the trash' }).click();

    await expect(leaving(page, atTheFoot)).toHaveCount(0);
    await isTheOpenOne(page, deployment, above);

    // The last message of the folder leaves it open, and there is none to hand the reader in its place.
    await toolbar.getByRole('button', { name: 'Delete' }).click();
    await question.getByRole('button', { name: 'Move to the trash' }).click();

    await expect(page.getByText('There is no mail in this folder.')).toBeVisible();
    await expect(listed).toHaveCount(0);
    await expect(page.getByRole('article', messageRegion)).toHaveCount(0);

    await folder(page, 'Trash').click();
    await expect(listed).toHaveCount(2);
});

test('opens the next message when the open one is dragged onto a folder', async ({ page, deployment }) => {
    await openTheInbox(page, deployment);

    // The level the corpus files that folder under arrives shut, so it is opened the way a reader opens it.
    await page.getByRole('treeitem', { name: 'Archive', expanded: false }).click();
    await page.keyboard.press('ArrowRight');
    await expect(folder(page, '2024')).toBeVisible();

    await row(page, 'Message 7').click();
    await isTheOpenOne(page, deployment, 'Message 7');

    await row(page, 'Message 7').dragTo(folder(page, '2024'));

    await expect(notice(page, 'Filed in “Archive / 2024”')).toContainText('1 message');
    await expect(leaving(page, 'Message 7')).toHaveCount(0);
    await isTheOpenOne(page, deployment, 'Message 8');

    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
        { moves: [{ storedEmailId: 'message-7', destinationFolder: 'ARCHIVE/2024' }] },
    ]);

    await folder(page, '2024').click();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option')).toHaveCount(1);
    await expect(row(page, 'Message 7')).toBeVisible();
});

test.describe('at the phone composition', () => {
    test.use({ viewport: phoneWindow, hasTouch: true, isMobile: true });

    /** Carries a row aside with a finger, as far as a swipe commits, the way the pointer events of one arrive. */
    async function swipe(page: Page, subject: string, across: number): Promise<void> {
        const carried = row(page, subject);
        const box = await carried.boundingBox();

        if (box === null) {
            throw new Error(`The row ending ${subject} is not drawn.`);
        }

        const at = { pointerId: 7, pointerType: 'touch', isPrimary: true, clientY: box.y + box.height / 2 };
        const from = box.x + box.width / 2;

        await carried.dispatchEvent('pointerdown', { ...at, clientX: from });
        await carried.dispatchEvent('pointermove', { ...at, clientX: from + across / 2 });
        await carried.dispatchEvent('pointermove', { ...at, clientX: from + across });
        await carried.dispatchEvent('pointerup', { ...at, clientX: from + across });
    }

    test('archives a row swiped right into the folder the archive in its menu files one in', async ({
        page,
        deployment,
    }) => {
        await openTheInbox(page, deployment);

        // The row's menu first, because its archive is offered only once the folders are known — which is what a swipe
        // needs too, and has nothing of its own to wait on.
        await fromTheMenu(page, 'Message 7', 'Archive');

        await expect(notice(page, 'Archived')).toContainText('1 message');
        await expect(row(page, 'Message 7')).toHaveCount(0);

        await swipe(page, 'Message 6', 140);

        await expect(notice(page, 'Archived').filter({ hasText: '1 message' })).toHaveCount(2);
        await expect(row(page, 'Message 6')).toHaveCount(0);

        expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([
            { moves: [{ storedEmailId: 'message-7', destinationFolder: 'FILED' }] },
            { moves: [{ storedEmailId: 'message-6', destinationFolder: 'FILED' }] },
        ]);
    });

    test('answers a row swiped left the way the reply control answers the message it opens', async ({
        page,
        deployment,
    }) => {
        deployment.grant('mailfathom.mail.drafts.write');

        await openTheInbox(page, deployment);

        const reply = page.getByRole('dialog', { name: 'Reply' });

        await row(page, 'Message 7').click();
        await page.getByRole('button', { name: 'Reply', exact: true }).click();

        await expect(reply.getByRole('button', { name: 'Remove news@example.invalid from To' })).toBeVisible();

        await reply.getByRole('button', { name: 'Close the message' }).click();
        await page.getByRole('button', { name: 'Back to the list' }).click();

        await swipe(page, 'Message 8', -140);

        await expect(reply.getByRole('button', { name: 'Remove news@example.invalid from To' })).toBeVisible();

        // Each read the message it answers, whose sender is who the reply is addressed to.
        expect(deployment.requests('GET', '/messages/message-7')).not.toHaveLength(0);
        expect(deployment.requests('GET', '/messages/message-8')).not.toHaveLength(0);
    });
});
