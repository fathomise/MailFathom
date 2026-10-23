// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, renderHook } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { usePressOutsideSurface } from './usePressOutsideSurface';

// A drawer standing open along the start edge, 300 wide, drawn the way the stylesheet would draw it. jsdom lays nothing
// out, so its box is given rather than measured.
function anOpenDrawer(travelsOn = 'translate, display, overlay'): {
    readonly drawer: HTMLDialogElement;
    readonly closed: () => void;
} {
    const drawer = document.createElement('dialog');
    const closed = vi.fn(() => {
        drawer.removeAttribute('open');
    });

    drawer.setAttribute('open', '');
    drawer.setAttribute('data-pull', 'start');
    drawer.style.transitionProperty = travelsOn;
    drawer.close = closed;
    drawer.getBoundingClientRect = () => new DOMRect(0, 0, 300, 800);
    document.body.append(drawer);

    return { drawer, closed };
}

afterEach(() => {
    document.body.replaceChildren();
});

describe('usePressOutsideSurface', () => {
    it('closes a drawer pressed beside it, which is a press on its backdrop', () => {
        renderHook(() => {
            usePressOutsideSurface();
        });
        const { drawer, closed } = anOpenDrawer();

        fireEvent.pointerDown(drawer, { clientX: 340, clientY: 200 });
        fireEvent.click(drawer, { clientX: 340, clientY: 200 });

        expect(closed).toHaveBeenCalledOnce();
    });

    it('stays open for a press inside its own box, its padding included', () => {
        renderHook(() => {
            usePressOutsideSurface();
        });
        const { drawer, closed } = anOpenDrawer();

        fireEvent.pointerDown(drawer, { clientX: 280, clientY: 200 });
        fireEvent.click(drawer, { clientX: 280, clientY: 200 });

        expect(closed).not.toHaveBeenCalled();
    });

    // Selecting the text in a field and letting go over the backdrop is not a press beside the drawer.
    it('stays open for a drag begun inside it and let go beside it', () => {
        renderHook(() => {
            usePressOutsideSurface();
        });
        const { drawer, closed } = anOpenDrawer();

        fireEvent.pointerDown(drawer, { clientX: 280, clientY: 200 });
        fireEvent.click(drawer, { clientX: 340, clientY: 200 });

        expect(closed).not.toHaveBeenCalled();
    });

    it('leaves a surface drawn as a centred dialog at this width to its own controls', () => {
        renderHook(() => {
            usePressOutsideSurface();
        });
        const { drawer, closed } = anOpenDrawer('opacity, scale, display, overlay');

        fireEvent.pointerDown(drawer, { clientX: 340, clientY: 200 });
        fireEvent.click(drawer, { clientX: 340, clientY: 200 });

        expect(closed).not.toHaveBeenCalled();
    });
});
