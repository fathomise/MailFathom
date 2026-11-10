// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render } from '@testing-library/react';
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
            data-testid="surface"
        >
            <input data-testid="field" />
        </section>
    );
}

// The window is 800 tall in every case; what differs is how much of it the keyboard leaves.
function viewportLeaving(height: number, scale = 1): { resize: (height: number) => void } {
    const viewport = Object.assign(new EventTarget(), { height, offsetTop: 0, scale });

    vi.stubGlobal('visualViewport', viewport);

    return {
        resize: (to: number) => {
            viewport.height = to;
            viewport.dispatchEvent(new Event('resize'));
        },
    };
}

function coverOf(surface: HTMLElement): string {
    return surface.style.getPropertyValue('--keyboard-cover');
}

describe('a surface standing on the on-screen keyboard', () => {
    afterEach(() => {
        vi.unstubAllGlobals();
    });

    it('is covered by nothing while no keyboard is up', () => {
        viewportLeaving(800);

        const { getByTestId } = render(<Surface foot={800} />);

        expect(coverOf(getByTestId('surface'))).toBe('');
    });

    it('learns how much of the window the keyboard took when it fills the screen', () => {
        const viewport = viewportLeaving(800);
        const { getByTestId } = render(<Surface foot={800} />);

        viewport.resize(500);

        expect(coverOf(getByTestId('surface'))).toBe('300px');
        expect(getByTestId('surface').hasAttribute('data-on-keyboard')).toBe(true);
    });

    it('measures from where it would end, which a bar of navigation under its pane puts above the window’s foot', () => {
        const viewport = viewportLeaving(800);
        const { getByTestId } = render(<Surface foot={736} />);

        viewport.resize(500);

        expect(coverOf(getByTestId('surface'))).toBe('236px');
    });

    it('follows a keyboard that grows without counting what it already took off', () => {
        const viewport = viewportLeaving(800);
        const { getByTestId } = render(<Surface foot={800} />);

        viewport.resize(500);
        viewport.resize(450);

        expect(coverOf(getByTestId('surface'))).toBe('350px');
    });

    it('brings the field being written in back into view when the keyboard rises', () => {
        const viewport = viewportLeaving(800);
        const { getByTestId } = render(<Surface foot={800} />);
        const field = getByTestId('field');
        const broughtIntoView = vi.fn();

        field.scrollIntoView = broughtIntoView;
        field.focus();
        viewport.resize(500);

        expect(broughtIntoView).toHaveBeenCalledWith({ block: 'nearest' });
        expect(coverOf(getByTestId('surface'))).toBe('300px');
    });

    it('stands back down when the keyboard leaves', () => {
        const viewport = viewportLeaving(500);
        const { getByTestId } = render(<Surface foot={800} />);

        viewport.resize(800);

        expect(coverOf(getByTestId('surface'))).toBe('0px');
        expect(getByTestId('surface').hasAttribute('data-on-keyboard')).toBe(false);
    });

    it('reads a pinch zoom as no keyboard', () => {
        viewportLeaving(400, 2);

        const { getByTestId } = render(<Surface foot={800} />);

        expect(coverOf(getByTestId('surface'))).toBe('');
    });
});
