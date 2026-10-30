// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { MailSpamVerdict } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActControls } from './MailboxActControls';
import type { ActRefusal, MoveDestination, MoveDestinationGroup } from './mailboxDestinations';
import { MailboxActsContext, nothingActed, type ActedMessage, type MailboxActs } from './useMailboxActs';

const invoice: ActedMessage = {
    storedEmailId: 'message-1',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
    spam: null,
};
const receipt: ActedMessage = {
    storedEmailId: 'message-2',
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
    spam: null,
};

const clients: MoveDestination = { folderId: 'work-clients', name: 'Projects / Clients', role: null };
const work: MoveDestinationGroup = {
    accountId: 'work',
    accountName: 'Northwind \u00b7 work',
    ordinal: 0,
    shared: false,
    destinations: [clients],
};

function drawControls(
    acts: Partial<MailboxActs> = {},
    messages: readonly ActedMessage[] = [invoice],
    onActed: () => void = () => undefined,
    spam: 'strip' | 'toolbar' | null = null,
): void {
    render(
        <LocalizationProvider>
            <MailboxActsContext value={{ ...nothingActed, refusalOf: () => null, ...acts }}>
                <MailboxActControls messages={messages} shape="labelled" spam={spam} onActed={onActed} />
            </MailboxActsContext>
        </LocalizationProvider>,
    );
}

