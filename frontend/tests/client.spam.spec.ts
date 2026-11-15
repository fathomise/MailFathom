// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import { bodiesOf, desktopWindow, folder, notice, openTheInbox, phoneWindow, settled, test } from './client.harness';
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

/** Asks the row's own menu for one of the two spam acts, each of which asks before it does anything. */
async function fromTheMenu(page: Page, subject: string, act: 'Spam…' | 'Not spam…'): Promise<void> {
    await listed(page, subject).click({ button: 'right' });
    await page.getByRole('menuitem', { name: act, exact: true }).click();
}

// Withdrawing a mark lets the message be answered and its links followed again, so it is asked first whether or not
// anything moves — and what the question was about is the row's, the notice's, and the next read's to agree on.
test('asks before marking a message not spam where nothing moves, sends nothing on Cancel, and unfolds the row once confirmed', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);

    await fromTheMenu(page, 'Message 2', 'Spam…');

    const chooser = page.getByRole('dialog', { name: 'Mark 1 message as spam' });

    await chooser.getByRole('radio', { name: /^Marketing/u }).check();
    await chooser.getByRole('button', { name: 'Mark as spam' }).click();
    await expect(listed(page, 'Message 2')).toHaveAccessibleName(/Spam: Marketing, marked by you\./u);

    const question = page.getByRole('dialog', { name: 'Mark this message not spam?' });

    await fromTheMenu(page, 'Message 2', 'Not spam…');

    await expect(question).toContainText('It is marked not spam and stays where it is.');
    await expect(question.getByRole('button', { name: 'Not spam, move to Inbox' })).toHaveCount(0);
    await question.getByRole('button', { name: 'Cancel' }).click();

    // Cancelled, the question is gone and the mark stands: the one request is the mark that made it spam.
    await expect(question).toBeHidden();
    await expect(listed(page, 'Message 2')).toHaveAccessibleName(/Spam: Marketing, marked by you\./u);
    expect(bodiesOf(deployment, '/mutations/spam')).toHaveLength(1);

    await fromTheMenu(page, 'Message 2', 'Not spam…');
    await question.getByRole('button', { name: 'Not spam', exact: true }).click();

    await expect(notice(page, 'Marked not spam - 1')).toContainText('It stays where it is and no longer folds away.');
    await expect(listed(page, 'Message 2')).not.toHaveAccessibleName(/Spam/u);

    expect(bodiesOf(deployment, '/mutations/spam')).toStrictEqual([
        { verdict: 'Spam', spamSubcategory: 'Marketing', storedEmailIds: ['message-2'] },
        { verdict: 'NotSpam', spamSubcategory: null, storedEmailIds: ['message-2'] },
    ]);

    await page.reload();

    await expect(listed(page, 'Message 4')).toBeVisible();
    await expect(listed(page, 'Message 2')).not.toHaveAccessibleName(/Spam/u);
});

// Where the account files junk, the same press moves the message, so the question says where to and the confirmation
// names it — and the row that leaves the Spam folder is one the inbox has to hold afterwards.
test('moves a message marked not spam out of the Spam folder into the inbox where the account files junk, after saying so', async ({
    page,
    deployment,
}) => {
    deployment.fileJunk();
    await openTheInbox(page, deployment);
    await folder(page, 'Spam').click();
    await expect(listed(page, phishingSubject)).toBeVisible();

    await fromTheMenu(page, phishingSubject, 'Not spam…');

    const question = page.getByRole('dialog', { name: 'Mark this message not spam?' });

    await expect(question).toContainText('It is marked not spam and moved to the Inbox.');
    await question.getByRole('button', { name: 'Not spam, move to Inbox' }).click();

    await expect(notice(page, 'Marked not spam - 1')).toContainText('It is in the Inbox now.');
    await expect(listed(page, phishingSubject)).toHaveCount(0);
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option')).toHaveCount(3);

    expect(bodiesOf(deployment, '/mutations/spam')).toStrictEqual([
        { verdict: 'NotSpam', spamSubcategory: null, storedEmailIds: ['message-spam-desk'] },
    ]);
    expect(bodiesOf(deployment, '/mutations/moves')).toStrictEqual([]);

    await page.getByRole('treeitem', { name: /^Inbox\b/u }).click();
    await expect(listed(page, 'Message 0')).toBeVisible();
    await expect(listed(page, phishingSubject)).toBeVisible();
    await expect(listed(page, phishingSubject)).not.toHaveAccessibleName(/Spam|Phishing/u);

    await page.reload();

    await expect(listed(page, phishingSubject)).toBeVisible();
    await folder(page, 'Spam').click();
    await expect(page.getByRole('listbox', { name: 'Messages' }).getByRole('option')).toHaveCount(3);
    await expect(listed(page, phishingSubject)).toHaveCount(0);
});

/** How tall a listed row stands, which only a browser lays out. */
async function heightOf(page: Page, subject: string): Promise<number> {
    return (await listed(page, subject).boundingBox())?.height ?? Number.NaN;
}

/** Where a listed row stands in the window, read once it has stopped moving. */
async function restingTopOf(page: Page, subject: string): Promise<number> {
    let previous = Number.NaN;

    await expect
        .poll(async () => {
            const now = (await listed(page, subject).boundingBox())?.y ?? Number.NaN;
            const rested = now === previous;

            previous = now;

            return rested;
        })
        .toBe(true);

    return previous;
}

