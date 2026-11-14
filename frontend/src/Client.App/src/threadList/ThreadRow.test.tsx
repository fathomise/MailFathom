// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ListedMailThread } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ExternalMarkingContext } from '../senderMark/externalMark';
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
        categories: [],
        title,
        ...overrides,
    };
}

function drawing(listed: ListedMailThread, onOpen: () => void = () => undefined): HTMLElement {
    render(
        <LocalizationProvider>
            <ul>
                <ThreadRow
                    thread={listed}
                    open={false}
                    selected={false}
                    selecting={false}
                    reading={false}
                    onOpen={onOpen}
                    onToggle={() => undefined}
                    onMenu={() => undefined}
                    onElement={() => undefined}
                />
            </ul>
        </LocalizationProvider>,
    );

    return screen.getByRole('button');
}

describe('ThreadRow, the newest message local only', () => {
    it('draws one mark named Local only', () => {
        const row = drawing(thread({ newest: { ...thread().newest, localOnly: true } }));

        expect(within(row).getAllByText('Local only')).toHaveLength(1);
    });

    it('draws none on a message the mail server still holds', () => {
        expect(within(drawing(thread())).queryByText('Local only')).toBeNull();
    });
});

describe('ThreadRow, the External chip', () => {
    const reason =
        'External - not signed by Nordwind. It was not sent from a Nordwind mail account through MailFathom. ' +
        'A colleague writing from another mail application is marked the same way.';
    const received = {
        authorAuthentication: 'Authenticated',
        deploymentTrust: 'Unknown',
        signatureOutcome: 'NotSigned',
        signerMatchesAuthor: false,
        trustSource: null,
        trustMember: null,
    } as const;

    function drawingMarked(listed: ListedMailThread, marked: readonly string[] = ['work']): HTMLElement {
        render(
            <LocalizationProvider>
                <ExternalMarkingContext value={{ accounts: new Set(marked), organization: 'Nordwind' }}>
                    <ul>
                        <ThreadRow
                            thread={listed}
                            open={false}
                            selected={false}
                            selecting={false}
                            reading={false}
                            onOpen={() => undefined}
                            onToggle={() => undefined}
                            onMenu={() => undefined}
                            onElement={() => undefined}
                        />
                    </ul>
                </ExternalMarkingContext>
            </LocalizationProvider>,
        );

        return screen.getByRole('button');
    }

    it('says its reason in the row’s name where the newest received message is not the organization’s', () => {
        const row = drawingMarked(thread({ newestReceivedSender: received }));

        expect(row.textContent).toContain(reason);
    });

    it.each([
        [
            'the organization signed the newest received message',
            thread({ newestReceivedSender: { ...received, trustSource: 'OrganizationMember' } }),
            ['work'],
        ],
        ['the account does not mark', thread({ newestReceivedSender: received }), []],
        ['the conversation holds nothing received', thread(), ['work']],
    ] as const)('draws none where %s', (_, listed, marked) => {
        expect(drawingMarked(listed, marked).textContent).not.toContain('External');
    });
});

describe('ThreadRow, what sort of mail it holds', () => {
    const categories: ListedMailThread['categories'] = [
        { identity: 'finance', name: null },
        { identity: 'Legal', name: 'Legal' },
        { identity: 'Budget 2027', name: 'Budget 2027' },
    ];

    it('draws two names and how many more', () => {
        const row = drawing(thread({ categories }));

        expect(row.textContent).toContain('Finance');
        expect(row.textContent).toContain('Legal');
        expect(row.textContent).toContain('+1');
    });

    it('draws nothing where only a default no label is drawn for stands', () => {
        const row = drawing(thread({ categories: [{ identity: 'other', name: null }] }));

        expect(row.textContent).not.toContain('Other');
    });
});

describe('ThreadRow', () => {
    it('names who took part, how many messages it holds, and quotes the newest with who wrote it', () => {
        const row = drawing(thread());

        expect(row.textContent).toContain('Anna, me');
        expect(row.textContent).toContain('2 messages in this conversation');
        expect(row.textContent).toContain('Anna:The figures are attached.');
    });

    it('frames the title a model wrote, with where the conversation stands under its topic', () => {
        drawing(thread());

        expect(screen.getByText('AI title:')).toBeDefined();
        expect(screen.getByText('Quarterly figures for the audit')).toBeDefined();
        expect(screen.getByText('Anna is waiting for the signed copy.')).toBeDefined();
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

describe('ThreadRow, picked out and asked what it offers', () => {
    function drawingPicked(selecting: boolean): {
        readonly row: HTMLElement;
        readonly opened: ReturnType<typeof vi.fn>;
        readonly toggled: ReturnType<typeof vi.fn>;
        readonly asked: ReturnType<typeof vi.fn>;
    } {
        const opened = vi.fn();
        const toggled = vi.fn();
        const asked = vi.fn();

        render(
            <LocalizationProvider>
                <ul>
                    <ThreadRow
                        thread={thread()}
                        open={false}
                        selected={selecting}
                        selecting={selecting}
                        reading={false}
                        onOpen={opened}
                        onToggle={toggled}
                        onMenu={asked}
                        onElement={() => undefined}
                    />
                </ul>
            </LocalizationProvider>,
        );

        return { row: screen.getByRole('button'), opened, toggled, asked };
    }

    it('opens its menu where the pointer asked for it, and does not open the conversation', () => {
        const { row, opened, asked } = drawingPicked(false);

        fireEvent.contextMenu(row, { clientX: 40, clientY: 60 });

        expect(asked).toHaveBeenCalledWith({ x: 40, y: 60 });
        expect(opened).not.toHaveBeenCalled();
    });

    it('is picked out by a modified press rather than opened', () => {
        const { row, opened, toggled } = drawingPicked(false);

        fireEvent.click(row, { ctrlKey: true });

        expect(toggled).toHaveBeenCalledTimes(1);
        expect(opened).not.toHaveBeenCalled();
    });

    it('is taken out of a selection by a plain press while one is held, and says it is picked out', () => {
        const { row, opened, toggled } = drawingPicked(true);

        expect(row.getAttribute('aria-pressed')).toBe('true');

        fireEvent.click(row);

        expect(toggled).toHaveBeenCalledTimes(1);
        expect(opened).not.toHaveBeenCalled();
    });
});

describe('ThreadRow, while an act asked of it reads its messages', () => {
    it('says the conversation is being read, on the row the act was asked of', () => {
        render(
            <LocalizationProvider>
                <ul>
                    <ThreadRow
                        thread={thread()}
                        open={false}
                        selected={false}
                        selecting={false}
                        reading
                        onOpen={() => undefined}
                        onToggle={() => undefined}
                        onMenu={() => undefined}
                        onElement={() => undefined}
                    />
                </ul>
            </LocalizationProvider>,
        );

        expect(screen.getByRole('listitem').getAttribute('aria-busy')).toBe('true');
        expect(screen.getByRole('status').textContent).toBe('Reading this conversation before changing it…');
    });
});
