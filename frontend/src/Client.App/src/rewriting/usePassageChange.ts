// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState } from 'react';
import { failureSaid } from '../composer/draftingRead';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useToasts } from '../toasts/useToasts';
import type { PassageRewriter } from './passageRewriting';
import { rewriteRequestFor, type TouchedPassage } from './passages';
import { presetAfter, presetShapes, type RewritePreset } from './rewritePresets';

// A rewrite of part of a draft, from the press that asks for it to the change somebody keeps or undoes. Both surfaces
// that rewrite a passage hold one, and what differs between them is only where the text lives — a string in the
// Agent's draft, a region of the document in the composer — so the asking, the answering and every sentence said about
// either outcome are here once.
//
// **Nothing is laid over the draft by this hook.** It answers what came back and the surface puts it where its text is,
// because the composer's words are the document's rather than React's and a rewrite laid over them from state would
// throw away their formatting.

/** How a passage is to be rewritten: one of the bar's presets, or what the person typed into its field. */
export type RewriteHow = { readonly preset: RewritePreset } | { readonly said: string };

/** A rewrite in hand: being written, or written and waiting to be kept or undone. */
export interface PassageChange {
    readonly passages: readonly TouchedPassage[];
    readonly how: RewriteHow;

    /** Whether this is another version of a rewrite that was already shown. */
    readonly again: boolean;

    /** Each passage as it came back, in order, or `null` while the deployment is still writing them. */
    readonly rewritten: readonly string[] | null;
}

const busyCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'rewrite.busy.other',
    one: 'rewrite.busy.one',
    two: 'rewrite.busy.other',
    few: 'rewrite.busy.few',
    many: 'rewrite.busy.many',
    other: 'rewrite.busy.other',
};

function instructionOf(how: RewriteHow): string {
    return 'preset' in how ? presetShapes[how.preset].instruction : how.said;
}

/**
 * Holds one rewrite of part of a draft at a time.
 *
 * @param rewriter How the deployment is asked, or `null` where it rewrites nothing and nothing is ever asked.
 * @returns The change in hand, the words a surface says about it, and the acts on it.
 */
export function usePassageChange(rewriter: PassageRewriter | null) {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const [change, setChange] = useState<PassageChange | null>(null);
    const asked = useRef(0);
    const present = useRef(true);

    useEffect(() => {
        present.current = true;

        return () => {
            present.current = false;
        };
    }, []);

    /** What a rewrite is called where it is named: its preset, or the words somebody typed, quoted. */
    function howSaid(how: RewriteHow, again: boolean): string {
        const said =
            'preset' in how
                ? translate(presetShapes[how.preset].said)
                : translate('rewrite.quoted', { words: how.said });

        return again ? translate('rewrite.another', { how: said }) : said;
    }

    /** The line standing in place of the draft's controls while a change is in hand. */
    function noteOf(standing: PassageChange): string {
        const how = howSaid(standing.how, standing.again);

        return standing.rewritten === null
            ? translate(busyCounted[new Intl.PluralRules(locale).select(standing.passages.length)], {
                  count: new Intl.NumberFormat(locale).format(standing.passages.length),
                  how,
              })
            : translate('rewrite.review', { how });
    }

    function refused(said: MessageKey, kind: 'warning' | 'error'): void {
        toasts.raise({ kind, title: translate('rewrite.notRewrittenTitle'), body: translate(said) });
    }

    /**
     * Asks for the passages to be rewritten.
     *
     * @param paragraphs The draft's text, a paragraph each, as the passages were read from it.
     * @param passages The whole sentences the selection touched.
     * @param how How to rewrite them.
     * @param again Whether this is another version of the change just shown.
     * @returns Each passage rewritten, in order, or `null` where nothing is to be laid over the draft — a refusal, a
     *   failure, a rewrite that changed nothing, or an answer to an ask somebody has since moved past. Whatever the
     *   reason, the person has already been told it.
     */
    async function rewrite(
        paragraphs: readonly string[],
        passages: readonly TouchedPassage[],
        how: RewriteHow,
        again = false,
    ): Promise<readonly string[] | null> {
        if (rewriter === null) {
            return null;
        }

        const request = rewriteRequestFor(paragraphs, passages, instructionOf(how));

        if (request === null) {
            refused('rewrite.tooLong', 'warning');

            return null;
        }

        const ask = ++asked.current;

        setChange({ passages, how, again, rewritten: null });

        const answer = await rewriter(request);

        // An answer for an ask somebody undid, or for a surface that has gone, is laid over nothing.
        if (!present.current || ask !== asked.current) {
            return null;
        }

        if (answer.outcome === 'failed') {
            setChange(null);
            refused(failureSaid[answer.failure.reason], 'error');

            return null;
        }

        if (answer.value.outcome !== 'rewritten') {
            setChange(null);
            refused(
                answer.value.outcome === 'allowanceSpent' ? 'compose.draftAllowanceSpent' : 'rewrite.notRewritten',
                'warning',
            );

            return null;
        }

        const rewritten = answer.value.passages;

        if (rewritten.every((words, at) => words.trim() === passages[at]?.words.trim())) {
            setChange(null);
            toasts.raise({ kind: 'neutral', title: howSaid(how, again), body: translate('rewrite.nothingToChange') });

            return null;
        }

        setChange({ passages, how, again, rewritten });

        return rewritten;
    }

    /** Lets go of the change in hand, and of any answer still on its way for it. */
    function settle(): void {
        asked.current++;
        setChange(null);
    }

    /**
     * Says that a change was kept, with the way back to the text before it.
     *
     * @param kept The change kept.
     * @param inMessage Whether it was kept in the composer's message rather than in the Agent's draft.
     * @param undo Puts the text back as it was before the change.
     */
    function sayKept(kept: PassageChange, inMessage: boolean, undo: () => void): void {
        const how = howSaid(kept.how, kept.again);

        toasts.raise({
            kind: 'success',
            title: translate('rewrite.keptTitle'),
            body: translate(inMessage ? 'rewrite.keptInMessage' : 'rewrite.keptInDraft', { how }),
            action: { label: translate('rewrite.undo'), take: undo },
        });
    }

    /** How *Try again* asks: the next preset after a preset, and the same words again after words somebody typed. */
    function howAgain(standing: PassageChange): RewriteHow {
        return 'preset' in standing.how ? { preset: presetAfter(standing.how.preset) } : standing.how;
    }

    return { change, noteOf, rewrite, settle, sayKept, howAgain };
}
