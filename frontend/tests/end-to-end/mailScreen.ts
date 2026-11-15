// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';
import { signIn } from './deployment';
import { messagesIn, unseenIn, type DeliveredMessage, type StoredMessage } from './mailbox';

// What the specs acting on mail share about the Mail space: reaching it, finding the row of a message a spec delivered,
// the folders and the counts the tree draws, the notice an act raises, and the wait on the mail server. Each is read by
// the role and the words the client draws, as everything in this directory is.

/** How long a change may take to reach the screen or the mail server: a synchronization run, and the converger after it. */
export const settles = { timeout: 60_000 } as const;

/** Signs in and reaches the Mail space, waiting for the tree of folders to have been drawn. */
export async function openMail(page: Page): Promise<void> {
    await signIn(page);
    await page.getByRole('link', { name: 'Mail' }).click();

    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
}

/** A subject written as the literal part of a pattern. */
function literally(subject: string): string {
    return subject.replaceAll(/[.*+?^${}()|[\]\\]/gu, '\\$&');
}

/**
 * The row of one message, by the subject a spec delivered it under.
 *
 * A reply quotes that subject behind `Re:`, which is what the lookbehind refuses, and the row of a message marked spam
 * draws its time after the subject rather than before it, which is what the alternation allows.
 */
export function messageRow(page: Page, subject: string): Locator {
    return page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: new RegExp(`(?<!Re: )${literally(subject)}(?: |$)`, 'u') });
}

/** One folder of the tree, by its name — the inbox's carrying its unread count, so it is found by its first word. */
export function folder(page: Page, name: string): Locator {
    const tree = page.getByRole('tree', { name: 'Mailboxes and folders' });

    return name === 'Inbox'
        ? tree.getByRole('treeitem', { name: /^Inbox\b/u })
        : tree.getByRole('treeitem', { name, exact: true });
}

/**
 * How many messages the tree says are unread in the inbox, read from the name the row is announced by — its text draws
 * the figure twice, once to see and once to hear — which carries no count when there are none.
 */
async function unreadInTheInbox(page: Page): Promise<number> {
    const announced = await folder(page, 'Inbox').ariaSnapshot();
    const count = /treeitem "Inbox (\d[\d,]*) unread"/u.exec(announced);

    return count === null ? 0 : Number((count[1] ?? '0').replaceAll(',', ''));
}

/**
 * Waits until the unread count the tree draws for the inbox is the count of unseen messages the mail server holds there.
 *
 * The two are read side by side rather than as a count before an act and one after it, because a count read the moment
 * the tree appears may not have arrived yet, and because agreeing with the mail server is the claim worth making: a
 * count the client moved and the server did not would be a person told their mail was read when it was not.
 */
export async function unreadCountsAgree(page: Page): Promise<void> {
    await expect
        .poll(async () => {
            const [drawn, held] = await Promise.all([unreadInTheInbox(page), unseenIn('INBOX')]);

            return `${String(drawn)} drawn, ${String(held)} unseen on the mail server`;
        }, settles)
        .toMatch(/^(\d+) drawn, \1 unseen on the mail server$/u);
}

/** The notice an act raised, found by what it says it did. */
export function notice(page: Page, title: string): Locator {
    return page.getByRole('list', { name: 'Notices' }).getByRole('listitem').filter({ hasText: title });
}

/** Asks a row's own menu for an act, which is how one message is acted on without opening it. */
export async function fromTheMenu(row: Locator, act: string): Promise<void> {
    await row.click({ button: 'right' });
    await row.page().getByRole('menuitem', { name: act, exact: true }).click();
}

/** Waits for messages a spec delivered to have been synchronized and drawn in the inbox, and counted there. */
export async function arrived(page: Page, ...delivered: readonly DeliveredMessage[]): Promise<void> {
    for (const message of delivered) {
        await expect(messageRow(page, message.subject)).toBeVisible(settles);
    }

    await unreadCountsAgree(page);
}

/**
 * Waits until the mail server holds what a spec expects of one subject in one folder, and answers with it.
 *
 * A change reaches the server after the client has drawn it — a move or a flag is carried there by the service's own
 * converger — so the wait is on the server's answer rather than on a duration.
 */
export async function onTheMailServer(
    folderName: string,
    subject: string,
    expected: (held: readonly StoredMessage[]) => boolean,
): Promise<readonly StoredMessage[]> {
    let held: readonly StoredMessage[] = [];

    await expect
        .poll(
            async () => {
                held = await messagesIn(folderName, subject);

                return expected(held);
            },
            { ...settles, message: `What ${folderName} holds of "${subject}" on the mail server` },
        )
        .toBe(true);

    return held;
}

/** The mail server holds exactly one message of the subject in the folder, and it carries every flag named and none refused. */
export function one({
    carrying = [],
    without = [],
}: { readonly carrying?: readonly string[]; readonly without?: readonly string[] } = {}): (
    held: readonly StoredMessage[],
) => boolean {
    return (held) =>
        held.length === 1 &&
        carrying.every((flag) => held[0]?.flags.includes(flag) === true) &&
        without.every((flag) => held[0]?.flags.includes(flag) !== true);
}

/** The mail server holds no message of the subject in the folder. */
export function none(held: readonly StoredMessage[]): boolean {
    return held.length === 0;
}
