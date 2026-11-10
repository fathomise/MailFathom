// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen } from '@testing-library/react';
import { useRef } from 'react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useStandingOnKeyboard } from './useStandingOnKeyboard';

// A surface that would end `foot` pixels down the window, and ends as much higher as the cover published on it — which
// is what the `above-keyboard` utility does with that number in a browser, and what jsdom, laying nothing out, cannot.
function Surface({ foot }: { readonly foot: number }) {
    const surface = useRef<HTMLElement | null>(null);

    useStandingOnKeyboard(surface);

    return (
        <section
            ref={(element) => {
                surface.current = element;

                if (element !== null) {
                    element.getBoundingClientRect = () =>
                        ({
                            bottom:
                                foot - Number.parseInt(element.style.getPropertyValue('--keyboard-cover') || '0', 10),
                        }) as DOMRect;
                }
            }}
        >
            <input />
        </section>
    );
}

// The window is 800 tall in every case; what differs is how much of it the keyboard leaves.
function viewportLeaving(height: number, scale = 1): { resize: (height: number) => void } {
    const viewport = Object.assign(new EventTarget(), { height, offsetTop: 0, scale });

    vi.stubGlobal('visualViewport', viewport);
    vi.stubGlobal('innerHeight', 800);

    return {
        resize: (to: number) => {
            viewport.height = to;
            viewport.dispatchEvent(new Event('resize'));
        },
    };
}

/** The surface standing on the keyboard, reached through the field written in it. */
function standingSurface(): HTMLElement {
    const standing = screen.getByRole('textbox').closest('section');

    if (standing === null) {
        throw new Error('The field stands in no surface.');
    }

    return standing;
}

function cover(): string {
    return standingSurface().style.getPropertyValue('--keyboard-cover');
}

describe('a surface standing on the on-screen keyboard', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('is covered by nothing while no keyboard is up', () => {
        viewportLeaving(800);

        render(<Surface foot={800} />);

        expect(cover()).toBe('');
    });

    it('learns how much of the window the keyboard took when it fills the screen', () => {
        const viewport = viewportLeaving(800);
        render(<Surface foot={800} />);

        viewport.resize(500);

        expect(cover()).toBe('300px');
        expect(standingSurface().hasAttribute('data-on-keyboard')).toBe(true);
    });

    it('measures from where it would end, which a bar of navigation under its pane puts above the window’s foot', () => {
        const viewport = viewportLeaving(800);
        render(<Surface foot={736} />);

        viewport.resize(500);

        expect(cover()).toBe('236px');
    });

    it('follows a keyboard that grows without counting what it already took off', () => {
        const viewport = viewportLeaving(800);
        render(<Surface foot={800} />);

        viewport.resize(500);
        viewport.resize(450);

        expect(cover()).toBe('350px');
    });

    it('brings the field being written in back into view when the keyboard rises', () => {
        const viewport = viewportLeaving(800);
        render(<Surface foot={800} />);

        const field = screen.getByRole('textbox');
        const broughtIntoView = vi.fn();

        field.scrollIntoView = broughtIntoView;
        field.focus();
        viewport.resize(500);

        expect(broughtIntoView).toHaveBeenCalledWith({ block: 'nearest' });
        expect(cover()).toBe('300px');
    });

    it('stands back down when the keyboard leaves', () => {
        const viewport = viewportLeaving(500);
        render(<Surface foot={800} />);

        viewport.resize(800);

        expect(cover()).toBe('0px');
        expect(standingSurface().hasAttribute('data-on-keyboard')).toBe(false);
    });

    // A surface rising from the foot of the window reports a foot below it while it travels, which is not a keyboard.
    it('is covered by nothing while it arrives from below a window no keyboard is up in', () => {
        viewportLeaving(800);

        render(<Surface foot={1200} />);

        expect(cover()).toBe('');
    });

    it('measures again once its arrival has ended, where a keyboard was already up', () => {
        viewportLeaving(500);

        render(<Surface foot={1200} />);

        expect(cover()).toBe('700px');

        // Arrived: it would end at the window's foot, less what was taken off it while it travelled.
        standingSurface().getBoundingClientRect = () => ({ bottom: 800 - 700 }) as DOMRect;
        standingSurface().dispatchEvent(new Event('animationend'));

        expect(cover()).toBe('300px');
    });

    it('reads a pinch zoom as no keyboard', () => {
        viewportLeaving(400, 2);

        render(<Surface foot={800} />);

        expect(cover()).toBe('');
    });
});
