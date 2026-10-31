// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, renderHook, screen } from '@testing-library/react';
import { useState, type ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import type { Space } from '../routing/spaces';
import { ConnectionStatusContext, type ConnectionReading } from './connectionStatus';
import { ScreenLayersContext, useScreenLayerStack, type ScreenLayers } from './screenLayers';
import { SpaceOverflow } from './SpaceOverflow';

// jsdom draws a closed popover hidden and opens none — it implements neither the invoker nor `showPopover` — so what
// is inside the sheet is read through `hidden: true`, which is what a browser reports of a closed popover too. That is
// the same reading `AccountMenu.test.tsx` takes, and for the same reason: opening one is the platform's rather than
// this component's, and the browser suite is where that is proven.

const handedTheAccount = 'The account control this sheet was handed.';
const handedTheRefresh = 'The refresh this sheet was handed.';
const handedTheDeveloper = 'The developer control this sheet was handed.';

/** The connection status as the frame hands it, holding its details open or shut the way the frame does. */
function Status({ reading, children }: { readonly reading: ConnectionReading; readonly children: ReactNode }) {
    const [detailsOpen, showDetails] = useState(false);

    return (
        <ConnectionStatusContext
            value={{
                ...reading,
                attempts: 0,
                retry: () => undefined,
                detailsOpen: detailsOpen && reading.level === 'disconnected',
                showDetails,
                disconnectedNow: () => reading.level === 'disconnected',
            }}
        >
            {children}
        </ConnectionStatusContext>
    );
}

function renderOverflow(
    spaces: readonly Space[] = ['tasks', 'calendar'],
    current: Space | null = null,
    connection: ConnectionReading = { level: 'online' },
): { readonly current: ScreenLayers } {
    const { result } = renderHook(() => useScreenLayerStack());

    render(
        <LocalizationProvider>
            <ScreenLayersContext value={result.current}>
                <Status reading={connection}>
                    <SpaceOverflow
                        spaces={spaces}
                        current={current}
                        account={<button>{handedTheAccount}</button>}
                        presence={null}
                        refresh={<button>{handedTheRefresh}</button>}
                        developer={<button>{handedTheDeveloper}</button>}
                    />
                </Status>
            </ScreenLayersContext>
        </LocalizationProvider>,
    );

    return result;
}

/**
 * The sheet as the platform would report it once it is open, which jsdom reports of nothing.
 *
 * `hidePopover` is what the shell closes it by and jsdom declares neither it nor `showPopover`, so the one the sheet
 * would have is recorded; the toggle is the platform saying the state changed, which is what the component reads
 * rather than holding an opinion of its own.
 */
function theSheetIsOpened(): ReturnType<typeof vi.fn> {
    const sheet = document.getElementById('space-overflow');
    const hidden = vi.fn();

    if (sheet === null) {
        throw new Error('The sheet was not drawn.');
    }

    Object.defineProperty(sheet, 'hidePopover', { configurable: true, value: hidden });

    // jsdom declares no `ToggleEvent`, so the one thing the component reads off it is put on an ordinary event: what
    // a popover reports is the state it moved to, and that is the whole of what is asked here.
    const opening = new Event('toggle');

    Object.defineProperty(opening, 'newState', { value: 'open' });

    act(() => {
        sheet.dispatchEvent(opening);
    });

    return hidden;
}

describe('SpaceOverflow', () => {
    it('is opened by a control named for what it holds, which is the platform’s own popover', () => {
        renderOverflow();

        const control = screen.getByRole('button', { name: 'More' });

        expect(control.getAttribute('popovertarget')).toBe('space-overflow');
        expect(document.getElementById('space-overflow')?.getAttribute('popover')).toBe('auto');
    });

    it('offers each space it was handed as a link, at the address the bar would have used', () => {
        renderOverflow(['tasks', 'calendar', 'people']);

        expect(
            screen.getAllByRole('link', { hidden: true }).map((row) => [row.textContent, row.getAttribute('href')]),
        ).toEqual([
            ['Tasks', '#/tasks'],
            ['Calendar', '#/calendar'],
            ['People', '#/people'],
        ]);
    });

    it('marks the row the reader is on, so the sheet says where they are rather than only where they may go', () => {
        renderOverflow(['tasks', 'calendar'], 'calendar');

        expect(screen.getByRole('link', { current: 'page', hidden: true }).textContent).toBe('Calendar');
    });

    it('marks its own control while what is on the screen is behind it, the bar drawing no row for that space', () => {
        renderOverflow(['tasks', 'calendar'], 'calendar');

        expect(screen.getByRole('button', { name: 'More' }).getAttribute('aria-current')).toBe('true');
    });

    it('leaves its own control unmarked while the reader is on a space the bar draws itself', () => {
        renderOverflow(['tasks', 'calendar'], 'mail');

        expect(screen.getByRole('button', { name: 'More' }).getAttribute('aria-current')).toBeNull();
    });

    it('stands the account at the foot of the sheet, after every place there is to go', () => {
        renderOverflow();

        const account = screen.getByRole('button', { name: handedTheAccount, hidden: true });
        const lastRow = screen.getAllByRole('link', { hidden: true }).at(-1);

        expect(document.getElementById('space-overflow')?.contains(account)).toBe(true);
        expect(lastRow?.compareDocumentPosition(account)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });

    it('stands the developer control it was handed in the sheet, after the places and before the account', () => {
        renderOverflow();

        const developer = screen.getByRole('button', { name: handedTheDeveloper, hidden: true });

        expect(document.getElementById('space-overflow')?.contains(developer)).toBe(true);
        expect(screen.getAllByRole('link', { hidden: true }).at(-1)?.compareDocumentPosition(developer)).toBe(
            Node.DOCUMENT_POSITION_FOLLOWING,
        );
        expect(
            developer.compareDocumentPosition(screen.getByRole('button', { name: handedTheAccount, hidden: true })),
        ).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });

    // The sheet stands over the screen, so the back gesture reaches it before it navigates and a change of destination
    // leaves none of it behind. Both are one registration, and this is what proves it was made at all.
    it('closes the sheet when the back gesture reaches it, rather than leaving it over the next screen', () => {
        const shell = renderOverflow();
        const hidden = theSheetIsOpened();

        expect(shell.current.depth).toBe(1);

        act(() => {
            shell.current.closeTop();
        });

        expect(hidden).toHaveBeenCalled();
    });

    it('names the tile and leads the sheet with the alarm while the server is out of reach, opening onto Retry', () => {
        renderOverflow(['tasks'], null, { level: 'disconnected', cause: 'unanswered', since: 0 });

        expect(screen.getByRole('button', { name: 'More - not connected to the server' })).toBeDefined();

        const row = screen.getByRole('button', { name: 'Not connected to the server.', hidden: true });

        expect(row.getAttribute('aria-expanded')).toBe('false');

        fireEvent.click(row);

        expect(row.getAttribute('aria-expanded')).toBe('true');
        expect(screen.getByRole('button', { name: 'Retry', hidden: true })).toBeDefined();
    });

    it('names the tile plainly and offers nothing to open while connected', () => {
        renderOverflow(['tasks'], null, { level: 'online' });

        expect(screen.getByRole('button', { name: 'More' })).toBeDefined();
        expect(screen.getByText('Connected to the server.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Connected to the server.', hidden: true })).toBeNull();
    });
});
