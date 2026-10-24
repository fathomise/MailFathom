// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { writeClientEvent } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ThemeProvider } from '../theme/Theme';
import { lockDeveloperInformation, pressesToUnlock, pressVersionLine } from './developerAccess';
import { DeveloperInformationButton } from './DeveloperInformationButton';
import { developerRecord } from './developerRecord';
import { DeveloperSections, type DeveloperContext } from './DeveloperSections';

const context: DeveloperContext = {
    deploymentAddress: 'https://mail.example',
    deploymentVersion: '0.9.0',
    permissions: ['mailfathom.mail.read'],
    telemetryLevel: 'debug',
    telemetryAllowed: true,
    person: 'person-4f2a',
    credential: { kind: 'session', expiresAt: '2026-10-24T03:00:00Z', issuer: null },
    reachable: true,
    synchronizationEnabled: true,
    accounts: [
        {
            id: 'account-91c7',
            displayName: 'anna.kowalska@example.com',
            synchronizationState: 'Synchronized',
            lastSynchronizedAt: null,
            behind: false,
        },
    ],
    space: 'mail',
};

function renderSections(): ReturnType<typeof render> {
    return render(
        <LocalizationProvider>
            <ThemeProvider>
                <DeveloperSections context={context} onClose={() => undefined} />
            </ThemeProvider>
        </LocalizationProvider>,
    );
}

afterEach(() => {
    lockDeveloperInformation();
    vi.restoreAllMocks();
});

describe('DeveloperSections', () => {
    it('shows the identifiers a developer looks a session up by, and never the name an account goes by', () => {
        renderSections();

        expect(screen.getByText('account-91c7')).toBeDefined();
        expect(screen.getByText('person-4f2a')).toBeDefined();
        expect(screen.getByText('mailfathom.mail.read')).toBeDefined();
        expect(screen.queryByText(/anna\.kowalska/u)).toBeNull();
    });

    it('moves between its sections with the arrow keys where they are tabs', () => {
        renderSections();

        const first = screen.getByRole('tab', { name: 'Signals' });

        fireEvent.keyDown(first, { key: 'ArrowRight' });

        expect(screen.getByRole('tab', { name: 'Requests' }).getAttribute('aria-selected')).toBe('true');
        expect(document.activeElement).toBe(screen.getByRole('tab', { name: 'Requests' }));

        fireEvent.keyDown(screen.getByRole('tab', { name: 'Requests' }), { key: 'End' });

        expect(screen.getByRole('tab', { name: 'Environment' }).getAttribute('aria-selected')).toBe('true');
    });

    it('watches only while it is shown', () => {
        const { unmount } = renderSections();

        act(() => {
            writeClientEvent({ event: 'signals_opened' });
        });

        expect(screen.getByText('opened')).toBeDefined();

        unmount();
        writeClientEvent({ event: 'signals_dropped' });

        expect(developerRecord().events).toEqual([]);
    });
});

describe('DeveloperInformationButton', () => {
    function renderButton(): void {
        render(
            <LocalizationProvider>
                <DeveloperInformationButton />
            </LocalizationProvider>,
        );
    }

    it('draws nothing until the version line has been pressed enough times', () => {
        renderButton();

        expect(screen.queryByRole('button', { name: 'Developer information' })).toBeNull();

        act(() => {
            for (let press = 0; press < pressesToUnlock; press += 1) {
                pressVersionLine();
            }
        });

        expect(screen.getByRole('button', { name: 'Developer information' }).getAttribute('aria-expanded')).toBe(
            'false',
        );
    });

    it('opens the surface in a window of its own under a mouse, and closes it on the next press', () => {
        const close = vi.fn();
        const opened = Object.assign(new EventTarget(), { close }) as unknown as Window;
        const open = vi.spyOn(window, 'open').mockReturnValue(opened);

        act(() => {
            for (let press = 0; press < pressesToUnlock; press += 1) {
                pressVersionLine();
            }
        });
        renderButton();

        fireEvent.click(screen.getByRole('button', { name: 'Developer information' }));

        expect(open).toHaveBeenCalledTimes(1);
        expect(screen.getByRole('button', { name: 'Developer information' }).getAttribute('aria-expanded')).toBe(
            'true',
        );

        fireEvent.click(screen.getByRole('button', { name: 'Developer information' }));

        expect(close).toHaveBeenCalledTimes(1);
    });
});
