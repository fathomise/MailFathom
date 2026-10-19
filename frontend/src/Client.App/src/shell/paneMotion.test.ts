// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { renderHook } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { paneArrival, usePaneMotion } from './paneMotion';

function moved(from: { inFront: boolean; composition: string }) {
    return renderHook(({ inFront, composition }) => usePaneMotion(inFront, composition), { initialProps: from });
}

describe('usePaneMotion', () => {
    it('has not moved on the screen it was first drawn on', () => {
        const { result } = moved({ inFront: true, composition: 'one pane' });

        expect(result.current).toBeNull();
    });

    it('goes deeper when the view is put in front of the one it was opened from', () => {
        const { result, rerender } = moved({ inFront: false, composition: 'one pane' });

        rerender({ inFront: true, composition: 'one pane' });

        expect(result.current).toBe('push');
    });

    it('goes back when the view is taken away again', () => {
        const { result, rerender } = moved({ inFront: true, composition: 'one pane' });

        rerender({ inFront: false, composition: 'one pane' });

        expect(result.current).toBe('back');
    });

    it('does not move where the composition changed under the view instead', () => {
        const { result, rerender } = moved({ inFront: true, composition: 'one pane' });

        rerender({ inFront: false, composition: 'two panes' });

        expect(result.current).toBeNull();
    });
});

describe('paneArrival', () => {
    it('plays the deeper arrival on the view going in front, and nothing on the one it covers', () => {
        expect(paneArrival('push', true)).toBe('animate-pane-push');
        expect(paneArrival('push', false)).toBe('');
    });

    it('plays the returning arrival on the view coming back, and nothing on the one that left', () => {
        expect(paneArrival('back', false)).toBe('animate-pane-back');
        expect(paneArrival('back', true)).toBe('');
    });
});