describe('MailboxActControls', () => {
    it('draws the five acts in the order the design puts them, one strip’s worth for either strip', () => {
        drawControls();

        expect(
            screen
                .getAllByRole('button')
                .map((control) => control.textContent)
                .filter((said) => said !== ''),
        ).toStrictEqual(['Archive', 'Delete', 'Flag', 'Mark unread', 'Move']);
    });

    // The three that file a message happen on the press and report in a toast that offers the way back, which is the
    // design's rule: asking about every act is what teaches a reader to agree without reading.
    it.each([
        ['Archive', 'archive'],
        ['Flag', 'flag'],
        ['Mark unread', 'markUnread'],
    ] as const)('performs %s on the press rather than asking about it first', (name, act) => {
        const performed = vi.fn();
        const acted = vi.fn();

        drawControls({ perform: performed }, [invoice, receipt], acted);
        fireEvent.click(screen.getByRole('button', { name }));

        expect(performed).toHaveBeenCalledWith(act, [invoice, receipt]);
        expect(acted).toHaveBeenCalledOnce();
    });

    // One control, both directions. Where every message shares a state it offers the opposite of it, so the press does
    // something to every one of them; where they differ it marks read, which is the one act a mixed selection can all
    // take. The two messages above are read, which is why every other test here meets *Mark unread*.
    it('offers to mark read where every message is unread, and performs that rather than its opposite', () => {
        const performed = vi.fn();
        const unread = [
            { ...invoice, unread: true },
            { ...receipt, unread: true },
        ];

        drawControls({ perform: performed }, unread);

        expect(screen.queryByRole('button', { name: 'Mark unread' })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Mark read' }));

        expect(performed).toHaveBeenCalledWith('markRead', unread);
    });

    // The same one control read the same way: the design draws the flag slot as a toggle on the toolbar and in a row's
    // menu, so a strip standing over a flagged selection offers taking the flag off rather than putting a second one on.
    it('offers to take the flag off where every message is flagged, and performs that rather than its opposite', () => {
        const performed = vi.fn();
        const flagged = [
            { ...invoice, flagged: true },
            { ...receipt, flagged: true },
        ];

        drawControls({ perform: performed }, flagged);

        expect(screen.queryByRole('button', { name: 'Flag' })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Unflag' }));

        expect(performed).toHaveBeenCalledWith('unflag', flagged);
    });

    it('offers to put a flag on where the messages are a mixture, which is the one act all of them can take', () => {
        drawControls({}, [invoice, { ...receipt, flagged: true }]);

        expect(screen.getByRole('button', { name: 'Flag' })).toBeDefined();
    });

    it('offers to mark read where the messages are a mixture, which is the one act all of them can take', () => {
        drawControls({}, [invoice, { ...receipt, unread: true }]);

        expect(screen.getByRole('button', { name: 'Mark read' })).toBeDefined();
    });

    it('counts the messages in the question about deleting, and says both what it does and how long it can be undone', () => {
        drawControls({}, [invoice, receipt]);
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        const asked = screen.getByRole('dialog');

        expect(within(asked).getByRole('heading', { name: 'Delete 2 messages?' })).toBeDefined();
        expect(within(asked).getByText('Each one is filed in the trash folder of the account it is in.')).toBeDefined();
        expect(within(asked).getByText('You can take this back for 5 seconds afterwards.')).toBeDefined();
    });

    it('asks about destroying rather than filing where the messages are already in the trash, and offers no way back', () => {
        drawControls({ deletesPermanently: () => true }, [invoice, receipt]);
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        const asked = screen.getByRole('dialog');

        expect(within(asked).getByRole('heading', { name: 'Delete 2 messages?' })).toBeDefined();
        expect(
            within(asked).getByText(
                'Already in the trash - deleting now removes 2 messages together with attachments.',
            ),
        ).toBeDefined();
        expect(within(asked).getByText('This cannot be undone.')).toBeDefined();
        expect(within(asked).getByRole('button', { name: 'Delete' })).toBeDefined();
    });

    it('deletes nothing where the question was answered with the way back out of it', () => {
        const performed = vi.fn();
        const acted = vi.fn();

        drawControls({ perform: performed }, [invoice], acted);
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));
        fireEvent.click(screen.getByRole('button', { name: 'Cancel' }));

        expect(performed).not.toHaveBeenCalled();
        expect(acted).not.toHaveBeenCalled();
    });

    it('offers the folders the messages could go to, asked of the messages rather than of the mailbox', () => {
        const offered = vi.fn(() => [work]);

        drawControls({ destinationsOf: offered }, [invoice, receipt]);
        fireEvent.click(screen.getByRole('button', { name: 'Move' }));

        expect(offered).toHaveBeenCalledWith([invoice, receipt]);
        expect(screen.getByRole('button', { name: 'Projects / Clients' })).toBeDefined();
    });

    // Filing into the trash is deleting, so choosing it asks what Delete asks and then deletes, rather than filing the
    // mail away as a move with a toast and an animation of its own.
    it('asks the delete question where the trash is the folder chosen, and deletes once it is answered', () => {
        const performed = vi.fn();
        const trash: MoveDestination = { folderId: 'work-trash', name: 'Trash', role: 'Trash' };

        drawControls({ perform: performed, destinationsOf: () => [{ ...work, destinations: [trash, clients] }] });
        fireEvent.click(screen.getByRole('button', { name: 'Move' }));
        fireEvent.click(screen.getByRole('button', { name: 'Trash' }));

        const asked = screen.getByRole('dialog', { name: 'Delete 1 message?' });

        expect(performed).not.toHaveBeenCalled();

        fireEvent.click(within(asked).getByRole('button', { name: 'Move to the trash' }));

        expect(performed).toHaveBeenCalledWith('delete', [invoice]);
    });

    // The record is durable from the press and the account's pass carries it out minutes later, so the control has to
    // say the act is already happening rather than offer a submission the deployment would answer twice.
    it('says an act already asked of every message it is about is on its way, rather than offering it again', () => {
        const performed = vi.fn();

        drawControls({
            asked: new Map([['message-1', { act: 'archive', from: invoice.folder, leaves: true, destroys: false }]]),
            perform: performed,
        });

        const control = screen.getByRole('button', {
            name: 'Archive - this is already on its way to your mail server.',
        });

        fireEvent.click(control);

        expect(control.getAttribute('aria-disabled')).toBe('true');
        expect(performed).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Flag' })).toBeDefined();
    });

    it('goes on offering an act asked of only some of the messages, the rest of them not having it yet', () => {
        drawControls(
            {
                asked: new Map([
                    ['message-1', { act: 'archive', from: invoice.folder, leaves: true, destroys: false }],
                ]),
            },
            [invoice, receipt],
        );

        expect(screen.getByRole('button', { name: 'Archive' })).toBeDefined();
    });

    it.each([
        ['notOffered', 'Archive - this credential may not change mail on your mail server.'],
        ['nothingToActOn', 'Archive - nothing is open or selected for this to be about.'],
        ['noArchiveFolder', 'Archive - this account names no archive folder, so there is nowhere to archive to.'],
        ['severalAccounts', 'Archive - messages from several accounts cannot be filed into one folder.'],
        ['noOtherFolder', 'Archive - this account has no other folder to file into.'],
        ['foldersUnknown', 'Archive - MailFathom has not read your folders, so it cannot say where this would go.'],
        ['noConnection', 'Archive - no connection to the MailFathom server.'],
    ] as [ActRefusal, string][])(
        'says %s in the control’s own name rather than refusing after the press',
        (refusal, said) => {
            const performed = vi.fn();

            drawControls({ refusalOf: () => refusal, perform: performed });

            const control = screen.getByRole('button', { name: said });

            expect(control.getAttribute('aria-disabled')).toBe('true');

            fireEvent.click(control);

            expect(performed).not.toHaveBeenCalled();
        },
    );
});

