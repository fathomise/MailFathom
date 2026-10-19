// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, type Page } from '@playwright/test';

import { actingGrants, openSignedIn, settled, test } from './client.harness';

// The client's own motion, resolved against a real reduced-motion preference and a computed stylesheet.

/** The notification centre opened, which is the panel the client's own motion is stated on. */
async function openNotifications(page: Page): Promise<void> {
    await page.getByRole('button', { name: 'Notifications' }).click();

    await expect(page.getByRole('dialog', { name: 'Notifications' })).toBeVisible();
}

// What the panel's own transition runs at, read off the open centre. Written as a self-contained expression rather
// than as a closure taking the element, for the reason the two document-level ones above give: this suite compiles
// without a DOM declaration on purpose, so a function naming `getComputedStyle` would be the one thing that changes.
// The panel is named by the same accessible name the query above finds it by, so the style read and the visibility
// assertion are about one element rather than two.
const theOpenPanelsTransitionDuration = `
    getComputedStyle(document.querySelector('dialog[open][aria-label="Notifications"]')).transitionDuration
`;

/**
 * Every duration the panel's own transition runs at, deduplicated.
 *
 * A transition over four properties reports four durations, so what is read is the set of them: a rule that removed
 * the motion from three of them and left the fourth would be a screen that still moves.
 */
async function motionDurations(page: Page): Promise<string[]> {
    const stated = await page.evaluate(theOpenPanelsTransitionDuration);

    // A read answering with anything but the string a computed style is has not measured the panel at all, and an
    // empty list stood in for it would satisfy *both* assertions below — which is how the first version of this
    // helper reported the moving case green against a page it had never read.
    expect(typeof stated).toBe('string');

    return [...new Set(String(stated).split(', '))];
}

// Only a browser resolves a media query against a real preference and computes a duration from a stylesheet, so this
// is one of the claims `pnpm test` structurally cannot make: jsdom computes no styles and would pass for a client
// carrying no such rule at all.
test('moves the notification centre onto the screen over a duration of its own', async ({ page }) => {
    await openSignedIn(page);
    await openNotifications(page);

    expect(await motionDurations(page)).not.toEqual(['0s']);
});

test('takes that motion away for a reader who asked for less of it, rather than shortening it', async ({
    browser,
    deployment,
}) => {
    const context = await browser.newContext({ reducedMotion: 'reduce' });

    await deployment.serve(context);

    const page = await context.newPage();

    await openSignedIn(page);
    await openNotifications(page);

    // Removed rather than shortened, which is the accessibility obligation: the panel still arrives where it belongs,
    // and what is gone is the travel. A duration merely made small would read as motion to somebody who asked for none.
    expect(await motionDurations(page)).toEqual(['0s']);
});

// A surface drawn in the document rather than in the top layer is gone the moment its value is, so leaving is what the
// client has to hold it on the screen for. The page's animations are slowed while the menu leaves, which turns the
// departure from something over in a fifth of a second into something a check can find still running — without it,
// whether the menu is caught leaving would depend on how loaded the machine is — and are put back to speed once it has
// been caught, so nothing waits the slowed departure out.
//
// Read in the page, because what is asserted is the one fact no role query reaches: that the menu still in the
// document sits inside an inert region, so a press on something already going reaches nothing.
const theMenuIsHeldInert = `(() => {
    const menu = document.querySelector('[role="menu"]');
    return menu !== null && menu.closest('[inert]') !== null;
})()`;

test('plays a menu’s departure before taking it off the screen', async ({ page, deployment }) => {
    deployment.grant(...actingGrants);
    await openSignedIn(page, '/#/mail');

    await page.getByRole('listbox', { name: 'Messages' }).getByRole('option').first().click({ button: 'right' });
    await expect(page.getByRole('menu')).toBeVisible();

    const animations = await page.context().newCDPSession(page);
    await animations.send('Animation.enable');
    await animations.send('Animation.setPlaybackRate', { playbackRate: 0.05 });

    // Left by the keyboard's own way out, which the menu answers from wherever focus stands inside it.
    await page.getByRole('menuitem').first().focus();
    await page.keyboard.press('Escape');

    await expect.poll(() => page.evaluate(theMenuIsHeldInert)).toBe(true);

    await animations.send('Animation.setPlaybackRate', { playbackRate: 1 });
    await expect(page.getByRole('menu', { includeHidden: true })).toHaveCount(0);
});

