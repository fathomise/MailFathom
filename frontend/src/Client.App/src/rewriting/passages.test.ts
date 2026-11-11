// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { longestRewrittenDraft } from '@mailfathom/client-backend';
import { describe, expect, it } from 'vitest';
import { passagesTouched, rewriteRequestFor, withPassagesRewritten } from './passages';

const draft = ['Thanks for the offer. We accept the price, but not the date. See you soon.', 'Best regards, Anna'];

describe('passagesTouched', () => {
    it('widens a selection inside one sentence to the whole of it', () => {
        const touched = passagesTouched(draft, { paragraph: 0, at: 30 }, { paragraph: 0, at: 36 });

        expect(touched).toStrictEqual([
            { paragraph: 0, start: 22, end: 60, words: 'We accept the price, but not the date.' },
        ]);
    });

    it('takes every sentence a selection crosses as one stretch', () => {
        const touched = passagesTouched(draft, { paragraph: 0, at: 5 }, { paragraph: 0, at: 25 });

        expect(touched.map(({ words }) => words)).toStrictEqual([
            'Thanks for the offer. We accept the price, but not the date.',
        ]);
    });

    it('takes a stretch for each paragraph a selection crosses', () => {
        const touched = passagesTouched(draft, { paragraph: 0, at: 63 }, { paragraph: 1, at: 4 });

        expect(touched.map(({ paragraph, words }) => [paragraph, words])).toStrictEqual([
            [0, 'See you soon.'],
            [1, 'Best regards, Anna'],
        ]);
    });

    it('takes nothing where a selection holds only the space between two sentences', () => {
        expect(passagesTouched(draft, { paragraph: 0, at: 21 }, { paragraph: 0, at: 22 })).toStrictEqual([]);
    });
});

describe('rewriteRequestFor', () => {
    it('marks each stretch by where it stands in the draft joined a paragraph to a line', () => {
        const touched = passagesTouched(draft, { paragraph: 0, at: 63 }, { paragraph: 1, at: 4 });
        const request = rewriteRequestFor(draft, touched, 'Make it warmer.');

        expect(request).not.toBeNull();
        expect(request?.passages.map(({ start, length }) => request.text.slice(start, start + length))).toStrictEqual([
            'See you soon.',
            'Best regards, Anna',
        ]);
        expect(request?.text).toBe(draft.join('\n'));
    });

    it('sends the paragraphs around the stretch where the whole draft is longer than one rewriting reads', () => {
        const filler = 'x'.repeat(longestRewrittenDraft / 2);
        const long = [filler, filler, 'Short one.', filler, filler];
        const touched = passagesTouched(long, { paragraph: 2, at: 0 }, { paragraph: 2, at: 3 });
        const request = rewriteRequestFor(long, touched, 'Shorter.');

        expect(request?.text.length).toBeLessThanOrEqual(longestRewrittenDraft);
        expect(request?.text).toContain('Short one.');
        expect(request?.passages.map(({ start, length }) => request.text.slice(start, start + length))).toStrictEqual([
            'Short one.',
        ]);
    });

    it('refuses stretches that alone are longer than one rewriting reads', () => {
        const long = ['A sentence.', `${'y'.repeat(longestRewrittenDraft)}.`];
        const touched = passagesTouched(long, { paragraph: 0, at: 0 }, { paragraph: 1, at: 2 });

        expect(rewriteRequestFor(long, touched, 'Shorter.')).toBeNull();
    });
});

describe('withPassagesRewritten', () => {
    it('lays each rewrite over the stretch it was written for and leaves the rest as it was', () => {
        const touched = passagesTouched(draft, { paragraph: 0, at: 0 }, { paragraph: 1, at: 2 });

        expect(withPassagesRewritten(draft, touched, ['Thank you, we agree.', 'Kind regards, Anna'])).toStrictEqual([
            'Thank you, we agree.',
            'Kind regards, Anna',
        ]);
    });
});
