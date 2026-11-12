// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { AgentConversationEntry, AnswerBlock } from '@mailfathom/client-backend';
import { answerInFlight, proposedNext, ranDeep, threadOf } from './threadTurns';

// The fold never reads inside a block, so the one a newer service would write stands in for any of them.
const block: AnswerBlock = { named: 'somethingNew', type: null };

const asked: AgentConversationEntry = {
    kind: 'message',
    sequence: 1,
    messageId: 'q-1',
    author: 'person',
    text: 'How many bays?',
    scope: { kind: 'thread', subject: 't-1' },
    mode: 'auto',
};

const started: AgentConversationEntry = { kind: 'answerStarted', sequence: 2, run: 'q-1' };

describe('threadOf', () => {
    it('folds what an answer was written as into the answer, under the scope of the question it answers', () => {
        const turns = threadOf([
            asked,
            started,
            { kind: 'status', sequence: 3, run: 'q-1', status: 'Reading' },
            { kind: 'status', sequence: 4, run: 'q-1', status: 'Counting' },
            { kind: 'block', sequence: 5, run: 'q-1', block },
            { kind: 'answerEnded', sequence: 6, run: 'q-1', outcome: 'completed', followUps: [], ceiling: null },
        ]);

        expect(turns).toMatchObject([
            { kind: 'question', text: 'How many bays?' },
            {
                kind: 'answer',
                run: 'q-1',
                scope: { kind: 'thread' },
                status: 'Counting',
                blocks: [{ sequence: 5, phase: null }],
                ending: 'completed',
            },
        ]);
    });

    it('carries the question an answer answers, the mode it was asked in, the move to deep, and the ceiling', () => {
        const [, answer] = threadOf([
            asked,
            started,
            { kind: 'answerDeepened', sequence: 3, run: 'q-1', reason: 'It reaches back a year.' },
            {
                kind: 'answerEnded',
                sequence: 4,
                run: 'q-1',
                outcome: 'completed',
                followUps: [],
                ceiling: { messagesRead: 120 },
            },
        ]);

        expect(answer).toMatchObject({
            question: 'How many bays?',
            mode: 'auto',
            deepened: 'It reaches back a year.',
            ceiling: { messagesRead: 120 },
        });
    });

    it('holds a proposal pending until a resolution decides it', () => {
        const proposed: AgentConversationEntry = { kind: 'proposal', sequence: 3, run: 'q-1', block };

        const pending = threadOf([asked, started, proposed]);
        const declined = threadOf([
            asked,
            started,
            proposed,
            { kind: 'resolution', sequence: 4, proposedAt: 3, state: 'declined' },
        ]);

        expect(pending[1]).toMatchObject({ blocks: [{ phase: 'pending' }] });
        expect(declined[1]).toMatchObject({ blocks: [{ phase: 'declined' }] });
    });

    it('reads a line the agent wrote after a stop as the note the stop left', () => {
        const turns = threadOf([
            asked,
            started,
            { kind: 'answerEnded', sequence: 3, run: 'q-1', outcome: 'stopped', followUps: [], ceiling: null },
            {
                kind: 'message',
                sequence: 4,
                messageId: 'n-1',
                author: 'agent',
                text: 'Stopped.',
                scope: null,
                mode: null,
            },
        ]);

        expect(turns[2]).toEqual({ kind: 'note', sequence: 4, text: 'Stopped.', afterStop: true });
    });

    it('draws nothing for an entry kind this build does not read', () => {
        expect(threadOf([{ kind: 'other', sequence: 1 }])).toEqual([]);
    });
});

describe('proposedNext', () => {
    const suggested: Extract<AgentConversationEntry, { kind: 'answerEnded' }> = {
        kind: 'answerEnded',
        sequence: 3,
        run: 'q-1',
        outcome: 'completed',
        followUps: ['Draft a reply', 'Who else was copied?'],
        ceiling: null,
    };

    it('is what the last answer suggested once it ended, at the ending that carried it', () => {
        expect(proposedNext(threadOf([asked, started, suggested]))).toEqual({
            sequence: 3,
            questions: ['Draft a reply', 'Who else was copied?'],
        });
    });

    it('is nothing while the last answer is still being composed', () => {
        expect(proposedNext(threadOf([asked, started]))).toBeNull();
    });

    it('is nothing where the answer that ended suggested nothing', () => {
        expect(proposedNext(threadOf([asked, started, { ...suggested, followUps: [] }]))).toBeNull();
    });

    it('is nothing once the conversation moved past the answer that suggested it', () => {
        const turns = threadOf([
            asked,
            started,
            suggested,
            {
                kind: 'message',
                sequence: 4,
                messageId: 'q-2',
                author: 'person',
                text: 'Draft a reply',
                scope: null,
                mode: null,
            },
        ]);

        expect(proposedNext(turns)).toBeNull();
    });
});

describe('answerInFlight', () => {
    it('is the last answer while it has not ended', () => {
        expect(answerInFlight(threadOf([asked, started]))).toMatchObject({ run: 'q-1' });
    });

    it('is nothing once the last answer ended', () => {
        const turns = threadOf([
            asked,
            started,
            { kind: 'answerEnded', sequence: 3, run: 'q-1', outcome: 'failed', followUps: [], ceiling: null },
        ]);

        expect(answerInFlight(turns)).toBeNull();
    });
});

describe('ranDeep', () => {
    it('is an answer asked in deep, or asked in auto and moved there, and no other', () => {
        const [, auto] = threadOf([asked, started]);
        const [, deepened] = threadOf([
            asked,
            started,
            { kind: 'answerDeepened', sequence: 3, run: 'q-1', reason: 'It reaches back a year.' },
        ]);
        const [, deep] = threadOf([{ ...asked, mode: 'deep' }, started]);

        expect([auto, deepened, deep].map((turn) => turn?.kind === 'answer' && ranDeep(turn))).toEqual([
            false,
            true,
            true,
        ]);
    });
});
