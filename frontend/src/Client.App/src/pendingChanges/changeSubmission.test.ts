// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailMutationOutcome, MailMutationResult } from '@mailfathom/client-backend';
import { refusalsIn } from './changeSubmission';

function answered(storedEmailId: string, outcome: MailMutationOutcome): MailMutationResult {
    return {
        storedEmailId,
        outcome,
        changes: outcome === 'recorded' ? [{ recordId: `record-${storedEmailId}`, state: 'pending' }] : [],
    };
}

describe('refusalsIn', () => {
    it('reports nothing for a message the deployment recorded or already made the change to', () => {
        expect(refusalsIn([answered('first', 'recorded'), answered('second', 'applied')])).toEqual(new Map());
    });

    it('gathers the messages refused for one reason under that reason, in the order they were answered', () => {
        const refused = refusalsIn([
            answered('first', 'message-not-found'),
            answered('second', 'applied'),
            answered('third', 'message-not-found'),
            answered('fourth', 'destination-not-found'),
        ]);

        expect(refused).toEqual(
            new Map([
                ['message-not-found', ['first', 'third']],
                ['destination-not-found', ['fourth']],
            ]),
        );
    });

    it('counts a message already where it was asked to go among the refusals, which the queue then says nothing about', () => {
        expect(refusalsIn([answered('first', 'already-in-destination')])).toEqual(
            new Map([['already-in-destination', ['first']]]),
        );
    });
});
