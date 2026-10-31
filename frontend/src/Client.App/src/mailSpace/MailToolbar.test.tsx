// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect } from 'react';
import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { MailSpamVerdict } from '@mailfathom/client-backend';
import type { Composing } from '../composer/useComposing';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { ListedMailContext, nothingListed } from '../messageList/useListedMail';
import type { ListFold } from '../messageRows/listFold';
import { WorkspaceProvider } from '../workspace/Workspace';
import { useWorkspace, type OpenOutbox } from '../workspace/useWorkspace';
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
    lead: { readonly openMailboxes: (() => void) | null; readonly listFold: ListFold | null } = {
        openMailboxes: null,
        listFold: null,
    },
    outbox: OpenOutbox | null = null,
): { composed: ReturnType<typeof vi.fn> } {
    const composed = vi.fn();
    const composing: Composing = { offered, drafts: false, opening: null, compose: composed, close: () => undefined };

    render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <ListedMailContext value={{ ...nothingListed, placeOf: () => place }}>
                    <MailboxActsContext value={acts}>
                        <ComposingContext value={composing}>
                            <Opens selection={selection} outbox={outbox} />
                            <MailToolbar
                                strip={() => undefined}
                                fit={fit}
                                openMailboxes={lead.openMailboxes}
                                listFold={lead.listFold}
                            />
                        </ComposingContext>
                    </MailboxActsContext>
                </ListedMailContext>
            </WorkspaceProvider>
        </LocalizationProvider>,
    );

    return { composed };
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

    it('leads with the fold’s list control and then the drawer where the mailboxes are a drawer, ahead of composing', () => {
        const opened = vi.fn();
        drawToolbar(true, messageId, nothingActed, 'labelled', {
            openMailboxes: opened,
            listFold: { folded: true, kept: false, turn: vi.fn(), keep: vi.fn() },
        });

        const buttons = screen.getAllByRole('button');
        fireEvent.click(screen.getByRole('button', { name: 'Folders and filters' }));

        expect(buttons.slice(0, 3).map((button) => button.getAttribute('aria-label') ?? button.textContent)).toEqual([
            'Show the list',
            'Folders and filters',
            'New message',
        ]);
        expect(opened).toHaveBeenCalledOnce();
    });

    it('turns the fold’s list either way from one control, and keeps nothing open from the strip', () => {
        const fold = { kept: false, turn: vi.fn(), keep: vi.fn() };
        drawToolbar(true, messageId, nothingActed, 'labelled', {
            openMailboxes: null,
            listFold: { ...fold, folded: true },
        });

        fireEvent.click(screen.getByRole('button', { name: 'Show the list' }));
        fireEvent.contextMenu(screen.getByRole('button', { name: 'Show the list' }));

        expect(fold.turn).toHaveBeenCalledOnce();
        expect(fold.keep).not.toHaveBeenCalled();
    });

    it('keeps a widened list open on a hold, and names the hold on the control either way', () => {
        const fold = { folded: false, turn: vi.fn(), keep: vi.fn() };
        drawToolbar(true, messageId, nothingActed, 'labelled', {
            openMailboxes: null,
            listFold: { ...fold, kept: false },
        });
        const narrowing = screen.getByRole('button', { name: 'Narrow the list' });

        fireEvent.contextMenu(narrowing);

        expect(fold.keep).toHaveBeenCalledOnce();
        expect(describedBy(narrowing)).toBe('Narrow the list. Hold to keep it open as you open other messages.');
    });

    it('says how to let a list kept open fold again', () => {
        drawToolbar(true, messageId, nothingActed, 'labelled', {
            openMailboxes: null,
            listFold: { folded: false, kept: true, turn: vi.fn(), keep: vi.fn() },
        });
        const kept = screen.getByRole('button', { name: 'Narrow the list' });

        expect(describedBy(kept)).toBe(
            'The list stays open as you open other messages. Hold to let it fold again, or tap to narrow it.',
        );
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
            { openMailboxes: null, listFold: null },
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

function describedBy(element: HTMLElement): string | null {
    const id = element.getAttribute('aria-describedby');

    return id === null ? null : (document.getElementById(id)?.textContent ?? null);
}
