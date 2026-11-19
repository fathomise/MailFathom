// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { Icon } from './Icon';
import { SymbolControl } from './SymbolControl';

describe('SymbolControl', () => {
    it('stays named and focusable while held, says why, and does nothing when pressed', () => {
        const pressed = vi.fn();
        render(
            <SymbolControl label="Remove the link" className="" held hint="Saving…" onPress={pressed}>
                <Icon name="link_off" />
            </SymbolControl>,
        );

        const control = screen.getByRole('button', { name: 'Remove the link' });
        fireEvent.click(control);

        expect(control.getAttribute('aria-disabled')).toBe('true');
        expect(document.getElementById(control.getAttribute('aria-describedby') ?? '')?.textContent).toBe('Saving…');
        expect(pressed).not.toHaveBeenCalled();
    });
});