describe('MailboxActControls, with the spam control', () => {
    const judged: MailSpamVerdict = {
        verdict: 'Spam',
        decidedBy: 'Deterministic',
        spamSubcategory: null,
        spamSubcategoryNamedBy: null,
        phishingRequest: null,
        impersonatedParty: null,
    };

    function labels(): (string | null)[] {
        return screen
            .getAllByRole('button')
            .map((control) => control.textContent)
            .filter((said) => said !== '');
    }

    it('draws Spam just before Move on a selection bar', () => {
        drawControls({ spamOffered: true }, [invoice], () => undefined, 'strip');

        expect(labels()).toStrictEqual(['Archive', 'Delete', 'Flag', 'Mark unread', 'Spam', 'Move']);
    });

    it('draws Mark spam after the five on the toolbar', () => {
        drawControls({ spamOffered: true }, [invoice], () => undefined, 'toolbar');

        expect(labels().at(-1)).toBe('Mark spam');
    });

    it('draws no spam control where the strip asks for none', () => {
        drawControls({ spamOffered: true });

        expect(screen.queryByRole('button', { name: /spam/iu })).toBeNull();
    });

    // What the strip is told once a mark is sent is handed on rather than run on the press, so a question cancelled
    // leaves the selection standing.
    it('turns to Not spam where every message is spam, and presses the one spam control over them', () => {
        const toggleSpam = vi.fn();
        const acted = vi.fn();
        const spam = [
            { ...invoice, spam: judged },
            { ...receipt, spam: judged },
        ];

        drawControls({ spamOffered: true, toggleSpam }, spam, acted, 'strip');
        fireEvent.click(screen.getByRole('button', { name: 'Not spam' }));

        expect(toggleSpam).toHaveBeenCalledWith(spam, acted);
        expect(acted).not.toHaveBeenCalled();
    });

    it('reads a selection mixing spam with other mail as the way in', () => {
        drawControls({ spamOffered: true }, [{ ...invoice, spam: judged }, receipt], () => undefined, 'strip');

        expect(screen.getByRole('button', { name: 'Spam' })).toBeDefined();
    });

    // On the toolbar *Not spam* stands where the answers would, so the strip of acts leaves it out.
    it('leaves Not spam out of the toolbar acts over an opened spam message', () => {
        drawControls({ spamOffered: true }, [{ ...invoice, spam: judged }], () => undefined, 'toolbar');

        expect(screen.queryByRole('button', { name: /spam/iu })).toBeNull();
    });
});
