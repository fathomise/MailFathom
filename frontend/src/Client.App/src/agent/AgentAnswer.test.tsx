// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { AgentMessageScope, DeclaredSource } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { AgentAnswer } from './AgentAnswer';
import type { Answer } from './threadTurns';

const lease: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'email', email: '0198f4a1-0000-7000-8000-000000000001' },
    label: 'Hall lease — renewal',
    medium: 'Written',
    unreadable: null,
};

const answered: Answer = {
    kind: 'answer',
    sequence: 2,
    run: 'run-1',
    scope: null,
    question: 'When does the lease renew?',
    mode: null,
    deepened: null,
    ceiling: null,
    sources: new Map([[lease.id, lease]]),
    status: null,
    ending: 'completed',
    followUps: null,
    blocks: [
        {
            sequence: 3,
            phase: null,
            block: {
                type: 'answer',
                named: 'answer',
                evidence: {
                    support: 'Cited',
                    citations: [lease.id],
                    freshness: { staleness: 'Unknown', observedAt: null },
                    conflictingClaims: [],
                },
                answer: { text: 'The lease renews in March.', confidence: 'Moderate', links: [] },
            },
        },
    ],
};

describe('AgentAnswer', () => {
    it('follows a citation to the source the answer declared under its name', () => {
        // Arrange
        const followed = vi.fn();
        render(
            <LocalizationProvider>
                <AgentAnswer answer={answered} settledThrough={10} onFollowSource={followed} onAskInDeep={null} />
            </LocalizationProvider>,
        );

        // Act
        fireEvent.click(screen.getByRole('button', { name: 'Citation 1: Hall lease — renewal' }));

        // Assert
        expect(followed).toHaveBeenCalledWith(lease);
    });

    it.each([
        ['fast', null, 'Fast'],
        ['deep', null, 'Deep'],
        ['auto', null, 'Auto · fast'],
        ['auto', 'It reaches back eight months.', 'Auto · went deep'],
    ] as const)('says an answer asked in %s ran as it did', (mode, deepened, said) => {
        drawnAnswer({ ...answered, mode, deepened });

        expect(screen.getByText(said)).toBeDefined();
    });

    it('says no mode for an answer to a question asked before a question carried one', () => {
        drawnAnswer(answered);

        expect(screen.queryByText(/^(Fast|Deep|Auto · fast|Auto · went deep)$/u)).toBeNull();
        expect(screen.getByText('The lease renews in March.')).toBeDefined();
    });

    it('says an auto run went deep from the moment it moved, and why once it has answered', () => {
        const running: Answer = { ...answered, mode: 'auto', deepened: 'It reaches back eight months.', ending: null };
        const { rerender } = drawnAnswer(running);

        expect(screen.getByText('Auto · went deep')).toBeDefined();
        expect(screen.queryByText('It reaches back eight months.')).toBeNull();

        rerender(inLocalization({ ...running, ending: 'completed' }));

        expect(screen.getByText('It reaches back eight months.')).toBeDefined();
    });

    it('draws a deep run in progress as what it is doing, and that it can be left', () => {
        drawnAnswer({ ...answered, mode: 'deep', ending: null, blocks: [], status: 'Reading supplier mail' });

        expect(screen.getByRole('status').textContent).toBe('Reading supplier mail');
        expect(screen.getByText(/^You can leave this conversation/u)).toBeDefined();
        expect(screen.queryByText('The agent is going through your mail…')).toBeNull();
    });

    it('offers a fast answer that stopped at its ceiling the same question in deep, about what it was asked about', async () => {
        const asked = vi.fn(() => Promise.resolve());
        const thread: AgentMessageScope = { kind: 'thread', subject: '0198f4a1-0000-7000-8000-00000000b001' };
        drawnAnswer({ ...answered, scope: thread, mode: 'fast', ceiling: { messagesRead: 200 } }, asked);

        expect(
            screen.getByText('Stopped at the fast limit: it read 200 messages. The rest may change the answer.'),
        ).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Ask again in Deep' }));

        await waitFor(() => {
            expect(asked).toHaveBeenCalledWith('When does the lease renew?', thread);
        });
    });

    it('offers nothing further where a deep answer stopped at its ceiling', () => {
        drawnAnswer({ ...answered, mode: 'deep', ceiling: { messagesRead: 1 } }, vi.fn());

        expect(
            screen.getByText('Stopped at the deep limit: it read 1 message. The rest may change the answer.'),
        ).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Ask again in Deep' })).toBeNull();
    });

    it('holds the offer back while the conversation cannot take a question', () => {
        drawnAnswer({ ...answered, mode: 'auto', ceiling: { messagesRead: 200 } }, null);

        expect(screen.getByRole('button', { name: 'Ask again in Deep' })).toHaveProperty('disabled', true);
    });

    it('draws a file the run placed before its words under them, in a list of the files it hands over', () => {
        const handingOver: Answer = {
            ...answered,
            blocks: [
                {
                    sequence: 1,
                    phase: null,
                    block: {
                        type: 'file',
                        named: 'file',
                        evidence: {
                            support: 'Cited',
                            citations: [lease.id],
                            freshness: { staleness: 'Unknown', observedAt: null },
                            conflictingClaims: [],
                        },
                        file: {
                            source: lease.id,
                            attachmentPosition: 0,
                            name: 'Hall lease.pdf',
                            mediaType: 'application/pdf',
                            sizeOctets: 6_144,
                            sender: null,
                            sentAt: null,
                        },
                    },
                },
                ...answered.blocks,
            ],
        };

        drawnAnswer(handingOver);

        const files = screen.getByRole('list', { name: 'Files it hands over' });
        const words = screen.getByText('The lease renews in March.');

        expect(files.textContent).toContain('Hall lease.pdf');
        expect(words.compareDocumentPosition(files) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    });
});

function inLocalization(
    answer: Answer,
    onAskInDeep: ((question: string, scope: AgentMessageScope | null) => Promise<unknown>) | null = null,
) {
    return (
        <LocalizationProvider>
            <AgentAnswer answer={answer} settledThrough={10} onFollowSource={vi.fn()} onAskInDeep={onAskInDeep} />
        </LocalizationProvider>
    );
}

function drawnAnswer(
    answer: Answer,
    onAskInDeep: ((question: string, scope: AgentMessageScope | null) => Promise<unknown>) | null = null,
) {
    return render(inLocalization(answer, onAskInDeep));
}
