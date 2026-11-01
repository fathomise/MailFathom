// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Locator, type Page } from '@playwright/test';

import {
    narrowWindow,
    openAccountMenu,
    openApplicationSettings,
    openSettings,
    openSignedIn,
    settled,
    test,
    wideWindow,
} from './client.harness';

// The account menu and the settings screen: the platform behaviour each owes a keyboard, the compositions the settings
// surface is drawn in, and the preferences a choice on them writes to the deployment and reads back.

/**
 * Every preferences document the page states, collected as it is sent.
 *
 * A control reports a choice and the write leaves afterwards, so anything asserted about it — its content, or merely
 * that it happened before a reload — waits on this list rather than on the click having returned.
 */
function statedPreferences(page: Page): string[] {
    const stated: string[] = [];

    page.on('request', (request) => {
        if (new URL(request.url()).pathname === '/api/client/preferences' && request.method() === 'POST') {
            stated.push(request.postData() ?? '');
        }
    });

    return stated;
}

/**
 * One segment of the theme chooser, as the label a pointer actually lands on.
 *
 * The input each segment is built around is hidden from sight rather than from the accessibility tree, so it is a
 * clipped pixel its own label covers — which is right for a person and wrong for a locator aimed at the input. What
 * receives the click in a browser is the label, so that is what this returns; the input is still what carries the role
 * and the name, and an assertion about either reaches it through the role.
 */
function themeSegment(page: Page, name: string): Locator {
    return page.getByRole('group', { name: 'Theme' }).getByText(name, { exact: true });
}

test('opens the account menu from its control and hands focus back to it on Escape', async ({ page }) => {
    await openSignedIn(page);

    // The menu is the platform's own popover, so what is asserted is the platform's contract rather than any code of
    // this client's: it opens from the control that names it, its controls are reachable once it is open, Escape
    // closes it, and focus returns to the control — none of which jsdom implements, so only a browser proves it.
    const control = page.getByRole('button', { name: 'Account and preferences' });
    await control.click();
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible();

    // The status row is the first thing under the person's name and the first thing a keyboard reaches, and the mailbox
    // rows follow it, because each is a control that puts that mailbox in scope rather than a line to read.
    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Your status' })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('button', { name: 'Work' })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('switch', { name: /Tab mode/u })).toBeFocused();

    await page.keyboard.press('Tab');
    await expect(page.getByRole('radio', { name: 'Auto' })).toBeFocused();

    // Escape is pressed from the way out rather than from a chooser, which takes the key for its own list.
    await page.getByRole('button', { name: 'Sign out' }).focus();
    await page.keyboard.press('Escape');
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeHidden();
    await expect(control).toBeFocused();
});

test('opens again in the theme that was chosen, after the page is loaded afresh', async ({ page }) => {
    const stated = statedPreferences(page);

    await openSignedIn(page);

    await expect(page.locator('html')).toHaveAttribute('data-theme', 'light');

    await openAccountMenu(page);
    await themeSegment(page, 'Dark').click();

    // Waited for rather than assumed: a click returns once it is dispatched, and navigating away from a document can
    // cancel a request the previous one had in flight — so a reload here could outrun the write it is about to prove.
    await expect.poll(() => stated).toHaveLength(1);
    await page.reload();

    // Only a real document loaded a second time proves the choice was written and read back. What it is asserted
    // through is the one attribute the whole token layer is declared against, which is what a screen is painted by.
    // With the deployment answering what it was last told, this reaches past the device store as well: a client that
    // wrote nothing would be handed `system` on the way back and paint whatever the machine prefers.
    await expect(page.locator('html')).toHaveAttribute('data-theme', 'dark');
});

test('states the whole preferences document to the deployment when one of them is chosen', async ({ page }) => {
    const stated = statedPreferences(page);

    await openSignedIn(page);
    await openAccountMenu(page);
    await themeSegment(page, 'Dark').click();

    // Only a browser proves this: what a screen hands the transport is one thing, and what `fetch` actually puts on
    // the wire is another — an adapter dropping the body would leave every assertion above green while the deployment
    // stored an empty document over somebody's other answers. The telemetry switch is the one value stated as nothing:
    // the person did not move it, so the client has no answer of theirs to send and the deployment keeps what it holds.
    await expect.poll(() => stated).toHaveLength(1);
    expect(JSON.parse(stated[0] ?? '')).toStrictEqual({
        telemetryEnabled: null,
        theme: 'dark',
        openMailInTabs: false,
        markReadOnOpen: true,
        expandWholeThread: false,
        messageView: 'reduced',
        aiFiltersShown: true,
        notificationSeconds: 5,
        listMode: 'messages',
    });
});