// Hiding a conversation's history folds it away before it goes, so the reader sees where the messages went. Slowed for
// the same reason as the menu above, and read in the page for the same reason: the messages still drawn while they
// fold sit inside an inert region, which no role query reaches.
const foldingMessages = `document.querySelectorAll('li[inert]').length`;

test('folds the rest of a conversation away before taking it off the screen', async ({ page }) => {
    await openSignedIn(page, '/#/mail');
    await page
        .getByRole('listbox', { name: 'Messages' })
        .getByRole('option', { name: /The racking quote/u })
        .click();

    const conversation = page.getByRole('region', { name: 'Conversation' });
    const messages = conversation.getByRole('article', { name: /^Message from/u });

    await conversation.getByRole('button', { name: 'Show all the messages' }).click();
    await expect(messages).toHaveCount(5);

    const animations = await page.context().newCDPSession(page);
    await animations.send('Animation.enable');
    await animations.send('Animation.setPlaybackRate', { playbackRate: 0.05 });

    await conversation.getByRole('button', { name: 'Hide the other messages' }).click();

    await expect.poll(() => page.evaluate(foldingMessages)).toBe(4);

    await animations.send('Animation.setPlaybackRate', { playbackRate: 1 });
    await expect(messages).toHaveCount(1);
    await expect.poll(() => page.evaluate(foldingMessages)).toBe(0);
});

// The list's filters are a band in the flow of the column rather than a surface over it, so what moves is its height:
// it unfolds from nothing and folds back to nothing, and the list under it moves with it. Only a browser lays out a
// height, and only one interpolating to `auto` travels it, so this is the check that the band is not merely fading
// in place at its full size.
function bandHeight(band: string): string {
    return `document.getElementById(${JSON.stringify(band)})?.getBoundingClientRect().height ?? 0`;
}

test('unfolds the list’s filters from no height and folds them back before they go', async ({ page }) => {
    await openSignedIn(page, '/#/mail');

    const filters = page.getByRole('button', { name: /^Filters/u }).first();
    const band = (await filters.getAttribute('aria-controls')) ?? '';

    await filters.click();
    await expect(filters).toHaveAttribute('aria-expanded', 'true');
    await expect.poll(async () => Number(await page.evaluate(bandHeight(band)))).toBeGreaterThan(0);
    await settled(page);
    const full = Number(await page.evaluate(bandHeight(band)));

    await filters.click();
    await expect.poll(async () => Number(await page.evaluate(bandHeight(band)))).toBe(0);

    const animations = await page.context().newCDPSession(page);
    await animations.send('Animation.enable');
    await animations.send('Animation.setPlaybackRate', { playbackRate: 0.05 });

    await filters.click();
    await expect.poll(async () => Number(await page.evaluate(bandHeight(band)))).toBeGreaterThan(0);
    expect(Number(await page.evaluate(bandHeight(band)))).toBeLessThan(full);

    await animations.send('Animation.setPlaybackRate', { playbackRate: 1 });
    await expect.poll(async () => Number(await page.evaluate(bandHeight(band)))).toBe(full);

    await animations.send('Animation.setPlaybackRate', { playbackRate: 0.05 });
    await filters.click();
    await expect.poll(async () => Number(await page.evaluate(bandHeight(band)))).toBeLessThan(full);
    expect(Number(await page.evaluate(bandHeight(band)))).toBeGreaterThan(0);

    await animations.send('Animation.setPlaybackRate', { playbackRate: 1 });
    await expect.poll(async () => Number(await page.evaluate(bandHeight(band)))).toBe(0);
});
