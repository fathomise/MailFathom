// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, renderHook, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ScreenLayersContext, useScreenLayerStack, type ScreenLayers } from '../shell/screenLayers';
import { ContextMenu, type ContextMenuItem } from './ContextMenu';
import { pressSuppressesTapFor } from './rowPress';

const archiving = vi.fn();

const items: readonly ContextMenuItem[] = [
    { icon: 'check_box', label: 'Select messages', choose: vi.fn() },
    { icon: 'archive', label: 'Archive', choose: archiving },
    { icon: 'delete', label: 'Delete', destroys: true, choose: vi.fn() },
];

function menuOver(
    closed: () => void = vi.fn(),
    offered: readonly ContextMenuItem[] = items,
): { readonly current: ScreenLayers } {
    // The shell around it, because the menu records itself as standing over the screen for as long as it is drawn.
    // Its three functions are the same ones for the life of the stack, so the value handed to the provider stays
    // current however the count moves.
    const { result } = renderHook(() => useScreenLayerStack());

    render(
        <LocalizationProvider>
            <ScreenLayersContext value={result.current}>
                <ContextMenu
                    header="Contract annex — signatures"
                    at={{ x: 40, y: 60 }}
                    items={offered}
                    onClose={closed}
                />
            </ScreenLayersContext>
        </LocalizationProvider>,
    );

    return result;
}

function walked(): HTMLElement[] {
    return screen.getAllByRole('menuitem');
}

