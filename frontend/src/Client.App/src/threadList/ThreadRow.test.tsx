// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ListedMailThread } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ThreadRow } from './ThreadRow';

const anna = { address: 'anna@example.invalid', displayName: 'Anna Nowak', reader: false };
const me = { address: 'me@example.invalid', displayName: 'Me Myself', reader: true };

const title = {
    topic: 'Quarterly figures for the audit',
    standing: 'Anna is waiting for the signed copy.',
    derivedFromMessageCount: 2,
    derivedFromLatestArrival: '2026-08-31T09:41:00+00:00',
};

function thread(overrides: Partial<ListedMailThread> = {}): ListedMailThread {
    return {
        threadId: 'a-conversation',
        account: 'work',
        messageCount: 2,
        unreadCount: 0,
        hasAttachments: false,
        participants: [anna, me],
        moreParticipantsNotNamed: false,
        newest: {
            id: 'two',
            subject: 'Re: The quarterly figures',
            receivedAt: '2026-08-31T09:41:00+00:00',
            senderAddress: anna.address,
            senderDisplayName: anna.displayName,
            preview: 'The figures are attached.',
            own: false,
            senderPhoto: null,
        },
        newestReceivedSender: null,
        title,
        ...overrides,
    };
}

function drawing(listed: ListedMailThread, onOpen: () => void = () => undefined): HTMLElement {
    render(
        <LocalizationProvider>
            <ul>
                <ThreadRow thread={listed} open={false} onOpen={onOpen} onElement={() => undefined} />
            </ul>
        </LocalizationProvider>,
    );

    return screen.getByRole('button');
}

describe('ThreadRow', () => {
    it('names who took part, how many messages it holds, and quotes the newest with who wrote it', () => {
        const row = drawing(thread());

        expect(row.textContent).toContain('Anna, me');
        expect(row.textContent).toContain('2 messages in this conversation');
        expect(row.textContent).toContain('Anna:The figures are attached.');
    });

    it('frames the title a model wrote, with where the conversation stands under its topic', () => {
        const row = drawing(thread());

        expect(screen.getByText('AI title:')).toBeDefined();
        expect(screen.getByText('Quarterly figures for the audit')).toBeDefined();
        expect(screen.getByText('Anna is waiting for the signed copy.')).toBeDefined();
        expect(row.closest('[data-thread-row]')?.getAttribute('data-thread-row')).toBe('standing');
    });

    it('stands under its subject, with no frame, where no derivation has named it', () => {
        drawing(thread({ title: null }));

        expect(screen.getByText('Re: The quarterly figures')).toBeDefined();
        expect(screen.queryByText('AI title:')).toBeNull();
    });

    it('draws the topic alone where nothing is left open', () => {
        drawing(thread({ title: { ...title, standing: '' } }));

        expect(screen.getByText('Quarterly figures for the audit')).toBeDefined();
        expect(screen.queryByText('Anna is waiting for the signed copy.')).toBeNull();
        expect(screen.queryByText('Updating with the new message…')).toBeNull();
    });

    it('keeps the topic and holds the standing back once a message joined since it was written', () => {
        drawing(thread({ messageCount: 3 }));

        expect(screen.getByText('Quarterly figures for the audit')).toBeDefined();
        expect(screen.getByText('Updating with the new message…')).toBeDefined();
        expect(screen.queryByText('Anna is waiting for the signed copy.')).toBeNull();
    });

    it('says a message in it carries attachments, and says nothing where none does', () => {
        drawing(thread({ hasAttachments: true }));

        expect(screen.getByText('Attachments in this thread')).toBeDefined();
    });

    it('draws no attachment mark where no message carries one', () => {
        drawing(thread());

        expect(screen.queryByText('Attachments in this thread')).toBeNull();
    });

    it('quotes the reader’s own newest message as theirs', () => {
        const row = drawing(thread({ newest: { ...thread().newest, own: true } }));

        expect(row.textContent).toContain('You:The figures are attached.');
    });

    it('stands the correspondent beside a conversation ending in the reader’s own reply, rather than nobody', () => {
        const row = drawing(thread({ newest: { ...thread().newest, own: true } }));

        expect(row.textContent).toMatch(/^AN/u);
    });

    it('says the conversation holds something unread', () => {
        drawing(thread({ unreadCount: 1 }));

        expect(screen.getByText('Unread')).toBeDefined();
    });

    it('carries no AI summary control, which the opened conversation carries instead', () => {
        drawing(thread());

        expect(screen.getAllByRole('button')).toHaveLength(1);
        expect(screen.queryByText('AI summary')).toBeNull();
    });

    it('opens the conversation in one press', () => {
        const onOpen = vi.fn();

        fireEvent.click(drawing(thread(), onOpen));

        expect(onOpen).toHaveBeenCalledOnce();
    });
});
