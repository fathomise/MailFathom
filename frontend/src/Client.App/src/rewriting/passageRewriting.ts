// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createContext, useContext, type RefObject } from 'react';
import type { ClientResult, DraftPassageRewriteRequest, DraftPassageRewriting } from '@mailfathom/client-backend';

// How a surface holding a draft reaches the deployment to have part of it rewritten. It is a context because the two
// surfaces that ask — the Agent's draft, an answer block several components below the frame, and the composer — sit in
// different subtrees, and what they need is one operation rather than a session and a transport threaded to each.
//
// The value is `null` wherever the deployment rewrites nothing or the credential may not ask it to, which is the whole
// of how a surface decides whether the AI button is drawn: a button promising a rewrite over a deployment that writes
// none fails a person at the one moment they trusted it.

/** Asks the deployment to rewrite the parts of a draft a request marks. */
export type PassageRewriter = (request: DraftPassageRewriteRequest) => Promise<ClientResult<DraftPassageRewriting>>;

export const PassageRewritingContext = createContext<PassageRewriter | null>(null);

/** The operation a draft's selected part is rewritten through, or `null` where this deployment rewrites none. */
export function usePassageRewriter(): PassageRewriter | null {
    return useContext(PassageRewritingContext);
}

/**
 * What the rewrite bar docks on under a finger: the Agent's field, which the Agent space owns and the draft inside one
 * of its answers cannot otherwise reach. Docked across that field's width, the bar never covers the rail or the history.
 */
export const RewriteDockContext = createContext<RefObject<HTMLElement | null> | null>(null);

export function useRewriteDock(): RefObject<HTMLElement | null> | null {
    return useContext(RewriteDockContext);
}
