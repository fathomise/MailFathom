// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import {
    longestRewrittenDraft,
    mostRewrittenPassages,
    type DraftPassageRewriteRequest,
} from '@mailfathom/client-backend';

// What a selection in a draft comes to once it is rewritten: the whole sentences it touches, one stretch per paragraph.
// Both surfaces that rewrite part of a draft — the Agent's draft and the composer — read their text as paragraphs and
// hand them here, so the rule about what gets rewritten is one rule rather than one per surface.
//
// **Whole sentences**, because half a sentence rewritten comes back as a splice that no longer reads; the design says
// so in the bar itself. Where a sentence ends is the platform's to say — `Intl.Segmenter` knows the abbreviations,
// the closing quotes and the scripts a pattern written here would not.

/** A place in a draft, as the paragraph it is in and how far into that paragraph's text. */
export interface TextPoint {
    readonly paragraph: number;
    readonly at: number;
}

/** One stretch of whole sentences a selection touched, inside one paragraph. */
export interface TouchedPassage {
    readonly paragraph: number;
    readonly start: number;
    readonly end: number;
    readonly words: string;
}

/** Where a place in a draft's text falls, as the line it is on and how far into that line. */
export function linePoint(text: string, offset: number): TextPoint {
    const before = text.slice(0, offset);

    return { paragraph: before.split('\n').length - 1, at: offset - (before.lastIndexOf('\n') + 1) };
}

const sentences = new Intl.Segmenter(undefined, { granularity: 'sentence' });

/** The whole sentences of one paragraph, as where each starts and ends with the space around it left out. */
function sentencesOf(text: string): readonly { readonly start: number; readonly end: number }[] {
    return [...sentences.segment(text)]
        .map(({ index, segment }) => ({
            start: index + segment.length - segment.trimStart().length,
            end: index + segment.trimEnd().length,
        }))
        .filter(({ start, end }) => end > start);
}

/**
 * Reads a selection into the whole sentences it touches.
 *
 * @param paragraphs The draft's text, a paragraph each.
 * @param from Where the selection starts.
 * @param to Where it ends, at or after `from`.
 * @returns A stretch for each paragraph the selection touches a sentence of, in order — empty where it touches only
 *   the space between them.
 */
export function passagesTouched(
    paragraphs: readonly string[],
    from: TextPoint,
    to: TextPoint,
): readonly TouchedPassage[] {
    const touched: TouchedPassage[] = [];

    for (let paragraph = from.paragraph; paragraph <= to.paragraph; paragraph++) {
        const text = paragraphs[paragraph] ?? '';
        const lowest = paragraph === from.paragraph ? from.at : 0;
        const highest = paragraph === to.paragraph ? to.at : text.length;
        const hit = sentencesOf(text).filter(({ start, end }) => end > lowest && start < highest);
        const first = hit.at(0);
        const last = hit.at(-1);

        if (first !== undefined && last !== undefined) {
            touched.push({ paragraph, start: first.start, end: last.end, words: text.slice(first.start, last.end) });
        }
    }

    return touched;
}

/**
 * Puts the request one rewriting sends together: the draft's text around the stretches, and where each stands in it.
 *
 * A draft longer than the service reads is sent as the paragraphs around the stretches, as many as fit, nearest first,
 * because what a sentence is rewritten to read between is the text beside it rather than the opening of a long message.
 *
 * @param paragraphs The draft's text, a paragraph each.
 * @param passages The stretches to rewrite, in order.
 * @param instruction How to rewrite them.
 * @returns The request, or `null` where the stretches alone are more than one rewriting takes.
 */
export function rewriteRequestFor(
    paragraphs: readonly string[],
    passages: readonly TouchedPassage[],
    instruction: string,
): DraftPassageRewriteRequest | null {
    const first = passages.at(0);
    const last = passages.at(-1);

    if (first === undefined || last === undefined || passages.length > mostRewrittenPassages) {
        return null;
    }

    let lowest = first.paragraph;
    let highest = last.paragraph;
    const lengthOf = (low: number, high: number) =>
        paragraphs.slice(low, high + 1).reduce((total, text) => total + text.length + 1, -1);

    if (lengthOf(lowest, highest) > longestRewrittenDraft) {
        return null;
    }

    for (let grew = true; grew;) {
        grew = false;

        if (lowest > 0 && lengthOf(lowest - 1, highest) <= longestRewrittenDraft) {
            lowest--;
            grew = true;
        }

        if (highest < paragraphs.length - 1 && lengthOf(lowest, highest + 1) <= longestRewrittenDraft) {
            highest++;
            grew = true;
        }
    }

    const sent = paragraphs.slice(lowest, highest + 1);
    const startOf = (paragraph: number) => (paragraph === lowest ? 0 : lengthOf(lowest, paragraph - 1) + 1);

    return {
        text: sent.join('\n'),
        passages: passages.map(({ paragraph, start, end }) => ({
            start: startOf(paragraph) + start,
            length: end - start,
        })),
        instruction,
    };
}

/**
 * The draft's paragraphs with each stretch replaced by its rewrite.
 *
 * @param paragraphs The draft's text, a paragraph each, as it was when the stretches were read.
 * @param passages The stretches that were rewritten.
 * @param rewritten Each stretch as it came back, in the same order.
 * @returns The paragraphs as they read with the change kept.
 */
export function withPassagesRewritten(
    paragraphs: readonly string[],
    passages: readonly TouchedPassage[],
    rewritten: readonly string[],
): readonly string[] {
    const kept = [...paragraphs];

    // From the last to the first, so a stretch replaced does not move where the one before it stands.
    for (let at = passages.length - 1; at >= 0; at--) {
        const passage = passages[at];
        const words = rewritten[at];
        const text = passage === undefined ? undefined : kept[passage.paragraph];

        if (passage !== undefined && words !== undefined && text !== undefined) {
            kept[passage.paragraph] = text.slice(0, passage.start) + words + text.slice(passage.end);
        }
    }

    return kept;
}
