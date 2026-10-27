// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientNotification } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadingZoneContext } from '../localization/useReadingZone';
import { NotificationRowMenu } from './NotificationRowMenu';

const mail: ClientNotification = {
    id: 'n-mail',
    kind: 'Mail',
    statement: null,
    title: 'Ada Lovelace wrote',
    body: 'About the engine',
    source: 'Inbox',
    target: { kind: 'Message', storedEmailId: 'm-9' },
    occurredAt: '2026-09-04T11:55:00Z',
    read: false,
};

describe('NotificationRowMenu', () => {
    // The row says how long ago, and the instant behind it is a mouse's hover there; a finger holding the row reads it
    // here instead, on the reader's own clock.
    it('says the instant the notification happened under its title', () => {
        render(
            <LocalizationProvider>
                <ReadingZoneContext value="Europe/Warsaw">
                    <NotificationRowMenu
                        notification={mail}
                        at={{ x: 20, y: 30 }}
                        onSelect={vi.fn()}
                        onToggleRead={vi.fn()}
                        onOpen={vi.fn()}
                        onSummarize={null}
                        decisions={[]}
                        onDelete={vi.fn()}
                        onClose={vi.fn()}
                    />
                </ReadingZoneContext>
            </LocalizationProvider>,
        );

        expect(screen.getByText(/^September 4, 2026.*1:55\sPM$/)).toBeDefined();
    });

    it('offers to summarize the messages a merged arrival stands for, and only where there is one', () => {
        const onSummarize = vi.fn();

        render(
            <LocalizationProvider>
                <NotificationRowMenu
                    notification={mail}
                    at={{ x: 20, y: 30 }}
                    onSelect={vi.fn()}
                    onToggleRead={vi.fn()}
                    onOpen={vi.fn()}
                    onSummarize={onSummarize}
                    decisions={[]}
                    onDelete={vi.fn()}
                    onClose={vi.fn()}
                />
            </LocalizationProvider>,
        );

        screen.getByRole('menuitem', { name: 'Summarize' }).click();

        expect(onSummarize).toHaveBeenCalledOnce();
    });
});
