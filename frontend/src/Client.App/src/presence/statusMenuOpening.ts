// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// How the status menu is reached from a row standing in another surface — the user menu, or the *More* sheet. The menu
// stands in place of that surface rather than over it, so opening it closes the surface, and closing it gives focus back
// to the control the surface was opened from. A module of its own beside `StatusMenu.tsx`, because a module exporting a
// component exports nothing else.

/** The one element the rows that open the menu name. */
export const statusMenu = 'status-menu';

/** Opens the status menu in place of the surface the pressed row stands in. */
export function openStatusMenu(pressed: HTMLElement): void {
    const standing = pressed.closest<HTMLElement>('[popover]');
    const menu = document.getElementById(statusMenu);

    if (menu === null) {
        return;
    }

    menu.dataset['returnsTo'] = standing?.id ?? '';
    standing?.hidePopover();
    menu.showPopover();
}

// The control the surface the menu replaced was opened from, which is where focus goes when the menu closes.
export function returnFocus(menu: HTMLElement): void {
    const surface = menu.dataset['returnsTo'];

    if (surface !== undefined && surface !== '') {
        document.querySelector<HTMLElement>(`[popovertarget="${surface}"]`)?.focus();
    }
}
