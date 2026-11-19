// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect } from 'react';
import { cleanup, fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailSpamVerdict } from '@mailfathom/client-backend';
import type { Composing } from '../composer/useComposing';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type ActedMessage, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { ListedMailContext, nothingListed } from '../messageList/useListedMail';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type OpenOutbox } from '../workspace/useWorkspace';
import { MailTasksContext, type MailTasks } from '../tasks/useMailTasks';
import { MailToolbar } from './MailToolbar';
import type { StripFit } from './useStripFit';

const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

const messageId = '00000000-0000-4000-8000-000000000000';

const place = {
    storedEmailId: messageId,
    account: 'work',
    folder: 'work-inbox',
    unread: false,
    flagged: false,
    spam: null,
};

function Opens({ selection, outbox }: { readonly selection: string | null; readonly outbox: OpenOutbox | null }) {
    const { revise } = useWorkspace();

    useEffect(() => {
        revise(outbox === null ? { selection } : { outbox });
    }, [revise, selection, outbox]);

    return null;
}

function drawToolbar(
    offered: boolean,
    selection: string | null = null,
    acts: MailboxActs = nothingActed,
    fit: StripFit = 'labelled',
    lead: { readonly openMailboxes: (() => void) | null } = { openMailboxes: null },
    outbox: OpenOutbox | null = null,
    tasks: MailTasks | null = null,
    placed: ActedMessage = place,
): { composed: ReturnType<typeof vi.fn> } {
    const composed = vi.fn();
    const composing: Composing = {
        offered,
        drafts: false,
        opening: null,
        filedAs: null,
        compose: composed,
        close: () => undefined,
    };

    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ListedMailContext value={{ ...nothingListed, placeOf: () => placed }}>
                    <MailboxActsContext value={acts}>
                        <ComposingContext value={composing}>
                            <Opens selection={selection} outbox={outbox} />
                            <MailTasksContext value={tasks}>
                                <MailToolbar strip={() => undefined} fit={fit} openMailboxes={lead.openMailboxes} />
                            </MailTasksContext>
                        </ComposingContext>
                    </MailboxActsContext>
                </ListedMailContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );

    return { composed };
}