/** Marks the messages picked out spam from the selection bar, as marketing. */
async function markPickedSpam(page: Page): Promise<void> {
    await selectionBar(page).getByRole('button', { name: 'Spam', exact: true }).click();

    const chooser = page.getByRole('dialog', { name: /^Mark \d+ messages? as spam$/u });

    await chooser.getByRole('radio', { name: /^Marketing/u }).check();
    await chooser.getByRole('button', { name: 'Mark as spam' }).click();
}

/**
 * Leaves the message picked out above the top of the list, still drawn and out of sight, with the keyboard on a row the
 * reader is reading. The keyboard moves first and picks nothing out on its own, so the act that follows hands focus
 * back to a row in sight rather than scrolling the picked one back into it.
 */
async function readBelow(page: Page, picked: string, reading: string): Promise<void> {
    await pickOut(page, picked);

    for (let step = 0; step < 4; step += 1) {
        await page.keyboard.press('ArrowDown');
    }

    await expect(listed(page, reading)).toBeFocused();
    await listed(page, reading).hover();
    await page.mouse.wheel(0, 2 * (await heightOf(page, 'Message 0')));
    await expect(listed(page, picked)).not.toBeInViewport();
    await expect(listed(page, reading)).toBeInViewport({ ratio: 1 });
}

test('draws a spam row folded outside the Spam folder at a height of its own, shorter than the rows around it', async ({
    page,
    deployment,
}) => {
    await openTheInbox(page, deployment);
    await pickOut(page, 'Message 2');
    await markPickedSpam(page);

    await expect(listed(page, 'Message 2')).toHaveAccessibleName(/Spam: Marketing, marked by you\./u);
    await expect.poll(() => heightOf(page, 'Message 2')).toBeLessThan(await heightOf(page, 'Message 1'));
    expect(await heightOf(page, 'Message 4')).toBe(await heightOf(page, 'Message 1'));

    // The window is laid out from those heights, so the row under the folded one starts where the folded one ends.
    const folded = await listed(page, 'Message 2').boundingBox();
    const next = await listed(page, 'The racking quote').boundingBox();

    expect(Math.abs((folded?.y ?? 0) + (folded?.height ?? 0) - (next?.y ?? Number.NaN))).toBeLessThanOrEqual(1);
});

test('folds a row above the reader without moving the row they are reading', async ({ page, deployment }) => {
    await openTheInbox(page, deployment);
    await readBelow(page, 'Message 1', 'Message 5');

    const reading = await restingTopOf(page, 'Message 5');

    await markPickedSpam(page);

    await expect(listed(page, 'Message 1')).toHaveAccessibleName(/Spam: Marketing, marked by you\./u);
    expect(await restingTopOf(page, 'Message 5')).toBe(reading);
});

test('unfolds a row above the reader without moving the row they are reading', async ({ page, deployment }) => {
    await openTheInbox(page, deployment);
    await pickOut(page, 'Message 1');
    await markPickedSpam(page);
    await expect(listed(page, 'Message 1')).toHaveAccessibleName(/Spam: Marketing, marked by you\./u);

    await readBelow(page, 'Message 1', 'Message 5');

    const reading = await restingTopOf(page, 'Message 5');

    await selectionBar(page).getByRole('button', { name: 'Not spam', exact: true }).click();
    await page
        .getByRole('dialog', { name: 'Mark this message not spam?' })
        .getByRole('button', { name: 'Not spam', exact: true })
        .click();

    await expect(listed(page, 'Message 1')).not.toHaveAccessibleName(/Spam/u);
    expect(await restingTopOf(page, 'Message 5')).toBe(reading);
});

// How wide the chooser stands is geometry, which only a browser lays out: a dialog of its own width in a wide window,
// and the whole width of the screen as a sheet on a phone.
test.describe('in the desktop window', () => {
    test.use({ viewport: desktopWindow });

    test('draws the spam chooser as a compact dialog rather than across the window', async ({ page, deployment }) => {
        await openTheInbox(page, deployment);
        await fromTheMenu(page, 'Message 2', 'Spam…');

        const chooser = page.getByRole('dialog', { name: 'Mark 1 message as spam' });

        await expect(chooser.getByRole('button', { name: 'Mark as spam' })).toBeVisible();
        await settled(page);

        const box = await chooser.boundingBox();

        // The dialog width of the design, 27.5rem, centred in the window.
        expect(box?.width).toBeGreaterThanOrEqual(439);
        expect(box?.width).toBeLessThanOrEqual(441);
        expect(Math.abs((box?.x ?? 0) + (box?.width ?? 0) / 2 - desktopWindow.width / 2)).toBeLessThanOrEqual(1);
    });
});

test.describe('at the phone composition', () => {
    test.use({ viewport: phoneWindow, hasTouch: true, isMobile: true });

    test('draws the spam chooser as a sheet the whole width of the screen', async ({ page, deployment }) => {
        await openTheInbox(page, deployment);
        await fromTheMenu(page, 'Message 2', 'Spam…');

        const chooser = page.getByRole('dialog', { name: 'Mark 1 message as spam' });

        await expect(chooser.getByRole('button', { name: 'Mark as spam' })).toBeVisible();
        await settled(page);

        const box = await chooser.boundingBox();

        expect(Math.abs(box?.x ?? Number.NaN)).toBeLessThanOrEqual(1);
        expect(Math.abs((box?.width ?? Number.NaN) - phoneWindow.width)).toBeLessThanOrEqual(1);
    });
});
