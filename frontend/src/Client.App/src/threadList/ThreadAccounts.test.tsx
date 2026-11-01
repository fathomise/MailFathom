// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type { MailAccount } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace } from '../workspace/useWorkspace';
import { ThreadAccounts } from './ThreadAccounts';

function account(id: string, displayName: string): MailAccount {
    return {
        id,
        displayName,
        synchronizationState: 'Synchronized',
        lastSynchronizedAt: null,
        behind: false,
        filesJunk: false,
        shared: false,
    };
}

const work = account('work', 'Work');
const home = account('home', 'Home');

// What the pane wrote, read back out of the workspace the list reads it from rather than out of the pane.
function PickedProbe() {
    const { workspace } = useWorkspace();

    return <output>{workspace.threadListAccount ?? null}</output>;
}

function drawing(accounts: readonly MailAccount[]): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ThreadAccounts accounts={accounts} />
                <PickedProbe />
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

describe('ThreadAccounts', () => {
    it('offers every account together and each account alone, and no folder', () => {
        drawing([work, home]);

        expect(screen.getAllByRole('button').map((button) => button.textContent)).toStrictEqual([
            'All accounts',
            'Work',
            'Home',
        ]);
        expect(screen.getByRole('button', { name: 'All accounts' }).getAttribute('aria-current')).toBe('true');
    });

    it('reads the list under the account picked, and back under every account', () => {
        drawing([work, home]);

        fireEvent.click(screen.getByRole('button', { name: 'Home' }));

        expect(screen.getByRole('status').textContent).toBe('home');
        expect(screen.getByRole('button', { name: 'Home' }).getAttribute('aria-current')).toBe('true');

        fireEvent.click(screen.getByRole('button', { name: 'All accounts' }));

        expect(screen.getByRole('status').textContent).toBe('');
    });

    it('offers the one account alone where only one is held, there being nothing to gather', () => {
        drawing([work]);

        expect(screen.getAllByRole('button').map((button) => button.textContent)).toStrictEqual(['Work']);
        expect(screen.getByRole('button', { name: 'Work' }).getAttribute('aria-current')).toBe('true');
    });
});
