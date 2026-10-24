// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent } from '@testing-library/react';

/**
 * Moves focus onto an element the way a key does, which is the one focus `useTip` answers by itself. jsdom decides
 * `:focus-visible` from the last event it saw, so a bare `focus()` after a press anywhere earlier in the file reads as a
 * pointer's focus; a Tab pressed first makes it the keyboard's.
 */
export function focusByKey(element: HTMLElement): void {
    fireEvent.keyDown(document.body, { key: 'Tab' });
    act(() => {
        element.focus();
    });
}
