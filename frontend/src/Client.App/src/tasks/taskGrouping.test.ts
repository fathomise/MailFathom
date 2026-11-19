// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { PersonalTask } from '@mailfathom/client-backend';
import { inDueOrder } from './taskGrouping';

function task(id: string, dueOn: string | null): PersonalTask {
    return {
        id,
        title: id,
        dueOn,
        reminders: [],
        remindsAt: [],
        origin: 'Asserted',
        completed: false,
        sourceMessageId: null,
        direction: 'Owed',
        estimatedMinutes: null,
        plan: null,
        anchor: null,
        counterpart: null,
        answer: null,
    };
}

describe('inDueOrder', () => {
    it('interleaves the two halves by the day each is due on', () => {
        const order = inDueOrder(
            [task('own-monday', '2026-09-21'), task('own-friday', '2026-09-25')],
            [task('proposed-wednesday', '2026-09-23')],
        );

        expect(order.map((held) => held.id)).toEqual(['own-monday', 'proposed-wednesday', 'own-friday']);
    });

    it('puts what nobody has dated last, which is where the deployment walks it', () => {
        const order = inDueOrder([task('undated', null), task('dated', '2026-09-25')], []);

        expect(order.map((held) => held.id)).toEqual(['dated', 'undated']);
    });

    it('keeps two tasks due on one day in the order they were given', () => {
        const order = inDueOrder([task('committed', '2026-09-21')], [task('proposed', '2026-09-21')]);

        expect(order.map((held) => held.id)).toEqual(['committed', 'proposed']);
    });
});
