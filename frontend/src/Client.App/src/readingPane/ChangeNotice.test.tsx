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
                <PendingChangesProvider session={null} abandoned={abandoned} onDecided={decided}>
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
    it('says which change the mailbox refused and what undoing it did, on the message it was asked of', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'flag', permanent: false }]);

        expect(screen.getByText('Not flagged - your mailbox refused this change')).toBeDefined();
        expect(
            screen.getByText(
                'The mail server would not take it, so MailFathom took the flag off again. It may take it if you ask again.',
            ),
        ).toBeDefined();
        expect(screen.getByRole('button', { name: 'Ask again' })).toBeDefined();
    });

    it('offers only letting go of a change the mail server refused for good, and says asking again would not help', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'flag', permanent: true }]);

        expect(
            screen.getByText(
                'The mail server refused it for good, so MailFathom took the flag off again. Asking again would not help.',
            ),
        ).toBeDefined();
        expect(screen.getByRole('button', { name: 'Let it go' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Ask again' })).toBeNull();
    });

    it('says nothing over a message no change is waiting on', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-other', act: 'flag', permanent: false }]);

        expect(screen.queryByText('Not flagged - your mailbox refused this change')).toBeNull();
    });

    it('asks the change again on the message as it stands, which answers it', () => {
        const { decided, perform } = drawing([
            { recordId: 'r-1', storedEmailId: 'e-1', act: 'flag', permanent: false },
        ]);

        fireEvent.click(screen.getByRole('button', { name: 'Ask again' }));

        expect(perform).toHaveBeenCalledExactlyOnceWith('flag', [message], undefined);
        expect(decided).toHaveBeenCalledExactlyOnceWith('r-1');
    });

    it('lets a change go without asking anything of the mailbox', () => {
        const { decided, perform } = drawing([
            { recordId: 'r-1', storedEmailId: 'e-1', act: 'delete', permanent: false },
        ]);

        fireEvent.click(screen.getByRole('button', { name: 'Let it go' }));

        expect(decided).toHaveBeenCalledExactlyOnceWith('r-1');
        expect(perform).not.toHaveBeenCalled();
    });

    it('offers a move again through the folder choice rather than guessing the folder', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'move', permanent: false }]);

        expect(screen.getByRole('button', { name: 'Move again…' })).toBeDefined();
    });

    it('offers only letting go of a change nothing here can ask again', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'copy', permanent: false }]);

        expect(screen.getByRole('button', { name: 'Let it go' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Ask again' })).toBeNull();
    });
});

describe('ChangeNotice, once answered', () => {
    it('puts focus on the message it stood over rather than leaving it with the control that went', () => {
        drawing([{ recordId: 'r-1', storedEmailId: 'e-1', act: 'delete', permanent: false }]);

        fireEvent.click(screen.getByRole('button', { name: 'Let it go' }));

        expect(document.activeElement).toBe(screen.getByRole('article'));
    });
});
