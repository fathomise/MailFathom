// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientMailListMode } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { WorkspaceProvider } from '../workspace/Workspace';
import { ListModeSwitch } from './ListModeSwitch';

function drawing(mode: ClientMailListMode, onChoose: (mode: ClientMailListMode) => void): void {
    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ListModeSwitch mode={mode} onChoose={onChoose} />
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

describe('ListModeSwitch', () => {
    it('says the list is read by message, and switches it to conversations', () => {
        const onChoose = vi.fn();

        drawing('messages', onChoose);
        fireEvent.click(screen.getByRole('button', { name: 'Messages, switch to threads' }));

        expect(onChoose).toHaveBeenCalledWith('threads');
    });

    it('says the list is read by conversation, and switches it back to messages', () => {
        const onChoose = vi.fn();

        drawing('threads', onChoose);
        fireEvent.click(screen.getByRole('button', { name: 'Threads, switch to messages' }));

        expect(onChoose).toHaveBeenCalledWith('messages');
    });
});
