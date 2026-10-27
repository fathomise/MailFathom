// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext } from 'react';
import { openUrl } from '@tauri-apps/plugin-opener';

// Following a link leaves the application rather than navigating it, and that is the one place the two heads genuinely
// differ: a web page opens a new browsing context, while a WebView that navigates to a sender's page has replaced the
// application with it. ADR 0024 settles the shape and ADR 0023 is where it came from — the application depends on one
// operation, which implementation satisfies it is decided once at the composition root, and no screen, component, or
// hook learns which head it is running on.
//
// So there is no platform branch below a screen. There is one branch, here, taken once, on whether a shell offered the
// command at all.
//
// The question asked before a link that leaves the application opens is not a head's, so it is not here either:
// `linkQuestion/LinkQuestion.tsx` supplies this same operation again inside the frame, around whichever one this module
// resolved, and every surface below it reaches the question by reaching the opener.

/**
 * A link somebody pressed, as the surface that drew it read it at the press.
 *
 * Only `target` is needed to open it. The rest is what the question in front of opening says about it — whether a
 * picture was pressed rather than words, what the words said, and what the surface already warned — so that the one
 * place that asks has what every surface knows without any of them drawing a question of its own.
 */
export interface FollowedLink {
    readonly target: string;
    readonly picture: boolean;

    /** The words the link showed, or `null` where it showed none a person could have read as a place. */
    readonly words: string | null;

    /** The warnings the surface already drew beside the link, each a sentence, which the question repeats. */
    readonly warnings: readonly string[];
}

/** How the application asks for a link to be opened out of itself. */
export type OpenLink = (link: FollowedLink) => Promise<void>;

export const LinkOpenerContext = createContext<OpenLink | null>(null);

export function useLinkOpener(): OpenLink {
    const opener = useContext(LinkOpenerContext);

    if (opener === null) {
        throw new Error('A component asked to open a link outside the LinkOpenerContext that main.tsx supplies.');
    }

    return opener;
}

/** Resolves the one operation for the head this bundle is running in, which is the whole of the composition. */
export function linkOpenerForThisApplication(): OpenLink {
    return shellOffersOpening() ? openThroughTheShell : openInANewBrowsingContext;
}

// The desktop shell announces itself by putting its own bridge on the global object before the bundle runs, so what is
// asked here is whether a shell is present rather than which operating system is underneath. `Object.hasOwn` reads it
// without asserting a type the DOM does not declare.
function shellOffersOpening(): boolean {
    return Object.hasOwn(globalThis, '__TAURI_INTERNALS__');
}

function openThroughTheShell({ target }: FollowedLink): Promise<void> {
    return openUrl(target);
}

function openInANewBrowsingContext({ target }: FollowedLink): Promise<void> {
    // `noopener` is what keeps the opened page from reaching back into the application through `window.opener`, and
    // `noreferrer` is what keeps this deployment's own address from travelling to whatever the sender linked to.
    //
    // The answer is discarded rather than checked, because asking for `noopener` is exactly what makes `window.open`
    // answer with nothing whether it opened a context or refused to. There is therefore no failure to report here: a
    // reader following a link they clicked is a gesture no browser blocks, and reading the null as a refusal would put
    // a warning under every link that worked.
    window.open(target, '_blank', 'noopener,noreferrer');

    return Promise.resolve();
}
