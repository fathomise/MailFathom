// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { pressDrift, pressOpensAfter, useRowPress } from '../contextMenu/rowPress';
import { tipRestsFor, tipStandsFor, useTip } from './useTip';
import { focusByKey } from './useTip.harness';

function Named({
    name,
    onPress,
    onHold,
}: {
    readonly name: string;
    readonly onPress?: () => void;
    readonly onHold?: () => void;
}) {
    const { anchor, tip } = useTip({ says: name, onPress, onHold });

    return (
        <>
            <button {...anchor} type="button" aria-label={name} />
            {tip}
        </>
    );
}

/** A row that answers a held finger with its menu, holding a control that answers the same finger with its name. */
function Row({ onMenu, onPress }: { readonly onMenu: (at: MenuPoint) => void; readonly onPress: () => void }) {
    const press = useRowPress(onMenu);

    return (
        <div
            role="option"
            aria-selected="false"
            onContextMenu={press.onContextMenu}
            onPointerDown={press.onPointerDown}
            onPointerMove={press.onPointerMove}
            onPointerUp={press.onPointerUp}
            onPointerCancel={press.onPointerCancel}
        >
            <Named name="Archive" onPress={onPress} />
        </div>
    );
}

function hold(element: HTMLElement): void {
    fireEvent.pointerDown(element, { pointerType: 'touch', clientX: 10, clientY: 10 });
    act(() => {
        vi.advanceTimersByTime(pressOpensAfter);
    });
}

