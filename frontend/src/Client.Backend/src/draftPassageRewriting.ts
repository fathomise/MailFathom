// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// Part of a draft written again, where somebody selected the sentences they did not like rather than asking for the
// whole message once more. It is the drafting route's second act and stands beside it: the same deployment writes it,
// under the same switch, grants and allowance, so whether a screen may offer it is the drafting read's answer.
//
// **The draft is sent as its text with the parts to rewrite marked by where they stand in it**, rather than as the
// parts alone, because a sentence is rewritten to read between the ones around it. The offsets are UTF-16 code units —
// what a JavaScript string counts and what the service's string counts — so nothing converts them on either side.
//
// **Three answers rather than a value and a failure**, as the drafting route's: the parts rewritten, a deployment that
// rewrote nothing — which is the draft as it was — and a spent allowance, which is the one a screen says aloud.
//
// The draft is the person's own text or one written out of their mail, so none of it is logged, kept, or put in a span
// attribute; the span names the route alone.

/** The route part of a draft is rewritten at, relative to the client prefix. */
export const draftPassageRewritingRoute = '/replies/drafting/passages';

/** One part of the draft to rewrite, as where it starts in the text and how long it is. */
export interface DraftPassage {
    readonly start: number;
    readonly length: number;
}

/** What one rewriting asks for: the draft's text, the parts of it to rewrite, and how. */
export interface DraftPassageRewriteRequest {
    /** The draft as plain text, a paragraph to a line. */
    readonly text: string;

    /** The parts to rewrite, in the order they stand in the text and none overlapping another. */
    readonly passages: readonly DraftPassage[];

    /** How to rewrite them, in words a person could have typed. */
    readonly instruction: string;
}

/** What a rewriting answered with: the parts as written again, or one of the two reasons there are none. */
export type DraftPassageRewriting =
    | {
          readonly outcome: 'rewritten';

          /** Each part as it was written again, in the order the request named them. */
          readonly passages: readonly string[];
      }
    | { readonly outcome: 'notRewritten' }
    | { readonly outcome: 'allowanceSpent' };

/**
 * The greatest text the service reads around the parts being rewritten.
 *
 * The client's own copy of the service's bound, so a screen sends the stretch of a long draft around the parts rather
 * than a request the deployment would refuse.
 */
export const longestRewrittenDraft = 8_000;

/** The most parts one rewriting takes, on the same terms as the text above. */
export const mostRewrittenPassages = 16;

/** The greatest instruction one rewriting takes, on the same terms as the text above. */
export const longestRewriteInstruction = 4_000;

// Each part comes back at most the length of the text it was written in, and the array around them is small.
const longestRewriteAnswer = 128 * 1024;

/**
 * Rewrites the parts of a draft a person selected.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param request The draft's text, the parts of it to rewrite, and how.
 * @returns The parts rewritten, the two shapes of having none, or why the answer never arrived.
 */
export function rewriteDraftPassages(
    session: ClientSession,
    transport: MailFathomTransport,
    request: DraftPassageRewriteRequest,
): Promise<ClientResult<DraftPassageRewriting>> {
    return spanned(`POST ${draftPassageRewritingRoute}`, async () => {
        const response = await send(transport, {
            method: 'POST',
            changesNothing: true,
            path: routeFor(session, draftPassageRewritingRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({
                text: request.text,
                passages: request.passages.map(({ start, length }) => ({ start, length })),
                instruction: request.instruction.trim().slice(0, longestRewriteInstruction),
            }),
            longestAnswer: longestRewriteAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        // The one non-success status a screen draws rather than reports, as on the drafting route: the operator has
        // spent the allowance, and the person pressing the control is owed being told so.
        if (response.status === 429) {
            return read({ outcome: 'allowanceSpent' });
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const rewriting = parseRewriting(response.body, request.passages.length);

        return rewriting === null ? failed('unreadable', response.status) : read(rewriting);
    });
}

function parseRewriting(body: string, asked: number): DraftPassageRewriting | null {
    let record: Record<string, unknown> | null;

    try {
        record = asRecord(JSON.parse(body));
    } catch {
        return null;
    }

    if (record === null) {
        return null;
    }

    if (record['rewritten'] !== true) {
        return record['rewritten'] === false ? { outcome: 'notRewritten' } : null;
    }

    // One part back for every part asked for, each of them words: anything else is not the rewrite that was asked for,
    // and laying it over the draft would put one part's words where another stood.
    const passages = record['passages'];
    if (!Array.isArray(passages) || passages.length !== asked) {
        return null;
    }

    const written: string[] = [];
    for (const passage of passages) {
        if (typeof passage !== 'string' || passage.trim().length === 0 || passage.length > longestRewrittenDraft) {
            return null;
        }

        written.push(passage);
    }

    return { outcome: 'rewritten', passages: written };
}
