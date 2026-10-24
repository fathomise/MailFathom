// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useSyncExternalStore } from 'react';
import { startWatching, stopWatching } from './developerRecord';

// Whether developer mode is on, whether its surface is open, and the window it stands in where it has one.
//
// **Switching it on is what starts the record**, and opening the surface only shows it: the unlock begins watching at
// once, so what happens while the surface is closed is kept, and closing the surface or its window lets nothing go.
//
// **Where a mouse drives the client it opens in a window of its own**, on the desktop head and in a browser alike, so it
// can stand beside the client rather than over it. Where a finger does it is a surface inside the page, because a
// tablet or a phone has no second window to put beside the first. The window is opened from the press itself, which is
// what a browser asks of a page before it lets one open, and a head that refuses it gets the surface inside the page.
//
// Both live in this module and nowhere else, which is what makes the unlock last exactly as long as the page: it is
// never written to a store, so a reload forgets it, and the record and the button it reveals go with it. The surface
// is a developer's tool rather than anything a reader is offered, and somebody who stumbled on it once is not shown it
// for good.

/** How many presses on the version line unlock the surface. */
export const pressesToUnlock = 5;

/** How long a pause between two presses may be before the count starts again, in milliseconds. */
export const longestPauseBetweenPresses = 1_500;

interface DeveloperAccess {
    readonly unlocked: boolean;
    readonly open: boolean;

    /** The window the surface stands in, or `null` where it stands inside the page. */
    readonly window: Window | null;
}

/** The name the window is opened under, so a second press while one stands finds that one rather than another. */
export const developerWindowName = 'mailfathom-developer-information';

const developerWindowFeatures = 'popup,width=1280,height=860';

let access: DeveloperAccess = { unlocked: false, open: false, window: null };
let opener: HTMLElement | null = null;
let presses = 0;
let lastPressAt = 0;
const listeners = new Set<() => void>();

function revise(next: DeveloperAccess): void {
    access = next;

    for (const listener of listeners) {
        listener();
    }
}

function subscribe(listener: () => void): () => void {
    listeners.add(listener);

    return () => {
        listeners.delete(listener);
    };
}

function read(): DeveloperAccess {
    return access;
}

/** Whether the surface is unlocked and whether it is open, read again whenever either changes. */
export function useDeveloperAccess(): DeveloperAccess {
    return useSyncExternalStore(subscribe, read);
}

/** Counts one press on the version line, and switches developer mode on at the last of a quick run of them. */
export function pressVersionLine(): void {
    const at = Date.now();

    presses = at - lastPressAt > longestPauseBetweenPresses ? 1 : presses + 1;
    lastPressAt = at;

    if (presses >= pressesToUnlock && !access.unlocked) {
        startWatching();
        revise({ ...access, unlocked: true });
    }
}

/**
 * Opens the surface where it is closed and closes it where it is open.
 *
 * @param inAWindow Whether to open it in a window of its own, which is what a mouse-driven client is given.
 */
export function toggleDeveloperInformation(inAWindow: boolean): void {
    if (access.open) {
        closeDeveloperInformation();

        return;
    }

    opener = document.activeElement instanceof HTMLElement ? document.activeElement : null;

    const opened = inAWindow ? window.open('', developerWindowName, developerWindowFeatures) : null;

    // The window closing by its own control is the surface closing, and the client leaving takes the window with it,
    // since nothing would be drawing into it any more.
    opened?.addEventListener('pagehide', closeDeveloperInformation, { once: true });
    window.addEventListener('pagehide', closeDeveloperInformation, { once: true });

    revise({ ...access, open: true, window: opened });
}

/**
 * Gives the window the surface stands in what it needs to look like the client: the client's stylesheets, its title,
 * and the language and theme the client is in. The stylesheets are copied by address rather than by node, because a
 * blank document resolves nothing against the client's own.
 */
export function dressDeveloperWindow(into: Window, title: string, locale: string, theme: string): void {
    const opened = into.document;
    const sheets = document.querySelectorAll<HTMLLinkElement | HTMLStyleElement>('link[rel="stylesheet"], style');

    opened.documentElement.lang = locale;
    opened.documentElement.dataset['theme'] = theme;
    opened.head.replaceChildren(
        ...Array.from(sheets, (sheet) => {
            if (!(sheet instanceof HTMLLinkElement)) {
                return opened.importNode(sheet, true);
            }

            const linked = opened.createElement('link');

            linked.rel = 'stylesheet';
            linked.href = sheet.href;

            return linked;
        }),
    );

    // After the stylesheets rather than before, because replacing what the head holds would take the title with it.
    opened.title = title;
}

/** Closes the surface, and the window it stood in. */
export function closeDeveloperInformation(): void {
    if (!access.open) {
        return;
    }

    const standing = access.window;

    revise({ ...access, open: false, window: null });
    window.removeEventListener('pagehide', closeDeveloperInformation);
    standing?.removeEventListener('pagehide', closeDeveloperInformation);
    standing?.close();
}

/** Puts focus back on the control that opened the surface, where that control is still on the page. */
export function returnFocusToOpener(): void {
    const returningTo = opener;

    opener = null;

    if (returningTo?.isConnected === true) {
        returningTo.focus();
    }
}

/** Forgets the unlock and the record, which is what a reload does and what a test asks for. */
export function lockDeveloperInformation(): void {
    presses = 0;
    lastPressAt = 0;
    opener = null;
    closeDeveloperInformation();
    stopWatching();
    revise({ unlocked: false, open: false, window: null });
}