// The two things a modal screen owes a keyboard, and the two only a browser can answer: `dialog.showModal` is what
// puts the page behind it out of reach and closes it on Escape, and jsdom implements neither the top layer nor the
// key. What a unit test can prove about this screen is what the component does — it is proven there — and what is
// left is that the platform does the rest, which is asked here rather than reimplemented anywhere.
test('keeps the keyboard inside the settings screen while it is open', async ({ page }) => {
    await openSignedIn(page);
    await openSettings(page);

    const panel = page.getByRole('dialog', { name: 'Settings' });

    await expect(panel.locator(':focus')).toHaveCount(1);

    // Far enough round to have left a screen that merely looked modal: the panel carries fewer controls than this.
    for (let step = 0; step < 12; step += 1) {
        await page.keyboard.press('Tab');
    }

    await expect(panel.locator(':focus')).toHaveCount(1);
});

test('closes the settings screen on Escape, handing focus back to the row that opened it', async ({ page }) => {
    await openSignedIn(page);
    await openSettings(page);

    await page.keyboard.press('Escape');

    await expect(page.getByRole('dialog', { name: 'Settings' })).toBeHidden();
    await expect(page.getByRole('button', { name: 'Settings', exact: true })).toBeFocused();
});

// The settings surface takes the whole window at every composition, and the one assertion only a browser can make
// about that: jsdom computes no geometry, so what a unit test can prove is which controls the surface carries, and
// what is left is the size it is drawn at.
test('draws the settings surface as the whole window in a wide window too, its sections down the side', async ({
    page,
}) => {
    await page.setViewportSize(wideWindow);
    await openSignedIn(page);
    await openSettings(page);
    await settled(page);

    const settings = page.getByRole('dialog', { name: 'Settings' });
    const panel = await settings.boundingBox();
    const navigation = await settings.getByRole('navigation', { name: 'Settings sections' }).boundingBox();

    expect(panel?.width).toBe(wideWindow.width);
    expect(panel?.height).toBe(wideWindow.height);

    // The navigation's own width, 18.0625rem at the root size this suite runs under, standing at the left edge.
    expect(navigation?.x).toBe(0);
    expect(navigation?.width).toBeCloseTo(289, 0);
});

test('draws the settings surface as the whole screen in a single-pane window', async ({ page }) => {
    await page.setViewportSize(narrowWindow);
    await openSignedIn(page);
    await openSettings(page);
    await settled(page);

    const panel = await page.getByRole('dialog', { name: 'Settings' }).boundingBox();

    expect(panel?.width).toBe(narrowWindow.width);
    expect(panel?.height).toBe(narrowWindow.height);
});

// Two sections as a person uses them: a value changed in each, the screen that value governs redrawn at once, and the
// choice read back from the deployment after the page is loaded afresh. What either section draws in answer to the value
// it was handed is the unit suite's; what is asked here is whether a *different* surface follows it.
test('corrects the name in the Profile section, and the account menu carries it at once and after a reload', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page);
    await openSettings(page);

    const name = page.getByRole('dialog', { name: 'Settings' }).getByRole('textbox', { name: 'Full name' });

    await name.fill('Iris Marlow-Vane');
    await name.press('Enter');
    await expect
        .poll(() => deployment.requests('POST', '/display-name'))
        .toMatchObject([{ body: { displayName: 'Iris Marlow-Vane' } }]);

    // Leaving the screen puts back the menu it was opened from, which is where the person's name is drawn. The closed
    // status menu carries the name too, so the one read is the one the rail's navigation holds.
    const spaces = page.getByRole('navigation', { name: 'Spaces' });

    await page.keyboard.press('Escape');
    await expect(spaces.getByText('Iris Marlow-Vane', { exact: true })).toBeVisible();

    await page.reload();
    await openAccountMenu(page);

    await expect(spaces.getByText('Iris Marlow-Vane', { exact: true })).toBeVisible();
});

test('takes the AI filters out of the folder column from the Application section, and keeps them out after a reload', async ({
    page,
    deployment,
}) => {
    await openSignedIn(page, '/#/mail');

    const filters = page.getByRole('region', { name: 'AI filters' });

    await expect(filters).toBeVisible();

    await openApplicationSettings(page);
    // The switch's input is a clipped pixel its own row covers, as the theme segments' are, so the press lands on the
    // row a pointer actually reaches.
    await page.getByRole('dialog', { name: 'Settings' }).getByText('Show the AI filters in the folder tree').click();
    await expect
        .poll(() => deployment.requests('POST', '/preferences'))
        .toMatchObject([{ body: { aiFiltersShown: false } }]);

    await page.keyboard.press('Escape');
    await expect(page.getByRole('dialog', { name: 'Settings' })).toBeHidden();
    await expect(filters).toHaveCount(0);

    await page.reload();

    // Waited for rather than assumed: the column is what the section would be drawn in, so its absence is only an
    // answer once the column itself is on the screen.
    await expect(page.getByRole('tree', { name: 'Mailboxes and folders' })).toBeVisible();
    await expect(filters).toHaveCount(0);
});
