// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { mostAskedQuestions, withAsked } from './askedQuestions';

describe('withAsked', () => {
    it('puts the newest question at the front', () => {
        const asked = withAsked([{ question: 'first' }], 'second');

        expect(asked).toEqual([{ question: 'second' }, { question: 'first' }]);
    });

    it('moves a question asked again rather than writing it twice', () => {
        const asked = withAsked(
            [{ question: 'who signed it' }, { question: 'what did they promise' }],
            'what did they promise',
        );

        expect(asked).toEqual([{ question: 'what did they promise' }, { question: 'who signed it' }]);
    });

    it('drops the oldest once the list is as long as one tab keeps', () => {
        const asked = Array.from({ length: mostAskedQuestions }, (_, at) => ({ question: `question ${String(at)}` }));

        const kept = withAsked(asked, 'the newest one');

        expect(kept).toHaveLength(mostAskedQuestions);
        expect(kept[0]?.question).toBe('the newest one');
        expect(kept.some((one) => one.question === `question ${String(mostAskedQuestions - 1)}`)).toBe(false);
    });
});