describe('ContextMenu', () => {
    it('announces as a menu named by what it is about', () => {
        menuOver();

        expect(screen.getByRole('menu', { name: 'Contract annex — signatures' })).toBeTruthy();
    });

    it('draws every item it was given, in the order it was given them', () => {
        menuOver();

        expect(walked().map((item) => item.textContent)).toStrictEqual(['Select messages', 'Archive', 'Delete']);
    });

    it('puts focus on the first item as it opens, so the keyboard is already in the menu', () => {
        menuOver();

        expect(document.activeElement).toBe(walked()[0]);
    });

    it('walks down the items with the arrow keys', () => {
        menuOver();

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' });

        expect(document.activeElement).toBe(walked()[1]);
    });

    it('walks back up them the same way', () => {
        menuOver();

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' });
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowUp' });

        expect(document.activeElement).toBe(walked()[0]);
    });

    it('comes round to the last item from the first, so nothing is unreachable in one direction', () => {
        menuOver();

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowUp' });

        expect(document.activeElement).toBe(walked()[2]);
    });

    it('reaches the ends of the menu directly', () => {
        menuOver();

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'End' });

        expect(document.activeElement).toBe(walked()[2]);
    });

    it('closes on Escape', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

        expect(closed).toHaveBeenCalledOnce();
    });

    it('closes rather than letting the keyboard tab out behind it', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Tab' });

        expect(closed).toHaveBeenCalledOnce();
    });

    it('closes on a press outside it', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.pointerDown(document.body);

        expect(closed).toHaveBeenCalledOnce();
    });

    it('stays open when the press was inside it, which is a reader reaching for an item', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.pointerDown(screen.getByRole('menuitem', { name: 'Archive' }));

        expect(closed).not.toHaveBeenCalled();
    });

    it('closes on a later menu gesture outside it, which is a reader pointing somewhere else', () => {
        const closed = vi.fn();
        const now = vi.spyOn(performance, 'now').mockReturnValue(1000);

        menuOver(closed);
        now.mockReturnValue(1000 + pressSuppressesTapFor + 1);
        fireEvent.contextMenu(document.body);
        now.mockRestore();

        expect(closed).toHaveBeenCalledOnce();
    });

    it('stays open through the platform’s own menu gesture for the long press that opened it', () => {
        const closed = vi.fn();
        const now = vi.spyOn(performance, 'now').mockReturnValue(1000);

        menuOver(closed);
        now.mockReturnValue(1000 + pressSuppressesTapFor - 100);
        fireEvent.contextMenu(document.body);
        now.mockRestore();

        expect(closed).not.toHaveBeenCalled();
    });

    it('refuses the pointer’s own menu gesture inside itself as well as outside, and stays where it is', () => {
        const closed = vi.fn();

        menuOver(closed);

        expect(fireEvent.contextMenu(screen.getByRole('menuitem', { name: 'Archive' }))).toBe(false);
        expect(closed).not.toHaveBeenCalled();
    });

    it('performs the item that was chosen and goes', () => {
        const closed = vi.fn();

        menuOver(closed);
        fireEvent.click(screen.getByRole('menuitem', { name: 'Archive' }));

        expect(archiving).toHaveBeenCalledOnce();
        expect(closed).toHaveBeenCalledOnce();
    });

    // A write while the server is out of reach is refused before it is pressed: the item stays, says why in place of
    // its name, and choosing it does nothing — the menu stays open, so the reader is not left wondering what happened.
    it('says why an item cannot act in its place, and does nothing when it is chosen', () => {
        const closed = vi.fn();
        const refusedArchive = vi.fn();

        menuOver(closed, [
            {
                icon: 'archive',
                label: 'Archive',
                refused: 'Archive - no connection to the MailFathom server.',
                choose: refusedArchive,
            },
        ]);

        const item = screen.getByRole('menuitem', { name: 'Archive - no connection to the MailFathom server.' });

        expect(item.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(item);

        expect(refusedArchive).not.toHaveBeenCalled();
        expect(closed).not.toHaveBeenCalled();
    });

    // A request the composer makes is turned over rather than taken, so its state is announced and the menu stays for
    // the next one — and the keyboard walks it like any other item.
    it('announces a checkable item’s state, and turns it over without closing', () => {
        const closed = vi.fn();
        const turned = vi.fn();

        menuOver(closed, [
            { icon: 'move_to_inbox', label: 'Request delivery notification', checked: false, choose: turned },
            { icon: 'mark_email_read', label: 'Request read receipt', checked: true, choose: vi.fn() },
        ]);

        const off = screen.getByRole('menuitemcheckbox', { name: 'Request delivery notification' });

        expect(off.getAttribute('aria-checked')).toBe('false');
        expect(
            screen.getByRole('menuitemcheckbox', { name: 'Request read receipt' }).getAttribute('aria-checked'),
        ).toBe('true');

        fireEvent.click(off);

        expect(turned).toHaveBeenCalledOnce();
        expect(closed).not.toHaveBeenCalled();

        fireEvent.keyDown(off, { key: 'ArrowDown' });

        expect(document.activeElement).toBe(screen.getByRole('menuitemcheckbox', { name: 'Request read receipt' }));
    });

    it('names a group under its heading, and takes one of its choices and goes', () => {
        const closed = vi.fn();
        const taken = vi.fn();

        menuOver(closed, [
            { icon: 'check_box', label: 'Request read receipt', checked: false, choose: vi.fn() },
            {
                icon: 'wb_twilight',
                label: 'Tomorrow morning',
                hint: 'Fri 08:00',
                checked: true,
                takesOne: true,
                opensGroup: 'Send later',
                choose: taken,
            },
            { icon: 'light_mode', label: 'Tomorrow afternoon', checked: false, takesOne: true, choose: vi.fn() },
        ]);

        const morning = screen.getByRole('menuitemradio', { name: /^Tomorrow morning/u });

        expect(screen.getByRole('separator', { name: 'Send later' })).toBeTruthy();
        expect(morning.textContent).toContain('Fri 08:00');
        expect(morning.getAttribute('aria-checked')).toBe('true');
        expect(screen.getByRole('menuitemradio', { name: 'Tomorrow afternoon' }).getAttribute('aria-checked')).toBe(
            'false',
        );

        fireEvent.keyDown(screen.getByRole('menuitemcheckbox', { name: 'Request read receipt' }), {
            key: 'ArrowDown',
        });

        expect(document.activeElement).toBe(morning);

        fireEvent.click(morning);

        expect(taken).toHaveBeenCalledOnce();
        expect(closed).toHaveBeenCalledOnce();
    });

    // It stands over whatever it was opened on, so the back gesture closes it before it navigates and a change of
    // destination leaves none of it behind. It is on the screen for exactly as long as it is drawn, which is why the
    // registration is unconditional rather than following a state of its own.
    it('closes when the back gesture reaches it, rather than standing over the next screen', () => {
        const closed = vi.fn();
        const shell = menuOver(closed);

        expect(shell.current.depth).toBe(1);

        act(() => {
            shell.current.closeTop();
        });

        expect(closed).toHaveBeenCalled();
    });
});
