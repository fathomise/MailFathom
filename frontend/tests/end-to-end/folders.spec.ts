// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { randomUUID } from 'node:crypto';
import { expect, test, type Page } from '@playwright/test';
import { foldersOnTheMailServer } from './mailbox';
import { notice, openMail, settles } from './mailScreen';

// A folder made, renamed, moved, and deleted through the tree against the real deployment, and held to the tree, the
// tree after a reload, and the folder list the mail server itself reports — a folder act is carried to the server while
// the request is still open, so the server is the witness that it was carried at all. Its name carries a mark drawn
// for the spec, so a run against a mailbox an earlier run left a folder in still makes one of its own.

// Paced by the service rather than by the page, as `mailbox-acts.spec.ts` says.
test.describe.configure({ timeout: 4 * 60_000 });

/** The account the run records, by the name the client writes in what it says about a folder act. */
const mailbox = 'End-to-end mailbox';

/** Reloads the page and waits for the tree of folders to be drawn again from the deployment. */
async function reloaded(page: Page): Promise<void> {
    await page.reload();

    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
}

/** Opens a folder of the tree that holds others, wherever it stands, so the folders inside it are drawn. */
async function opened(page: Page, name: string): Promise<void> {
    const tree = page.getByRole('tree', { name: 'Mailboxes and folders' });
    const shut = tree.getByRole('treeitem', { name, exact: true, expanded: false });
    const open = tree.getByRole('treeitem', { name, exact: true, expanded: true });

    await expect(shut.or(open)).toBeVisible(settles);

    if ((await shut.count()) > 0) {
        await shut.focus();
        await page.keyboard.press('ArrowRight');
    }

    await expect(open).toBeVisible();
}

/** Asks a folder's own menu for an act. */
async function fromTheFolderMenu(page: Page, name: string, act: string): Promise<void> {
    await page
        .getByRole('tree', { name: 'Mailboxes and folders' })
        .getByRole('treeitem', { name, exact: true })
        .click({ button: 'right' });
    await page.getByRole('menuitem', { name: act, exact: true }).click();
}

/** Waits until the mail server's folder list holds the folders named and none of those refused. */
async function theMailServerLists(holds: readonly string[], refuses: readonly string[]): Promise<void> {
    await expect
        .poll(
            async () => {
                const listed = await foldersOnTheMailServer();

                return holds.every((name) => listed.includes(name)) && refuses.every((name) => !listed.includes(name));
            },
            {
                ...settles,
                message: `The mail server lists ${holds.join(', ') || 'nothing asked for'} and not ${refuses.join(', ') || 'anything refused'}`,
            },
        )
        .toBe(true);
}

test('makes a folder, renames it, moves it inside another, and deletes it, and the tree, a reload, and the mail server agree after each', async ({
    page,
}) => {
    const mark = randomUUID().slice(0, 6);
    const made = `Suppliers ${mark}`;
    const renamed = `Vendors ${mark}`;
    const tree = page.getByRole('tree', { name: 'Mailboxes and folders' });
    const named = (name: string) => tree.getByRole('treeitem', { name, exact: true });

    await openMail(page);

    await fromTheFolderMenu(page, mailbox, 'New folder');

    const making = page.getByRole('dialog', { name: 'New folder' });

    await making.getByRole('textbox', { name: 'Folder name' }).fill(made);
    await making.getByRole('button', { name: 'Create folder' }).click();

    await expect(notice(page, `Created ${made} in ${mailbox}.`)).toBeVisible();
    await theMailServerLists([made], []);

    await reloaded(page);
    await expect(named(made)).toHaveAttribute('aria-level', '2', settles);

    await fromTheFolderMenu(page, made, 'Edit folder');

    const renaming = page.getByRole('dialog', { name: 'Edit folder' });

    await renaming.getByRole('textbox', { name: 'Folder name' }).fill(renamed);
    await renaming.getByRole('button', { name: 'Save changes' }).click();

    await expect(notice(page, `Renamed to ${renamed} in ${mailbox}.`)).toBeVisible();
    await expect(named(made)).toHaveCount(0);
    await theMailServerLists([renamed], [made]);

    await reloaded(page);
    await expect(named(renamed)).toHaveAttribute('aria-level', '2', settles);
    await expect(named(made)).toHaveCount(0);

    // Moved inside a folder rather than out of one: the mail server this run starts answers a rename out of a level OK
    // and leaves the folder where it was, so the direction that server carries out is the one a spec can hold it to.
    await fromTheFolderMenu(page, renamed, 'Edit folder');

    const moving = page.getByRole('dialog', { name: 'Edit folder' });

    await moving.getByRole('combobox', { name: 'Inside' }).selectOption({ label: 'Archive' });
    await moving.getByRole('button', { name: 'Save changes' }).click();

    await expect(notice(page, `Moved ${renamed} in ${mailbox}.`)).toBeVisible();
    await theMailServerLists([`Archive.${renamed}`], [renamed]);

    await reloaded(page);
    await opened(page, 'Archive');
    await expect(named(renamed)).toHaveAttribute('aria-level', '3', settles);

    await fromTheFolderMenu(page, renamed, 'Delete folder');

    const deleting = page.getByRole('dialog', { name: `Delete ${renamed}?` });

    await expect(deleting).toContainText(`${renamed} leaves ${mailbox}.`);
    await deleting.getByRole('button', { name: 'Delete folder' }).click();

    await expect(named(renamed)).toHaveCount(0, settles);
    await theMailServerLists([], [renamed, `Archive.${renamed}`]);

    await reloaded(page);
    await expect(tree.getByRole('treeitem', { name: 'Archive', exact: true })).toBeVisible();
    await expect(named(renamed)).toHaveCount(0);
});
