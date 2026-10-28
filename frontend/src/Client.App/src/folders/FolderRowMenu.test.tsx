// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { FolderRowMenu } from './FolderRowMenu';

// The menu a folder row opens, against a status that has or has not lost the server. Every act it offers writes, so
// what is proven is that each stays in the menu saying why it cannot act, rather than opening something whose answer has
// no route.

function statusAt(level: 'online' | 'disconnected'): ConnectionStatus {
    const shared = {
        attempts: null,
        retry: () => undefined,
        detailsOpen: false,
        showDetails: () => undefined,
        disconnectedNow: () => level === 'disconnected',
    };

    return level === 'online' ? { level, ...shared } : { level, cause: 'unanswered', since: 0, ...shared };
}

function drawn(level: 'online' | 'disconnected'): ReturnType<typeof vi.fn> {
    const onAct = vi.fn();

    render(
        <LocalizationProvider>
            <ConnectionStatusContext value={statusAt(level)}>
                <FolderRowMenu
                    acts={['newFolderInside', 'markAllRead', 'deleteFolder']}
                    header="Projects"
                    at={{ x: 20, y: 30 }}
                    onAct={onAct}
                    onClose={() => undefined}
                />
            </ConnectionStatusContext>
        </LocalizationProvider>,
    );

    return onAct;
}

describe('FolderRowMenu', () => {
    it('takes the act chosen while the server answers', () => {
        const onAct = drawn('online');

        fireEvent.click(screen.getByRole('menuitem', { name: 'Mark all as read' }));

        expect(onAct).toHaveBeenCalledWith('markAllRead');
    });

    it('keeps every act in the menu saying it has no connection while the server is out of reach, and takes none', () => {
        const onAct = drawn('disconnected');
        const items = screen.getAllByRole('menuitem');

        expect(items.map((item) => item.getAttribute('aria-disabled'))).toEqual(['true', 'true', 'true']);
        expect(items.every((item) => item.textContent.endsWith('- no connection to the MailFathom server.'))).toBe(
            true,
        );

        fireEvent.click(items[1] ?? document.body);

        expect(onAct).not.toHaveBeenCalled();
    });
});