describe('useTip', () => {
    beforeEach(() => {
        vi.useFakeTimers();
    });

    afterEach(() => {
        vi.useRealTimers();
    });

    it('waits for a mouse to rest before saying anything, so crossing a toolbar does not flicker', () => {
        render(<Named name="Archive" onPress={() => undefined} />);
        const control = screen.getByRole('button', { name: 'Archive' });

        fireEvent.pointerEnter(control, { pointerType: 'mouse' });
        act(() => {
            vi.advanceTimersByTime(tipRestsFor - 1);
        });
        const early = screen.queryByRole('tooltip');
        act(() => {
            vi.advanceTimersByTime(1);
        });

        expect(early).toBeNull();
        expect(screen.getByRole('tooltip').textContent).toBe('Archive');
    });

    it('acts on a tap and says nothing, a tap being what the control is for', () => {
        const pressed = vi.fn();
        render(<Named name="Archive" onPress={pressed} />);
        const control = screen.getByRole('button', { name: 'Archive' });

        fireEvent.pointerDown(control, { pointerType: 'touch' });
        fireEvent.pointerUp(control, { pointerType: 'touch' });
        fireEvent.click(control);

        expect(pressed).toHaveBeenCalledOnce();
        expect(screen.queryByRole('tooltip')).toBeNull();
    });

    it('does what a hold does once, says so, and never presses — the platform menu after it included', () => {
        const pressed = vi.fn();
        const held = vi.fn();
        render(<Named name="Narrow the list" onPress={pressed} onHold={held} />);
        const control = screen.getByRole('button', { name: 'Narrow the list' });

        hold(control);
        fireEvent.pointerCancel(control);
        fireEvent.contextMenu(control);
        fireEvent.click(control);

        expect(held).toHaveBeenCalledOnce();
        expect(pressed).not.toHaveBeenCalled();
        expect(screen.getByRole('tooltip').textContent).toBe('Narrow the list');
    });

    it('reaches what a hold does from a secondary click or the context-menu key as well', () => {
        const held = vi.fn();
        render(<Named name="Narrow the list" onPress={() => undefined} onHold={held} />);
        const control = screen.getByRole('button', { name: 'Narrow the list' });

        const platformMenu = fireEvent.contextMenu(control);

        expect(held).toHaveBeenCalledOnce();
        expect(platformMenu).toBe(false);
        expect(screen.getByRole('tooltip')).toBeDefined();
    });

    it('lets a name a finger brought up go by itself, since a finger has no hover to leave', () => {
        render(<Named name="Archive" onPress={() => undefined} />);

        hold(screen.getByRole('button', { name: 'Archive' }));
        const held = screen.queryByRole('tooltip');
        act(() => {
            vi.advanceTimersByTime(tipStandsFor);
        });

        expect(held).not.toBeNull();
        expect(screen.queryByRole('tooltip')).toBeNull();
    });

    it('opens the name of a control held inside a row, and never the row’s menu as well', () => {
        const menu = vi.fn<(at: MenuPoint) => void>();
        const pressed = vi.fn();
        render(<Row onMenu={menu} onPress={pressed} />);
        const control = screen.getByRole('button', { name: 'Archive' });

        hold(control);
        const platformMenu = fireEvent.contextMenu(control);
        fireEvent.pointerUp(control, { pointerType: 'touch' });
        fireEvent.click(control);

        expect(screen.getByRole('tooltip').textContent).toBe('Archive');
        expect(menu).not.toHaveBeenCalled();
        expect(platformMenu).toBe(false);
        expect(pressed).not.toHaveBeenCalled();
    });

    it('still lets the row open its menu for a finger held anywhere else on it', () => {
        const menu = vi.fn<(at: MenuPoint) => void>();
        render(<Row onMenu={menu} onPress={() => undefined} />);

        hold(screen.getByRole('option'));

        expect(menu).toHaveBeenCalledOnce();
        expect(screen.queryByRole('tooltip')).toBeNull();
    });

    it('takes the name away once the held finger starts travelling, which is a swipe rather than a hold', () => {
        render(<Named name="Archive" onPress={() => undefined} />);
        const control = screen.getByRole('button', { name: 'Archive' });

        hold(control);
        fireEvent.pointerMove(control, { pointerType: 'touch', clientX: 10 + pressDrift + 1, clientY: 10 });

        expect(screen.queryByRole('tooltip')).toBeNull();
    });

    it('stands one sentence at a time, the newest taking the place of the one before', () => {
        render(
            <>
                <Named name="Archive" onPress={() => undefined} />
                <Named name="Delete" onPress={() => undefined} />
            </>,
        );

        hold(screen.getByRole('button', { name: 'Archive' }));
        hold(screen.getByRole('button', { name: 'Delete' }));

        expect(screen.getAllByRole('tooltip').map((tip) => tip.textContent)).toEqual(['Delete']);
    });

    it('says a sentence on a tap where the element does nothing else', () => {
        render(<Named name="Archive - not built yet" />);
        const control = screen.getByRole('button');

        fireEvent.pointerDown(control, { pointerType: 'touch' });
        fireEvent.click(control);

        expect(screen.getByRole('tooltip').textContent).toBe('Archive - not built yet');
    });

    it('forgets a tap the browser took back, so a key pressing the element later keeps the sentence until focus moves', () => {
        render(<Named name="Archive - not built yet" />);
        const control = screen.getByRole('button');

        fireEvent.pointerDown(control, { pointerType: 'touch' });
        fireEvent.pointerCancel(control, { pointerType: 'touch' });
        focusByKey(control);
        fireEvent.click(control);
        act(() => {
            vi.advanceTimersByTime(tipStandsFor);
        });

        expect(screen.getByRole('tooltip').textContent).toBe('Archive - not built yet');
    });

    it('goes on Escape without the key reaching anything around it', () => {
        const around = vi.fn();
        render(
            <div onKeyDown={around}>
                <Named name="Archive" onPress={() => undefined} />
            </div>,
        );
        const control = screen.getByRole('button', { name: 'Archive' });

        focusByKey(control);
        const focused = screen.queryByRole('tooltip');
        fireEvent.keyDown(control, { key: 'Escape' });

        expect(focused).not.toBeNull();
        expect(screen.queryByRole('tooltip')).toBeNull();
        expect(around).not.toHaveBeenCalled();
    });
});
