// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { writeClientEvent } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ThemeProvider } from '../theme/Theme';
import { lockDeveloperInformation, pressesToUnlock, pressVersionLine } from './developerAccess';
import { DeveloperInformation } from './DeveloperInformation';
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

function renderSections(sideBySide = false, shown: DeveloperContext = context): ReturnType<typeof render> {
    return render(
        <LocalizationProvider>
            <ThemeProvider>
                <DeveloperSections context={shown} sideBySide={sideBySide} onClose={() => undefined} />
            </ThemeProvider>
        </LocalizationProvider>,
    );
}

afterEach(() => {
    lockDeveloperInformation();
    vi.restoreAllMocks();
});

function unlock(): void {
    act(() => {
        for (let press = 0; press < pressesToUnlock; press += 1) {
            pressVersionLine();
        }
    });
}

describe('DeveloperSections', () => {
    it('shows the identifiers a developer looks a session up by, and never the name an account goes by', () => {
        renderSections(true);

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

    it('stands every section side by side as a named region where there is room, with no tab anywhere', () => {
        renderSections(true);

        expect(screen.queryByRole('tablist')).toBeNull();
        expect(screen.queryByRole('tabpanel')).toBeNull();
        expect(screen.getByRole('region', { name: 'Signals' })).toBeDefined();
        expect(screen.getByRole('region', { name: 'Environment' })).toBeDefined();
    });

    it('draws an instant the deployment sent in a form it cannot read as absent rather than failing', () => {
        renderSections(true, {
            ...context,
            accounts: [
                {
                    id: 'account-91c7',
                    displayName: 'anna.kowalska@example.com',
                    synchronizationState: 'Synchronized',
                    lastSynchronizedAt: 'yesterday',
                    behind: false,
                },
            ],
        });

        expect(screen.getByText('account-91c7')).toBeDefined();
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

describe('DeveloperInformation', () => {
    function renderSurface(): void {
        render(
            <LocalizationProvider>
                <ThemeProvider>
                    <DeveloperInformationButton />
                    <DeveloperInformation context={context} />
                </ThemeProvider>
            </LocalizationProvider>,
        );
    }

    it('closes the window it stands in on Escape', () => {
        const into = document.implementation.createHTMLDocument('');
        const close = vi.fn();
        const opened = Object.assign(new EventTarget(), { document: into, close }) as unknown as Window;

        vi.spyOn(window, 'open').mockReturnValue(opened);
        unlock();
        renderSurface();
        fireEvent.click(screen.getByRole('button', { name: 'Developer information' }));

        expect(into.body.textContent).toContain('Signals');

        act(() => {
            into.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
        });

        expect(close).toHaveBeenCalledTimes(1);
        expect(screen.getByRole('button', { name: 'Developer information' }).getAttribute('aria-expanded')).toBe(
            'false',
        );
    });

    it('puts focus back on the control that opened it once it closes inside the page', () => {
        vi.spyOn(window, 'open').mockReturnValue(null);
        unlock();
        renderSurface();

        const opener = screen.getByRole('button', { name: 'Developer information' });

        opener.focus();
        fireEvent.click(opener);

        expect(screen.getByRole('dialog', { name: 'Developer information' })).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Close developer information' }));

        expect(screen.queryByRole('dialog', { name: 'Developer information' })).toBeNull();
        expect(document.activeElement).toBe(opener);
    });
});
