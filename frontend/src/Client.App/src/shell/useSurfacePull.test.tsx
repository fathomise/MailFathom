// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useSurfacePull } from './useSurfacePull';

// A surface standing open in the document, drawn the way the stylesheet would draw it: a sheet or a drawer travels on
// `translate`, a centred dialog does not. jsdom lays nothing out, so its height and width are given rather than measured.
function anOpenSurface(
    pull: 'down' | 'start',
    travelsOn = 'translate, display, overlay',
): { readonly surface: HTMLDialogElement; readonly closed: () => void } {
    const surface = document.createElement('dialog');
    const closed = vi.fn(() => {
        surface.removeAttribute('open');
    });

    surface.setAttribute('open', '');
    surface.setAttribute('data-pull', pull);
    surface.style.transitionProperty = travelsOn;
    surface.close = closed;
    Object.defineProperty(surface, 'offsetHeight', { value: 600 });
    Object.defineProperty(surface, 'offsetWidth', { value: 300 });

    const inside = document.createElement('p');
    surface.append(inside);
    document.body.append(surface);

    return { surface, closed };
}

function touch(type: string, on: Element, x: number, y: number): void {
    const event = new Event(type, { bubbles: true, cancelable: true });

    Object.defineProperty(event, 'touches', { value: type === 'touchend' ? [] : [{ clientX: x, clientY: y }] });
    on.dispatchEvent(event);
}

/** A finger that lands, travels through each point, and lifts — resting on the last point, so it lifts at no speed. */
function drag(on: Element, points: readonly (readonly [number, number])[]): void {
    const [first, ...rest] = points;

    if (first === undefined) {
        return;
    }

    touch('touchstart', on, ...first);

    for (const point of [...rest, rest.at(-1) ?? first]) {
        touch('touchmove', on, ...point);
    }

    touch('touchend', on, 0, 0);
}

afterEach(() => {
    document.body.replaceChildren();
});

describe('useSurfacePull', () => {
    it('closes a sheet pulled down past a third of its height, the way its close control would', () => {
        renderHook(() => {
            useSurfacePull();
        });
        const { surface: sheet, closed } = anOpenSurface('down');

        drag(sheet.firstElementChild ?? sheet, [
            [100, 100],
            [100, 120],
            [100, 400],
        ]);

        expect(closed).toHaveBeenCalledOnce();
    });

    it('lets a sheet pulled only a little go back to where it stood', () => {
        renderHook(() => {
            useSurfacePull();
        });
        const { surface: sheet, closed } = anOpenSurface('down');

        drag(sheet, [
            [100, 100],
            [100, 120],
            [100, 160],
        ]);

        expect(closed).not.toHaveBeenCalled();
        expect(sheet.style.translate).toBe('');
    });

    it('leaves the content to scroll first while it is scrolled away from its top', () => {
        renderHook(() => {
            useSurfacePull();
        });
        const { surface: sheet, closed } = anOpenSurface('down');
        const content = sheet.firstElementChild ?? sheet;
        Object.defineProperty(content, 'scrollTop', { value: 80 });

        drag(content, [
            [100, 100],
            [100, 120],
            [100, 400],
        ]);

        expect(closed).not.toHaveBeenCalled();
    });

    it('closes a drawer pushed back toward the edge it came from', () => {
        renderHook(() => {
            useSurfacePull();
        });
        const { surface: drawer, closed } = anOpenSurface('start');

        drag(drawer, [
            [250, 300],
            [230, 300],
            [40, 300],
        ]);

        expect(closed).toHaveBeenCalledOnce();
    });

    it('leaves a surface drawn as a centred dialog at this width alone', () => {
        renderHook(() => {
            useSurfacePull();
        });
        const { surface: dialog, closed } = anOpenSurface('down', 'opacity, scale, display, overlay');

        drag(dialog, [
            [100, 100],
            [100, 120],
            [100, 400],
        ]);

        expect(closed).not.toHaveBeenCalled();
    });
});
