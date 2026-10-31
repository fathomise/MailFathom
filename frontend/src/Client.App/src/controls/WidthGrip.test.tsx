// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { pressOpensAfter } from '../contextMenu/rowPress';
import { focusByKey } from './useTip.harness';
import { LocalizationProvider } from '../localization/Localization';
import { WidthGrip } from './WidthGrip';

// One boundary, with numbers of the harness's own: they are props rather than the component's, so the cases below are
// about the grip and none of them moves when a screen changes the bounds it draws a boundary with.
const narrowestList = 232;
const widestList = 560;
const startingListWidth = 340;
const listWidthStep = 16;

const listBoundary = {
    label: 'Message list width',
    hint: 'Drag to resize, or double-click to reset.',
    narrowest: narrowestList,
    widest: widestList,
    step: listWidthStep,
    startingWidth: startingListWidth,
};

function renderGrip(width = 400): { chosen: ReturnType<typeof vi.fn>; grip: HTMLElement } {
    const chosen = vi.fn();

    render(
        <LocalizationProvider>
            <WidthGrip {...listBoundary} width={width} onWidth={vi.fn()} onChosen={chosen} />
        </LocalizationProvider>,
    );

    return { chosen, grip: screen.getByRole('separator') };
}

describe('WidthGrip', () => {
    afterEach(() => {
        vi.useRealTimers();
    });

    it('stands as a separator naming what it moves, so a reader finds it by what it is', () => {
        const { grip } = renderGrip();

        expect(screen.getByRole('separator', { name: 'Message list width' })).toBe(grip);
        expect(grip.getAttribute('aria-orientation')).toBe('vertical');
    });

    it('is named by its label alone, the grip a finger takes hold of adding nothing a screen reader would announce', () => {
        const { grip } = renderGrip();

        expect(screen.getByRole('separator', { name: 'Message list width' })).toBe(grip);
        expect(grip.textContent).toBe('');
    });

    it('reports where the boundary stands and how far it may go', () => {
        const { grip } = renderGrip(400);

        expect(grip.getAttribute('aria-valuenow')).toBe('400');
        expect(grip.getAttribute('aria-valuemin')).toBe(String(narrowestList));
        expect(grip.getAttribute('aria-valuemax')).toBe(String(widestList));
    });

    it('is reachable from the keyboard, which is the whole of what makes the split operable without a mouse', () => {
        const { grip } = renderGrip();

        grip.focus();

        expect(document.activeElement).toBe(grip);
    });

    it('narrows the list by one step on the left arrow', () => {
        const { chosen, grip } = renderGrip(400);

        fireEvent.keyDown(grip, { key: 'ArrowLeft' });

        expect(chosen).toHaveBeenCalledWith(400 - listWidthStep);
    });

    it('widens the list by one step on the right arrow', () => {
        const { chosen, grip } = renderGrip(400);

        fireEvent.keyDown(grip, { key: 'ArrowRight' });

        expect(chosen).toHaveBeenCalledWith(400 + listWidthStep);
    });

    it('returns the split to the width it started at on Home', () => {
        const { chosen, grip } = renderGrip(500);

        fireEvent.keyDown(grip, { key: 'Home' });

        expect(chosen).toHaveBeenCalledWith(startingListWidth);
    });

    it('returns the split to the width it started at on a double-click, which is the same act with a mouse', () => {
        const { chosen, grip } = renderGrip(500);

        fireEvent.doubleClick(grip);

        expect(chosen).toHaveBeenCalledWith(startingListWidth);
    });

    it('moves the boundary with the pointer, and reports the width it was let go at', () => {
        const moved = vi.fn();
        const chosen = vi.fn();

        render(
            <LocalizationProvider>
                <WidthGrip {...listBoundary} width={400} onWidth={moved} onChosen={chosen} />
            </LocalizationProvider>,
        );

        const grip = screen.getByRole('separator');
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 600 });
        fireEvent.pointerMove(grip, { pointerId: 1, clientX: 660 });
        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 680 });

        expect(moved).toHaveBeenCalledWith(460);
        expect(chosen).toHaveBeenCalledWith(480);
    });

    it('says nothing over a finger dragging the boundary, however long the drag takes', () => {
        vi.useFakeTimers();
        const { grip } = renderGrip();

        fireEvent.pointerDown(grip, { pointerId: 1, pointerType: 'touch', clientX: 600, clientY: 10 });
        fireEvent.pointerMove(grip, { pointerId: 1, pointerType: 'touch', clientX: 660, clientY: 10 });
        act(() => {
            vi.advanceTimersByTime(pressOpensAfter);
        });
        fireEvent.pointerUp(grip, { pointerId: 1, pointerType: 'touch', clientX: 680, clientY: 10 });

        expect(screen.queryByRole('tooltip')).toBeNull();
    });

    it('says nothing to a finger held still on the boundary or tapping it, its hold being the drag', () => {
        vi.useFakeTimers();
        const { grip } = renderGrip();

        fireEvent.pointerDown(grip, { pointerId: 1, pointerType: 'touch', clientX: 600, clientY: 10 });
        act(() => {
            vi.advanceTimersByTime(pressOpensAfter);
        });
        fireEvent.pointerUp(grip, { pointerId: 1, pointerType: 'touch', clientX: 600, clientY: 10 });
        fireEvent.click(grip);

        expect(screen.queryByRole('tooltip')).toBeNull();
    });

    it('says how it is used to a key that reaches it', () => {
        const { grip } = renderGrip();

        focusByKey(grip);

        expect(screen.getByRole('tooltip').textContent).toBe(listBoundary.hint);
    });

    it('ignores a second pointer arriving mid-drag, so two fingers cannot fight over one boundary', () => {
        const moved = vi.fn();

        render(
            <LocalizationProvider>
                <WidthGrip {...listBoundary} width={400} onWidth={moved} onChosen={vi.fn()} />
            </LocalizationProvider>,
        );

        const grip = screen.getByRole('separator');
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 600 });
        fireEvent.pointerMove(grip, { pointerId: 2, clientX: 900 });

        expect(moved).not.toHaveBeenCalled();
    });

    it('leaves the drag with the pointer that started it when a second one lands on the grip', () => {
        const chosen = vi.fn();

        render(
            <LocalizationProvider>
                <WidthGrip {...listBoundary} width={400} onWidth={vi.fn()} onChosen={chosen} />
            </LocalizationProvider>,
        );

        const grip = screen.getByRole('separator');
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 600 });
        fireEvent.pointerDown(grip, { pointerId: 2, clientX: 900 });

        // The second finger is ignored rather than taking the drag over, so the first pointer's release still settles
        // the width it was dragging toward instead of being dropped as a stranger's.
        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 660 });

        expect(chosen).toHaveBeenCalledWith(460);
    });

    it('moves nothing on a pointer that never took the boundary', () => {
        const moved = vi.fn();

        render(
            <LocalizationProvider>
                <WidthGrip {...listBoundary} width={400} onWidth={moved} onChosen={vi.fn()} />
            </LocalizationProvider>,
        );

        fireEvent.pointerMove(screen.getByRole('separator'), { pointerId: 1, clientX: 900 });

        expect(moved).not.toHaveBeenCalled();
    });

    it('leaves a key it does not answer to whatever is behind it', () => {
        const { chosen, grip } = renderGrip();

        fireEvent.keyDown(grip, { key: 'PageDown' });

        expect(chosen).not.toHaveBeenCalled();
    });
});
