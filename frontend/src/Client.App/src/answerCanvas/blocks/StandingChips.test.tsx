// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { tipRestsFor } from '../../controls/useTip';
import { focusByKey } from '../../controls/useTip.harness';
import { LocalizationProvider } from '../../localization/Localization';
import { ConfidenceBandChip, SupportVerdictChip } from './StandingChips';

const citedExplanation =
    'The answer names the messages it was written from, and nothing checked that they back it - open a citation to see for yourself.';

function renderVerdict() {
    render(
        <LocalizationProvider>
            <SupportVerdictChip support="Cited" />
        </LocalizationProvider>,
    );

    return {
        chip: screen.getByRole('button', { name: 'cited' }),
        explanation: () => screen.getByRole('tooltip', { hidden: true }),
    };
}

describe('SupportVerdictChip', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('describes the chip with what its verdict means, without showing it at rest', () => {
        // Arrange
        const { chip, explanation } = renderVerdict();

        // Assert
        expect(explanation().textContent).toBe(citedExplanation);
        expect(chip.getAttribute('aria-describedby')).toBe(explanation().id);
        expect(explanation().hidden).toBe(true);
    });

    it('shows the explanation once a mouse rests on the chip and hides it once the mouse leaves', () => {
        // Arrange
        vi.useFakeTimers();
        const { chip, explanation } = renderVerdict();

        // Act
        fireEvent.pointerEnter(chip, { pointerType: 'mouse' });
        act(() => {
            vi.advanceTimersByTime(tipRestsFor);
        });
        const whileOver = explanation().hidden;
        fireEvent.pointerLeave(chip, { pointerType: 'mouse' });

        // Assert
        expect(whileOver).toBe(false);
        expect(explanation().hidden).toBe(true);
    });

    it('shows the explanation on keyboard focus and hides it when focus moves on or Escape is pressed', () => {
        // Arrange
        const { chip, explanation } = renderVerdict();

        // Act
        focusByKey(chip);
        const focused = explanation().hidden;
        fireEvent.keyDown(chip, { key: 'Escape' });
        const escaped = explanation().hidden;
        act(() => {
            chip.blur();
            chip.focus();
            chip.blur();
        });

        // Assert
        expect(focused).toBe(false);
        expect(escaped).toBe(true);
        expect(explanation().hidden).toBe(true);
    });

    it('shows the explanation on a tap and lets it go by itself, since a finger has no hover to leave', () => {
        // Arrange
        vi.useFakeTimers();
        const { chip, explanation } = renderVerdict();

        // Act
        fireEvent.pointerDown(chip, { pointerType: 'touch' });
        fireEvent.click(chip);
        const tapped = explanation().hidden;
        act(() => {
            vi.advanceTimersByTime(4000);
        });

        // Assert
        expect(tapped).toBe(false);
        expect(explanation().hidden).toBe(true);
    });

    it('forgets a tap the browser took back, so a later key press keeps the explanation until focus moves on', () => {
        // Arrange
        vi.useFakeTimers();
        const { chip, explanation } = renderVerdict();

        // Act
        fireEvent.pointerDown(chip, { pointerType: 'touch' });
        fireEvent.pointerCancel(chip, { pointerType: 'touch' });
        focusByKey(chip);
        fireEvent.click(chip);
        act(() => {
            vi.advanceTimersByTime(4000);
        });

        // Assert
        expect(explanation().hidden).toBe(false);
    });

    it('leaves a mouse press to the hover, keeping the explanation until the mouse leaves', () => {
        // Arrange
        vi.useFakeTimers();
        const { chip, explanation } = renderVerdict();

        // Act
        fireEvent.pointerEnter(chip, { pointerType: 'mouse' });
        act(() => {
            vi.advanceTimersByTime(tipRestsFor);
        });
        fireEvent.pointerDown(chip, { pointerType: 'mouse' });
        fireEvent.click(chip);
        act(() => {
            vi.advanceTimersByTime(4000);
        });
        const stillOver = explanation().hidden;
        fireEvent.pointerLeave(chip, { pointerType: 'mouse' });

        // Assert
        expect(stillOver).toBe(false);
        expect(explanation().hidden).toBe(true);
    });
});

describe('ConfidenceBandChip', () => {
    it.each([
        ['High', 'high confidence', 'The messages say this outright, and the answer restates what they say.'],
        ['Moderate', 'moderate confidence', 'The messages carry this, with a step of inference you may want to check.'],
        ['Low', 'low confidence', 'This is the best reading of partial sources, and it may be wrong.'],
    ] as const)('explains the %s band in its own sentence', (confidence, label, meaning) => {
        // Arrange
        render(
            <LocalizationProvider>
                <ConfidenceBandChip confidence={confidence} />
            </LocalizationProvider>,
        );

        // Act
        focusByKey(screen.getByRole('button', { name: label }));

        // Assert
        expect(screen.getByRole('tooltip').textContent).toBe(meaning);
    });
});
