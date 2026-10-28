// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type ActedMessage } from '../mailboxActs/useMailboxActs';
import { ConnectionStatusContext, type ConnectionStatus } from '../shell/connectionStatus';
import { SpamControl } from './SpamControl';

// The control as a strip draws it, against a status that has or has not lost the server. What is proven is that a mark
// — a write — says why it cannot be made before it is pressed, rather than opening a chooser whose answer has no route.

const invoice: ActedMessage = {
    storedEmailId: 'message-1',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
    spam: null,
};

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
    const toggleSpam = vi.fn();

    render(
        <LocalizationProvider>
            <ConnectionStatusContext value={statusAt(level)}>
                <MailboxActsContext value={{ ...nothingActed, spamOffered: true, toggleSpam }}>
                    <SpamControl messages={[invoice]} shape="labelled" wording="mark" />
                </MailboxActsContext>
            </ConnectionStatusContext>
        </LocalizationProvider>,
    );

    return toggleSpam;
}

describe('SpamControl', () => {
    it('marks spam when pressed while the server answers', () => {
        const toggleSpam = drawn('online');

        fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));

        expect(toggleSpam).toHaveBeenCalledTimes(1);
    });

    it('says it cannot mark spam before it is pressed while the server is out of reach, and marks nothing', () => {
        const toggleSpam = drawn('disconnected');
        const refused = screen.getByRole('button', { name: 'Mark spam - no connection to the MailFathom server.' });

        expect(refused.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(refused);

        expect(toggleSpam).not.toHaveBeenCalled();
    });
});
