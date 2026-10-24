// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ReactNode } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { swipeDistance, swipeDrift } from './swipeAcross';
import { useTabSwipe } from './useTabSwipe';

const tabs = ['day', 'week', 'month', 'agenda'] as const;

function Surface({
    current,
    onChoose,
    children,
}: {
    readonly current: string;
    readonly onChoose: (tab: string) => void;
    readonly children?: ReactNode;
}) {
    return (
        <div {...useTabSwipe(tabs, current, onChoose)}>
            {current}
            {children}
        </div>
    );
}

/** A finger landing on the surface, travelling, and lifting again. */
function swipe(surface: Element, across: number, down: number, pointerType: string): void {
    const landed = { pointerId: 1, pointerType, clientX: 200, clientY: 200 };
    const travelled = { pointerId: 1, pointerType, clientX: 200 + across, clientY: 200 + down };

    fireEvent.pointerDown(surface, landed);
    fireEvent.pointerMove(surface, travelled);
    fireEvent.pointerUp(surface, travelled);
}

function swiped(current: string, across: number, down = 0, pointerType = 'touch') {
    const onChoose = vi.fn<(tab: string) => void>();
    const { container } = render(<Surface current={current} onChoose={onChoose} />);
    const surface = container.firstElementChild;

    if (surface === null) {
        throw new Error('The surface was not drawn.');
    }

    swipe(surface, across, down, pointerType);

    return onChoose;
}

describe('useTabSwipe', () => {
    it('opens the tab to the right on a swipe to the left, and the one before on a swipe to the right', () => {
        expect(swiped('week', -swipeDistance)).toHaveBeenCalledExactlyOnceWith('month');
        expect(swiped('week', swipeDistance)).toHaveBeenCalledExactlyOnceWith('day');
    });

    it('does nothing past either end rather than wrapping round', () => {
        expect(swiped('agenda', -swipeDistance)).not.toHaveBeenCalled();
        expect(swiped('day', swipeDistance)).not.toHaveBeenCalled();
    });

    it('leaves the tab alone for a finger that has not gone far enough', () => {
        expect(swiped('week', -(swipeDistance - 1))).not.toHaveBeenCalled();
    });

    it('takes a finger that wandered further down than across for a scroll', () => {
        expect(swiped('week', -swipeDrift, swipeDrift + 1)).not.toHaveBeenCalled();
    });

    it('switches nothing for a mouse dragged across', () => {
        expect(swiped('week', -swipeDistance, 0, 'mouse')).not.toHaveBeenCalled();
    });

    // A caret or a selection dragged across a field is the field's gesture, and a section switched under it would take
    // what was typed away before the field had sent it.
    it('switches nothing for a finger that lands in a field', () => {
        const onChoose = vi.fn<(tab: string) => void>();
        render(
            <Surface current="week" onChoose={onChoose}>
                <input />
            </Surface>,
        );

        swipe(screen.getByRole('textbox'), -swipeDistance, 0, 'touch');

        expect(onChoose).not.toHaveBeenCalled();
    });

    it('switches once for one swipe however far the finger goes on', () => {
        const onChoose = vi.fn<(tab: string) => void>();
        render(<Surface current="day" onChoose={onChoose} />);
        const surface = screen.getByText('day');

        fireEvent.pointerDown(surface, { pointerId: 1, pointerType: 'touch', clientX: 300, clientY: 0 });
        fireEvent.pointerMove(surface, {
            pointerId: 1,
            pointerType: 'touch',
            clientX: 300 - swipeDistance,
            clientY: 0,
        });
        fireEvent.pointerMove(surface, { pointerId: 1, pointerType: 'touch', clientX: 0, clientY: 0 });

        expect(onChoose).toHaveBeenCalledExactlyOnceWith('week');
    });
});
