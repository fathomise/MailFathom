// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, test } from '@playwright/test';

import * as deployment from '../fixtures/deployment';
import { openDesktopHead } from './head';

// A picture pasted into the composer, asked of the real WebView. The browser suite pastes one in Chromium through the
// real clipboard; what it cannot say is whether WebKitGTK hands a pasted picture over as a file the composer can read,
// takes the insertion, and draws the picture from the object URL the composer issued — any of which failing leaves an
// empty box where the picture should be and a message sent without it. The shell loads its client from the corpus
// development server, which sends no content security policy, so the policy a published shell applies is not asked
// here.
//
// The paste is a synthetic one carrying a `DataTransfer` the script built, because WebDriver reaches no system
// clipboard. That is the event the composer reads either way: what is under test is the engine's file, its editing
// command, and its drawing of a `blob:` address, never how the operating system's clipboard got there.

test.describe.configure({ timeout: 180_000 });

// A picture of one pixel, whole, so the engine has something to decode and a width to report once it has drawn it.
const onePixelPng = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';

test('takes a pasted picture into the composer and draws it from the address the composer issued', async () => {
    const head = await openDesktopHead({});

    try {
        await head.fill('#sign-in-user-name', deployment.userName);
        await head.fill('#sign-in-password', deployment.password);
        await head.click('button[type="submit"]');

        await head.evaluate('(document.location.hash = "#/mail", true)');
        await head.settled(`(() => {
            const opening = [...document.querySelectorAll('button:not([inert] *)')].find(
                (button) => button.textContent.trim() === 'New message' || button.getAttribute('aria-label') === 'New message',
            );

            opening?.click();

            return opening === undefined ? null : true;
        })()`);

        const words = '[role="textbox"][aria-label="Message"]';

        await head.click(words);
        await head.evaluate(`(() => {
            const region = document.querySelector('${words}');
            const octets = Uint8Array.from(atob('${onePixelPng}'), (character) => character.charCodeAt(0));
            const carried = new DataTransfer();

            carried.items.add(new File([octets], 'pixel.png', { type: 'image/png' }));
            region.focus();
            region.dispatchEvent(new ClipboardEvent('paste', { clipboardData: carried, bubbles: true, cancelable: true }));

            return true;
        })()`);

        const drawn = await head.settled<{ readonly source: string; readonly width: number }>(`(() => {
            const picture = document.querySelector('${words} img');

            return picture === null || !picture.complete || picture.naturalWidth === 0
                ? null
                : { source: picture.getAttribute('src'), width: picture.naturalWidth };
        })()`);

        expect(drawn.source).toMatch(/^blob:/u);
        expect(drawn.width).toBe(1);
    } finally {
        await head.close();
    }
});