function keepingTasks(makeTaskOn: MailTasks['makeTaskOn'] = () => undefined): MailTasks {
    return {
        session: { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' },
        transport: () => Promise.reject(new Error('Nothing is read here.')),
        threadsWithTasks: new Set(),
        makeTask: () => undefined,
        makeTaskOn,
        openTask: () => undefined,
        record: () => Promise.reject(new Error('Nothing is written here.')),
        follow: () => () => undefined,
    };
}

function actsOffering(performed: MailboxActs['perform']): MailboxActs {
    return { ...nothingActed, refusalOf: () => null, perform: performed };
}

// jsdom evaluates no media query, so the setup answers every one of them with `false` and this strip is drawn in the
// single-pane composition unless a test says otherwise. Stating a width is stating what `useTwoPanes` reads, which is
// a `min-width` query built from the token — so what is answered here is that query, and the pointer as the test says.
function theCompositionHasTwoPanes(finger = false): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.includes('pointer: coarse') ? finger : query.includes('min-width'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

describe('MailToolbar', () => {
    it('opens a message of its own from the control the design puts first', () => {
        const { composed } = drawToolbar(true);

        fireEvent.click(screen.getByRole('button', { name: 'New message' }));

        expect(composed).toHaveBeenCalledWith({ kind: 'new' });
    });

    it.each([
        ['Reply', 'senderOnly'],
        ['Reply all', 'everyone'],
        ['Forward', 'forward'],
    ])('answers what is open with %s', (name, answers) => {
        const { composed } = drawToolbar(true, messageId);

        fireEvent.click(screen.getByRole('button', { name }));

        expect(composed).toHaveBeenCalledWith({ kind: 'answer', answers, storedEmailId: messageId });
    });

    it('draws answering as what it will be while nothing is open, rather than moving under the cursor', () => {
        const { composed } = drawToolbar(true);

        fireEvent.click(screen.getByRole('button', { name: 'Reply - not built yet' }));

        expect(composed).not.toHaveBeenCalled();
    });

    it('offers no writing at all to a credential that may not file a draft', () => {
        const { composed } = drawToolbar(false, messageId);

        fireEvent.click(screen.getByRole('button', { name: 'New message - not built yet' }));

        expect(composed).not.toHaveBeenCalled();
    });

    it('says in each control’s own name why a credential without the grant cannot change a mailbox', () => {
        drawToolbar(true, messageId);

        for (const action of ['Archive', 'Delete', 'Flag', 'Mark unread', 'Move']) {
            expect(
                screen.getByRole('button', {
                    name: `${action} - this credential may not change mail on your mail server.`,
                }),
            ).toBeDefined();
        }
    });

    it.each([
        ['Archive', 'archive'],
        ['Flag', 'flag'],
        ['Mark unread', 'markUnread'],
    ])('acts on what is open when %s is pressed', (name, act) => {
        const performed = vi.fn();

        drawToolbar(true, messageId, actsOffering(performed));

        fireEvent.click(screen.getByRole('button', { name }));

        expect(performed).toHaveBeenCalledWith(act, [place]);
    });

    it('asks before deleting rather than deleting on the press, which is the one act that is asked about', () => {
        const performed = vi.fn();

        drawToolbar(true, messageId, actsOffering(performed));

        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(performed).not.toHaveBeenCalled();
        expect(screen.getByRole('heading', { name: 'Delete 1 message?' })).toBeDefined();
    });

    it('draws each control as its symbol alone once the names no longer fit the strip', () => {
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'symbols');

        for (const name of ['Reply', 'Archive']) {
            expect(screen.getByRole('button', { name }).textContent).toBe('');
        }
    });

    it('gives every control its name in words while the names fit the strip', () => {
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        for (const name of ['New message', 'Reply', 'Archive']) {
            expect(screen.getByRole('button', { name }).textContent).toBe(name);
        }
    });

    it('keeps hiding the panels apart at the far edge as a symbol alone, whatever fits the strip', () => {
        theCompositionHasTwoPanes();
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        const hiding = screen.getByRole('button', {
            name: 'Hide the panels - the correspondence alone',
        });

        expect(hiding.textContent).toBe('');
        expect(hiding.className).toContain('ms-auto');
    });

    it('hides the panels when pressed and says so, so the state is readable rather than only visible', () => {
        theCompositionHasTwoPanes();
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        const hiding = screen.getByRole('button', { name: 'Hide the panels - the correspondence alone' });
        expect(hiding.getAttribute('aria-pressed')).toBe('false');

        fireEvent.click(hiding);

        const showing = screen.getByRole('button', { name: "Show the thread's panels" });
        expect(showing.getAttribute('aria-pressed')).toBe('true');
    });

    it('takes More actions over from the head once the panels are hidden at the desktop composition', () => {
        theCompositionHasTwoPanes();
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        expect(screen.queryByRole('button', { name: /^More actions/u })).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Hide the panels - the correspondence alone' }));

        expect(screen.getByRole('button', { name: /^More actions/u })).toBeDefined();
    });

    // Two panes under a finger leave the head's acts to this strip, so it carries More actions there whatever the
    // panels do; a mouse keeps it in the head, as the test above shows.
    it('takes More actions over from the head at two panes under a finger', () => {
        theCompositionHasTwoPanes(true);
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        expect(screen.getByRole('button', { name: /^More actions/u })).toBeDefined();
    });

    // Where two panes under a finger leave the head its subject alone, Make a task stands here instead, about the
    // thread of the message open, which the strip reads on the press since it holds only its identity.
    it('takes Make a task over from the head at two panes under a finger, about the open message', () => {
        const madeOn = vi.fn();
        const tasks = keepingTasks(madeOn);

        theCompositionHasTwoPanes(true);
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled', { openMailboxes: null }, null, tasks);
        fireEvent.click(screen.getByRole('button', { name: 'Make a task' }));

        expect(madeOn).toHaveBeenCalledWith(messageId);

        cleanup();
        theCompositionHasTwoPanes(false);
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled', { openMailboxes: null }, null, tasks);

        expect(screen.getByRole('button', { name: 'Reply' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Make a task' })).toBeNull();
    });

    // A message the deployment threads with nothing has no thread for a task to belong to, so nothing offers one.
    it('offers no Make a task for a message that stands in no thread', () => {
        theCompositionHasTwoPanes(true);
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled', { openMailboxes: null }, null, keepingTasks(), {
            ...place,
            threadId: null,
        });

        expect(screen.getByRole('button', { name: 'Reply' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Make a task' })).toBeNull();
    });

    it('leads with the control that hides the mailbox column wherever the column stands beside the list', () => {
        theCompositionHasTwoPanes();
        drawToolbar(true);

        const buttons = screen.getAllByRole('button');

        expect(buttons[0]?.getAttribute('aria-label')).toBe('Hide folders and filters');
        expect(buttons[1]?.textContent).toBe('New message');
    });

    it('hides the mailbox column and offers to show it again from the same place', () => {
        theCompositionHasTwoPanes();
        drawToolbar(true);

        fireEvent.click(screen.getByRole('button', { name: 'Hide folders and filters' }));

        expect(screen.getByRole('button', { name: 'Show folders and filters' })).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Hide folders and filters' })).toBeNull();
    });

    it('offers no column control where the mailboxes are a drawer, which has a close control of its own', () => {
        drawToolbar(true);

        expect(screen.queryByRole('button', { name: 'Hide folders and filters' })).toBeNull();
    });

    it('leads with the drawer where the mailboxes are a drawer, ahead of composing', () => {
        const opened = vi.fn();
        drawToolbar(true, messageId, nothingActed, 'labelled', { openMailboxes: opened });

        const buttons = screen.getAllByRole('button');
        fireEvent.click(screen.getByRole('button', { name: 'Folders and filters' }));

        expect(buttons.slice(0, 2).map((button) => button.getAttribute('aria-label') ?? button.textContent)).toEqual([
            'Folders and filters',
            'New message',
        ]);
        expect(opened).toHaveBeenCalledOnce();
    });

    it('leaves the panel control out of a single-pane composition, which the design gives it nothing to do in', () => {
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'labelled');

        expect(screen.queryByRole('button', { name: 'Hide the panels - the correspondence alone' })).toBeNull();
    });

    it('gives composing up to the floating control first, keeping the other names in words', () => {
        drawToolbar(true, messageId, actsOffering(vi.fn()), 'floating');

        expect(screen.queryByRole('button', { name: 'New message' })).toBeNull();
        expect(screen.getByRole('button', { name: 'Reply' }).textContent).toBe('Reply');
    });

    // An Outbox holds sends rather than messages in a folder, so while one is open the strip offers writing a new message
    // and nothing that would answer, file, flag, or delete — and no panels to hide, the Outbox drawing none.
    it('offers no mail act and no panel control while an Outbox stands in the list’s place', () => {
        theCompositionHasTwoPanes();
        drawToolbar(
            true,
            null,
            actsOffering(() => undefined),
            'labelled',
            { openMailboxes: null },
            { account: 'work', entry: null },
        );

        expect(
            screen.getAllByRole('button').map((control) => control.getAttribute('aria-label') ?? control.textContent),
        ).toStrictEqual(['Hide folders and filters', 'New message']);
    });

    it('asks about no message at all while nothing is open, rather than about the last one that was', () => {
        const asked = vi.fn(() => null);

        drawToolbar(true, null, { ...nothingActed, refusalOf: asked });

        expect(asked).toHaveBeenCalledWith('archive', []);
    });
});

// The person's own mark from this session is what the toolbar reads the open message's verdict through first, so the
// harness's one listed place stays the same message whether or not it is spam.
function actsWhereTheOpenMessageIsMarkedSpam(toggleSpam: MailboxActs['toggleSpam']): MailboxActs {
    const verdict: MailSpamVerdict = {
        verdict: 'Spam',
        decidedBy: 'Person',
        spamSubcategory: null,
        spamSubcategoryNamedBy: null,
        phishingRequest: null,
        impersonatedParty: null,
    };

    return {
        ...actsOffering(vi.fn()),
        spamOffered: true,
        toggleSpam,
        spamMarked: new Map([[messageId, { verdict, from: place.folder, leaves: false }]]),
    };
}

describe('MailToolbar, the spam control', () => {
    it('marks the open message spam from the control after its acts', () => {
        const toggleSpam = vi.fn();

        drawToolbar(true, messageId, { ...actsOffering(vi.fn()), spamOffered: true, toggleSpam });
        fireEvent.click(screen.getByRole('button', { name: 'Mark spam' }));

        expect(toggleSpam).toHaveBeenCalledWith([place], undefined);
    });

    it('draws Not spam where the three answers would stand over an open message marked spam', () => {
        const toggleSpam = vi.fn();

        drawToolbar(true, messageId, actsWhereTheOpenMessageIsMarkedSpam(toggleSpam));

        for (const name of ['Reply', 'Reply all', 'Forward', 'Mark spam']) {
            expect(screen.queryByRole('button', { name })).toBeNull();
        }

        fireEvent.click(screen.getByRole('button', { name: 'Not spam' }));

        expect(toggleSpam).toHaveBeenCalledWith([place], undefined);
    });
});
