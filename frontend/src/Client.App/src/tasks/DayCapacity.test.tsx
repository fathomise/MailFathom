// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { DayCapacity } from './DayCapacity';

function drawPanel({ dueToday = 3, eventsToday = 2 }: { dueToday?: number; eventsToday?: number } = {}): void {
    render(
        <LocalizationProvider>
            <DayCapacity dueToday={dueToday} eventsToday={eventsToday}>
                <hr />
            </DayCapacity>
        </LocalizationProvider>,
    );
}

describe('DayCapacity', () => {
    it('says the reading was written by AI with the provenance mark rather than an AI badge', () => {
        drawPanel();

        expect(screen.getByRole('img', { name: 'Written by AI' })).toBeDefined();
        expect(screen.queryByText('AI')).toBeNull();
    });

    it('counts the day rather than composing a sentence about it', () => {
        drawPanel();

        expect(screen.getByText(/3 tasks are due today\./)).toBeDefined();
        expect(screen.getByText(/The calendar already holds 2 events\./)).toBeDefined();
    });

    it('counts one of each in the form English has for one', () => {
        drawPanel({ dueToday: 1, eventsToday: 1 });

        expect(screen.getByText(/One task is due today\./)).toBeDefined();
        expect(screen.getByText(/The calendar already holds one event\./)).toBeDefined();
    });

    it('holds the offer under the count, inside the panel it names', () => {
        drawPanel();

        expect(within(screen.getByRole('region', { name: 'Day capacity' })).getByRole('separator')).toBeDefined();
    });
});
