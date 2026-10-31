// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { pressOpensAfter } from '../contextMenu/rowPress';
import { Control } from './Control';

describe('Control', () => {
    it('carries its name as words where the shape draws them', () => {
        render(<Control label="New message" icon="edit_square" onPress={() => undefined} />);

        expect(screen.getByRole('button', { name: 'New message' }).textContent).toBe('New message');
    });

    it('carries its name for the accessibility tree where the shape draws the symbol alone', () => {
        render(<Control label="Reply" icon="reply" shape="symbol" onPress={() => undefined} />);

        const control = screen.getByRole('button', { name: 'Reply' });

        expect(control.textContent).toBe('');
        expect(control.getAttribute('aria-label')).toBe('Reply');
    });

    // A head in the thread pane draws its acts as words and narrows them to symbols where the pane is narrow, which is a
    // container query rather than a tree — the browser suite holds the narrowing — so the words stay the name either way.
    it('is named by its words where the pane decides between its words and its symbol', () => {
        render(<Control label="Reply" icon="reply" shape="paneNamed" onPress={() => undefined} />);

        const control = screen.getByRole('button', { name: 'Reply' });

        expect(control.hasAttribute('aria-label')).toBe(false);
    });

    it.each([
        ['on', true, 'true'],
        ['off', false, 'false'],
    ])('says a state it holds is %s, so what it is set to is read rather than only seen', (_name, held, said) => {
        render(
            <Control
                label="Hide the panels"
                icon="fullscreen"
                shape="symbol"
                pressed={held}
                onPress={() => undefined}
            />,
        );

        expect(screen.getByRole('button', { name: 'Hide the panels' }).getAttribute('aria-pressed')).toBe(said);
    });

    // The other half of that, and the one a wrong default would break everywhere at once: a control that does a thing
    // rather than holding a state reports none, because a button carrying `aria-pressed` is announced as a toggle.
    it('reports no state at all for a control that holds none', () => {
        render(<Control label="New message" icon="edit_square" onPress={() => undefined} />);

        expect(screen.getByRole('button', { name: 'New message' }).hasAttribute('aria-pressed')).toBe(false);
    });

    // A control whose words are shorter than what it does describes the rest, and a symbol drawn alone says its name to
    // the eye as well as to the accessibility tree — while a control already reading its words says nothing twice.
    it('describes a hint in addition to its name, and adds nothing to words already on it', () => {
        render(<Control label="Ask" hint="Ask the agent about this conversation" onPress={() => undefined} />);
        render(<Control label="New message" onPress={() => undefined} />);

        const hinted = screen.getByRole('button', { name: 'Ask' });

        expect(document.getElementById(hinted.getAttribute('aria-describedby') ?? '')?.textContent).toBe(
            'Ask the agent about this conversation',
        );
        expect(screen.getByRole('button', { name: 'New message' }).hasAttribute('aria-describedby')).toBe(false);
    });

    it('shows the name of a symbol drawn alone to a finger holding it, without pressing it', () => {
        vi.useFakeTimers();
        const pressed = vi.fn();
        render(<Control label="Reply" icon="reply" shape="symbol" onPress={pressed} />);
        const control = screen.getByRole('button', { name: 'Reply' });

        fireEvent.pointerDown(control, { pointerType: 'touch' });
        act(() => {
            vi.advanceTimersByTime(pressOpensAfter);
        });
        fireEvent.pointerUp(control, { pointerType: 'touch' });
        fireEvent.click(control);

        expect(screen.getByRole('tooltip').textContent).toBe('Reply');
        expect(pressed).not.toHaveBeenCalled();
        vi.useRealTimers();
    });

    it('does what it is for when it is pressed, which is what makes it not a planned one', () => {
        const pressed = vi.fn();

        render(<Control label="New message" onPress={pressed} />);

        fireEvent.click(screen.getByRole('button', { name: 'New message' }));

        expect(pressed).toHaveBeenCalledTimes(1);
    });
});
