// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { FollowedLink } from '../shellOperations/linkOpener';

/** What the question in front of a link says about it, once it has been decided that the link asks. */
export interface LinkAsked {
    readonly target: string;

    /** Where it goes, as the address names it, so a lookalike is read in the form a browser will go to. */
    readonly host: string;
    readonly picture: boolean;

    /** What the surface already warned about the link, repeated in the question. */
    readonly warnings: readonly string[];

    /**
     * The host the link's words name where it differs from the one it goes to, said only where nothing else warned.
     *
     * A judgement the link already carries outranks this one, which is a reading of the words alone; and a picture has
     * no words to read.
     */
    readonly wordsName: string | null;
}

const leavesTheApplication = /^https?:/i;

// An address written as the words of a link — `travel.example`, `https://travel.example/check-in` — and nothing else, so
// a sentence that happens to contain a dot names no host.
const addressInWords = /^(?:https?:\/\/)?([a-z0-9-]+(?:\.[a-z0-9-]+)+)(?:[/?#]\S*)?$/i;

/** Whether a host is a trusted domain, or a subdomain of one. A name that only borrows a trusted one's letters is neither. */
export function isTrustedHost(hostname: string, trustedDomains: readonly string[]): boolean {
    return trustedDomains.some((domain) => hostname === domain || hostname.endsWith(`.${domain}`));
}

/**
 * Decides whether opening a link asks first, and what the question says.
 *
 * @returns `null` where the link opens at once — it does not leave for the web, or its host is trusted — and what to ask
 * otherwise.
 */
export function askingFor(link: FollowedLink, trustedDomains: readonly string[]): LinkAsked | null {
    if (!leavesTheApplication.test(link.target)) {
        return null;
    }

    // An address no parser reads still asks, naming itself: it is a press the person made, and whatever the head does
    // with it is somewhere the person was not told about.
    const url = URL.canParse(link.target) ? new URL(link.target) : null;

    if (url !== null && isTrustedHost(url.hostname, trustedDomains)) {
        return null;
    }

    const host = url?.host ?? link.target;
    const named = link.picture || link.warnings.length > 0 ? null : namedBy(link.words);

    return {
        target: link.target,
        host,
        picture: link.picture,
        warnings: link.warnings,
        wordsName: named !== null && url !== null && bare(named) !== bare(url.hostname) ? named : null,
    };
}

function namedBy(words: string | null): string | null {
    const named = addressInWords.exec(words?.trim() ?? '');

    return named?.[1]?.toLowerCase() ?? null;
}

function bare(host: string): string {
    return host.replace(/^www\./, '');
}
