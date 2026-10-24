// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { DeclaredSource } from '@mailfathom/client-backend';
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
                answer: { text: 'The lease renews in March.', confidence: 'Moderate' },
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
                <AgentAnswer answer={answered} settledThrough={10} onFollowSource={followed} />
            </LocalizationProvider>,
        );

        // Act
        fireEvent.click(screen.getByRole('button', { name: 'Citation 1: Hall lease — renewal' }));

        // Assert
        expect(followed).toHaveBeenCalledWith(lease);
    });
});
