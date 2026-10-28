// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionReading, type ConnectionStatus } from './connectionStatus';
import { ConnectionAlarm, ConnectionAnnouncement, ConnectionStatusLine } from './ConnectionStatusLine';

// The details word how long ago a cause began against the clock, so the clock is held still.
const now = Date.parse('2026-08-31T09:41:00+00:00');

beforeEach(() => {
    vi.useFakeTimers({ now });
});

afterEach(() => {
    vi.useRealTimers();
});

/** The status as the frame would hand it, with the details held the way the frame holds them. */
function Holding({
    reading,
    retry,
    attempts,
}: {
    readonly reading: ConnectionReading;
    readonly retry: () => void;
    readonly attempts: number | null;
}) {
    const [detailsOpen, showDetails] = useState(false);
    const status: ConnectionStatus = {
        ...reading,
        attempts,
        retry,
        detailsOpen: detailsOpen && (reading.level === 'delayed' || reading.level === 'disconnected'),
        showDetails,
        disconnectedNow: () => reading.level === 'disconnected',
    };

    return (
        <ConnectionStatusContext value={status}>
            <ConnectionAlarm />
            <ConnectionStatusLine place="menu" />
            <ConnectionAnnouncement />
        </ConnectionStatusContext>
    );
}

function draw(reading: ConnectionReading, retry: () => void = () => undefined, attempts: number | null = 1): void {
    render(
        <LocalizationProvider>
            <Holding reading={reading} retry={retry} attempts={attempts} />
        </LocalizationProvider>,
    );
}

describe('ConnectionStatusLine', () => {
    it.each([
        [{ level: 'connecting' } as const, 'Connecting to the server…'],
        [{ level: 'online' } as const, 'Connected to the server.'],
    ])('says each settled level in words and offers nothing to open', (reading, sentence) => {
        draw(reading);

        expect(screen.getAllByText(sentence)).toHaveLength(2);
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('announces the level wherever it changes, whether or not the menu is open', () => {
        draw({ level: 'online' });

        expect(screen.getByRole('status').textContent).toBe('Connected to the server.');
    });

    it('opens a delay onto every cause, naming the account by its name', () => {
        draw({
            level: 'delayed',
            delays: [
                { kind: 'channel', since: now - 40_000 },
                { kind: 'mailServer', account: 'Work', since: now - 120_000 },
            ],
        });

        const row = screen.getByRole('button', { name: 'Connected, with a delay.' });

        expect(row.getAttribute('aria-expanded')).toBe('false');

        fireEvent.click(row);

        expect(row.getAttribute('aria-expanded')).toBe('true');
        expect(screen.getByText(/Live updates from the server are not arriving/u)).toBeDefined();
        expect(screen.getByText(/^Work cannot reach its mail server\./u)).toBeDefined();
        expect(screen.getByText('Started 2 minutes ago.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Retry' })).toBeNull();
    });

    it('opens a lost server onto the attempt it is on and Retry, which reads everything again', () => {
        const retry = vi.fn();

        draw({ level: 'disconnected', cause: 'unanswered', since: now }, retry);
        fireEvent.click(screen.getByRole('button', { name: 'Not connected to the server.' }));

        expect(screen.getByText('The server did not answer.')).toBeDefined();
        expect(screen.getByText('Trying again - attempt 2 of 5.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

        expect(retry).toHaveBeenCalledOnce();
    });

    it('counts no attempts for a server whose ticket went unanswered while the session read was answered', () => {
        draw({ level: 'disconnected', cause: 'unanswered', since: now }, () => undefined, null);
        fireEvent.click(screen.getByRole('button', { name: 'Not connected to the server.' }));

        expect(screen.getByText('The server did not answer.')).toBeDefined();
        expect(screen.queryByText(/attempt/u)).toBeNull();
    });
});

describe('ConnectionAlarm', () => {
    it('stands only while the server is out of reach', () => {
        draw({ level: 'delayed', delays: [{ kind: 'channel', since: now }] });

        expect(screen.queryByRole('button', { name: 'Not connected to the server' })).toBeNull();
    });

    it('opens onto the details, by name rather than by colour', () => {
        draw({ level: 'disconnected', cause: 'offline', since: now });

        fireEvent.click(screen.getByRole('button', { name: 'Not connected to the server' }));

        expect(screen.getByRole('button', { name: 'Not connected to the server.' }).getAttribute('aria-expanded')).toBe(
            'true',
        );
        expect(screen.getByText(/This machine is offline\./u)).toBeDefined();
    });
});
