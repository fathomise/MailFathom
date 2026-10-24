// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
    closeDeveloperInformation,
    developerWindowName,
    lockDeveloperInformation,
    longestPauseBetweenPresses,
    pressesToUnlock,
    pressVersionLine,
    toggleDeveloperInformation,
    useDeveloperAccess,
} from './developerAccess';

function pressed(times: number): void {
    for (let press = 0; press < times; press += 1) {
        pressVersionLine();
    }
}

/** A window as `window.open` hands one back, reduced to what the surface asks of it. */
function openedWindow(): Window & { readonly closed: boolean } {
    const opened = new EventTarget() as EventTarget & { closed: boolean; close: () => void };

    opened.closed = false;
    opened.close = () => {
        opened.closed = true;
    };

    return opened as unknown as Window & { readonly closed: boolean };
}

beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(10_000);
});

afterEach(() => {
    lockDeveloperInformation();
    vi.useRealTimers();
    vi.restoreAllMocks();
});

describe('unlocking the developer information surface', () => {
    it('unlocks on the last of a quick run of presses on the version line', () => {
        const { result, rerender } = renderHook(() => useDeveloperAccess());

        pressed(pressesToUnlock - 1);
        rerender();
        expect(result.current.unlocked).toBe(false);

        pressed(1);
        rerender();
        expect(result.current.unlocked).toBe(true);
    });

    it('starts the count again after a pause between two presses', () => {
        const { result, rerender } = renderHook(() => useDeveloperAccess());

        pressed(pressesToUnlock - 1);
        vi.advanceTimersByTime(longestPauseBetweenPresses + 1);
        pressed(1);
        rerender();

        expect(result.current.unlocked).toBe(false);
    });

    it('is forgotten with the page, being held nowhere but in memory', () => {
        pressed(pressesToUnlock);

        expect(localStorage.length).toBe(0);
        expect(sessionStorage.length).toBe(0);
    });
});

describe('opening and closing it', () => {
    it('opens in a window of its own where a mouse drives the client, and the same press closes it', () => {
        const opened = openedWindow();
        const open = vi.spyOn(window, 'open').mockReturnValue(opened);
        const { result, rerender } = renderHook(() => useDeveloperAccess());

        toggleDeveloperInformation(true);
        rerender();

        expect(open).toHaveBeenCalledWith('', developerWindowName, expect.any(String));
        expect(result.current).toMatchObject({ open: true, window: opened });

        toggleDeveloperInformation(true);
        rerender();

        expect(result.current).toMatchObject({ open: false, window: null });
        expect(opened.closed).toBe(true);
    });

    it('opens inside the page where a finger drives it, and opens no window', () => {
        const open = vi.spyOn(window, 'open');
        const { result, rerender } = renderHook(() => useDeveloperAccess());

        toggleDeveloperInformation(false);
        rerender();

        expect(open).not.toHaveBeenCalled();
        expect(result.current).toMatchObject({ open: true, window: null });
    });

    it('opens inside the page where the head refuses it a window', () => {
        vi.spyOn(window, 'open').mockReturnValue(null);
        const { result, rerender } = renderHook(() => useDeveloperAccess());

        toggleDeveloperInformation(true);
        rerender();

        expect(result.current).toMatchObject({ open: true, window: null });
    });

    it('closes when its window is closed by the window’s own control', () => {
        const opened = openedWindow();
        vi.spyOn(window, 'open').mockReturnValue(opened);
        const { result, rerender } = renderHook(() => useDeveloperAccess());

        toggleDeveloperInformation(true);
        opened.dispatchEvent(new Event('pagehide'));
        rerender();

        expect(result.current.open).toBe(false);
    });

    it('takes its window with it when the client leaves', () => {
        const opened = openedWindow();
        vi.spyOn(window, 'open').mockReturnValue(opened);

        toggleDeveloperInformation(true);
        window.dispatchEvent(new Event('pagehide'));

        expect(opened.closed).toBe(true);
    });

    it('does nothing when asked to close while it is closed', () => {
        const { result, rerender } = renderHook(() => useDeveloperAccess());

        closeDeveloperInformation();
        rerender();

        expect(result.current.open).toBe(false);
    });
});
