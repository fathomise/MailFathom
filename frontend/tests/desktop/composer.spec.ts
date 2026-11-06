// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { expect, test } from '@playwright/test';

import * as clipboard from '../fixtures/clipboard';
import * as deployment from '../fixtures/deployment';
import { openDesktopHead, type DesktopHead } from './head';

// What is pasted into the composer, asked of the real WebView. The browser suite pastes in Chromium; what it cannot say
// is whether WebKitGTK passes a file a paste event carries to the composer, takes the insertion, and draws the picture
// from the object URL the composer issued — any of which failing leaves an empty box where the picture should be and a
// message sent without it — nor whether a markup document parsed by this engine runs nothing and fetches nothing,
// which ADR 0047 rests on each engine a head runs on. How a real clipboard hands either to the WebView is not asked
// here, for the reason the next paragraph gives. The shell loads its client from the corpus development server, which
// sends no content security policy, so the policy a published shell applies is not asked here.
//
// The paste is a synthetic one carrying a `DataTransfer` the script built, because WebDriver reaches no system
// clipboard. That is the event the composer reads either way: what is under test is the engine's file, its parser,
// its editing command, and its drawing of a `blob:` address, never how the operating system's clipboard got there.

test.describe.configure({ timeout: 180_000 });

// A picture of one pixel, whole, so the engine has something to decode and a width to report once it has drawn it.
const onePixelPng = 'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==';

const words = '[role="textbox"][aria-label="Message"]';

/** Signs in, opens a new message, and puts the caret in its words. */
async function openTheComposer(head: DesktopHead): Promise<void> {
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

    await head.click(words);
}

/** Raises a paste or a drop on the words carrying exactly the flavours named, as a script builds one. */
function deliver(kind: 'paste' | 'drop', flavours: Readonly<Record<string, string>>): string {
    return `(() => {
        const region = document.querySelector('${words}');
        const carried = new DataTransfer();
        const box = region.getBoundingClientRect();

        for (const [flavour, value] of Object.entries(${JSON.stringify(flavours)})) {
            carried.setData(flavour, value);
        }

        region.focus();
        region.dispatchEvent(
            '${kind}' === 'paste'
                ? new ClipboardEvent('paste', { clipboardData: carried, bubbles: true, cancelable: true })
                : new DragEvent('drop', {
                      dataTransfer: carried,
                      clientX: box.left + box.width / 2,
                      clientY: box.bottom - 4,
                      bubbles: true,
                      cancelable: true,
                  }),
        );

        return true;
    })()`;
}

test('takes a pasted picture into the composer and draws it from the address the composer issued', async () => {
    const head = await openDesktopHead({});

    try {
        await openTheComposer(head);
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

// Every address the hostile document names is pointed at the development server the shell loaded its client from, so a
// request for one — answered or refused there — is one the engine's own resource timing records. A request to a name
// that does not resolve would leave no entry at all, and the check would pass whatever the engine did.
test('reads a hostile document pasted and dropped, and a spreadsheet range, running nothing and fetching nothing', async () => {
    const head = await openDesktopHead({});

    try {
        await openTheComposer(head);

        const hostile = await head.evaluate<string>(
            `${JSON.stringify(clipboard.hostileDocument)}.replace(/https:\\/\\/([a-z-]+)\\.example\\.invalid/gu, location.origin + '/clipboard-fixture-$1')`,
        );

        await head.evaluate(deliver('paste', { 'text/html': hostile }));
        await head.evaluate(deliver('drop', { 'text/html': hostile }));
        await head.settled(`document.querySelectorAll('${words} td').length >= 4 ? true : null`);

        // A request the paste started has had a whole round trip to the same server to finish in by the time this one
        // has, so its entry is recorded before it is read.
        const read = await head.evaluate(`(async () => {
            await fetch(location.origin + '/clipboard-fixture-settled').catch(() => undefined);

            const region = document.querySelector('${words}');

            return {
                ran: globalThis['${clipboard.ranMarker}'] ?? null,
                fetched: performance
                    .getEntriesByType('resource')
                    .map((entry) => entry.name)
                    .filter((name) => name.includes('/clipboard-fixture-') && !name.endsWith('-settled')),
                foreign: region.querySelectorAll('script, style, link, base, meta, iframe, object, embed, svg, video, picture, source, img').length,
                handlers: [...region.querySelectorAll('*')].some((element) =>
                    [...element.attributes].some((attribute) => attribute.name.startsWith('on')),
                ),
                words: region.textContent.includes('Quarterly notes'),
            };
        })()`);

        expect(read).toEqual({ ran: null, fetched: [], foreign: 0, handlers: false, words: true });

        await head.evaluate(deliver('paste', { 'text/html': clipboard.excelRange, 'text/plain': clipboard.rangeText }));

        // Settled once the range's words are anywhere in the region, so a range that went in as something other than a
        // table says where it went rather than timing out.
        const range = await head.settled<{ readonly headers: readonly string[]; readonly within: string }>(`(() => {
            const region = document.querySelector('${words}');
            const word = [...region.querySelectorAll('*')].findLast((element) => element.textContent.includes('North yard') && element.children.length === 0);

            return word === undefined
                ? null
                : {
                      headers: [...region.querySelectorAll('th')].map((cell) => cell.textContent.trim()),
                      within: [...region.children].map((child) => child.tagName.toLowerCase()).join(' ') + ' / ' + word.closest('td, th, p, div')?.tagName.toLowerCase(),
                  };
        })()`);

        expect(range.headers, `the region holds ${range.within}`).toEqual(['Region', 'Units', 'Change']);
    } finally {
        await head.close();
    }
});
