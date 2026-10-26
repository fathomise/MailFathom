// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type ActedMessage } from '../mailboxActs/useMailboxActs';
import { PendingChangesProvider } from '../pendingChanges/PendingChanges';
import type { AbandonedChange } from '../pendingChanges/usePendingChanges';
import { ToastsProvider } from '../toasts/Toasts';
import { ChangeNotice } from './ChangeNotice';

const message: ActedMessage = {
    storedEmailId: 'e-1',
    account: 'work',
    folder: 'INBOX',
    unread: false,
    flagged: false,
    spam: null,
};

function drawing(abandoned: readonly AbandonedChange[]) {
    const decided = vi.fn();
    const perform = vi.fn();

    render(
        <LocalizationProvider>
            <ToastsProvider>
                <PendingChangesProvider
                    session={null}
                    transport={() => Promise.reject(new Error('Nothing is read here.'))}
                    abandoned={abandoned}
                    onDecided={decided}
                >
                    <MailboxActsContext value={{ ...nothingActed, perform }}>
                        <article tabIndex={-1}>
                            <ChangeNotice message={message} />
                        </article>
                    </MailboxActsContext>
                </PendingChangesProvider>
            </ToastsProvider>
        </LocalizationProvider>,
    );

    return { decided, perform };
}

describe('ChangeNotice', () => {
    it('says which change the mailbox did not take, on the message it was asked of', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'flag' }]);

        expect(screen.getByText('Not flagged - your mailbox did not take this change')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Ask again' })).toBeDefined();
    });

    it('says nothing over a message no change is waiting on', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-other', act: 'flag' }]);

        expect(screen.queryByText('Not flagged - your mailbox did not take this change')).toBeNull();
    });

    it('asks the change again on the message as it stands, which answers it', () => {
        const { decided, perform } = drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'flag' }]);

        fireEvent.click(screen.getByRole('button', { name: 'Ask again' }));

        expect(perform).toHaveBeenCalledExactlyOnceWith('flag', [message], undefined);
        expect(decided).toHaveBeenCalledExactlyOnceWith('r-1');
    });

    it('lets a change go without asking anything of the mailbox', () => {
        const { decided, perform } = drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'delete' }]);

        fireEvent.click(screen.getByRole('button', { name: 'Let it go' }));

        expect(decided).toHaveBeenCalledExactlyOnceWith('r-1');
        expect(perform).not.toHaveBeenCalled();
    });

    it('offers a move again through the folder choice rather than guessing the folder', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'move' }]);

        expect(screen.getByRole('button', { name: 'Move again…' })).toBeDefined();
    });

    it('offers only letting go of a change nothing here can ask again', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'copy' }]);

        expect(screen.getByRole('button', { name: 'Let it go' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Ask again' })).toBeNull();
    });
});

describe('ChangeNotice, once answered', () => {
    it('puts focus on the message it stood over rather than leaving it with the control that went', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'delete' }]);

        fireEvent.click(screen.getByRole('button', { name: 'Let it go' }));

        expect(document.activeElement).toBe(screen.getByRole('article'));
    });
});
