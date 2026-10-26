// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, renderHook, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { ClientNotification, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { nothingPending, PendingChangesContext, type PendingChanges } from '../pendingChanges/usePendingChanges';
import { AgentHandOverContext, type AgentHandOver } from '../routing/agentHandOver';
import { ScreenLayersContext, useScreenLayerStack, type ScreenLayers } from '../shell/screenLayers';
import { ToastsProvider } from '../toasts/Toasts';
import { NotificationCentre } from './NotificationCentre';
import type { NotificationCentre as Centre } from './useNotificationCentre';
import type { PanelSwipe } from './usePanelSwipe';

// The panel is driven by a centre it is handed rather than by one that reads a deployment, because what is being
// proven here is what a person sees and what pressing something asks for. What the asking then does on the wire is
// `useNotificationCentre.test.tsx`.

const now = new Date('2026-09-04T12:00:00Z');

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

const meeting: ClientNotification = {
    id: 'n-meeting',
    kind: 'Calendar',
    statement: null,
    title: 'Standing meeting moved',
    body: 'It is an hour later',
    source: null,
    target: { kind: 'Nothing' },
    occurredAt: '2026-09-03T09:00:00Z',
    read: true,
};

const swipe: PanelSwipe = {
    offset: null,
    dragging: false,
    springing: false,
    attachPanel: () => undefined,
    attachList: () => undefined,
    onNavigationPointerDown: () => undefined,
    onNavigationClickCapture: () => undefined,
    onPanelPointerDown: () => undefined,
};

const acts = {
    hide: vi.fn(),
    markRead: vi.fn(),
    markAllRead: vi.fn(),
    remove: vi.fn(),
    follow: vi.fn(),
    show: vi.fn(),
    changeDecided: vi.fn(),
};

function centre(held: Partial<Centre> = {}): Centre {
    return {
        unreadCount: 1,
        shown: true,
        notifications: [mail, meeting],
        arrived: new Set<string>(),
        reading: false,
        failure: null,
        ...acts,
        ...held,
    };
}

function panel(
    held: Partial<Centre> = {},
    handToAgent: ((handOver: AgentHandOver) => void) | null = null,
    onDecideSend: (outgoingEmailId: string, decision: 'again' | 'edit', onTaken: () => void) => void = () => undefined,
    pending: PendingChanges = nothingPending,
    reading: { readonly session: ClientSession; readonly transport: MailFathomTransport } | null = null,
): { readonly current: ScreenLayers } {
    // The shell around it, because the panel records itself as standing over the screen while it is shown. Its three
    // functions are the same ones for the life of the stack, so the value handed to the provider stays current
    // however the count moves.
    const { result } = renderHook(() => useScreenLayerStack());

    render(
        <LocalizationProvider>
            <ToastsProvider>
                <PendingChangesContext value={pending}>
                    <AgentHandOverContext value={handToAgent}>
                        <ScreenLayersContext value={result.current}>
                            <NotificationCentre
                                centre={centre(held)}
                                swipe={swipe}
                                session={reading?.session ?? null}
                                {...(reading === null ? {} : { transport: reading.transport })}
                                onDecideSend={onDecideSend}
                            />
                        </ScreenLayersContext>
                    </AgentHandOverContext>
                </PendingChangesContext>
            </ToastsProvider>
        </LocalizationProvider>,
    );

    return result;
}

/** Moves the panel off the tab it opens on, so a row already read is drawn as well. */
function showAll(): void {
    fireEvent.click(screen.getByRole('radio', { name: 'All' }));
}

/** The row naming this notification, which is the list item its title sits in. */
function rowFor(title: string): HTMLElement {
    const heading = screen.getByText(title);
    const row = heading.closest('li');

    if (row === null) {
        throw new Error(`No row draws ${title}.`);
    }

    return row;
}

beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(now);
});

afterEach(() => {
    vi.useRealTimers();
    vi.clearAllMocks();
});

describe('NotificationCentre', () => {
    it('opens as a dialog named for what it holds, so it is one thing a reader can move into', () => {
        panel();

        expect(screen.getByRole('dialog', { name: 'Notifications' })).toBeDefined();
    });

    it('stays off the screen while the centre is not being shown', () => {
        panel({ shown: false });

        expect(screen.queryByRole('dialog', { name: 'Notifications' })).toBeNull();
    });

    // `translate-x-*` and `translate-y-*` write the one `translate` property, so a class list that leaves both on the
    // panel at once composes a diagonal — which is the travel a reader sees as a slide out of the lower corner rather
    // than the one axis the design draws for each composition. Nothing in jsdom applies a stylesheet, so what
    // is asserted is that every displacement the panel carries is gated on exactly one of the two compositions.
    it('travels along one axis in each composition: up from under a phone, in from the side beside a rail', () => {
        panel();

        const displaced = [...screen.getByRole('dialog', { name: 'Notifications' }).classList].filter((name) =>
            name.includes('translate-'),
        );

        expect(displaced.length).toBeGreaterThan(0);
        expect(
            displaced.filter((name) => name.startsWith('max-workspace:')).every((name) => name.includes('-y-')),
        ).toBe(true);
        expect(displaced.filter((name) => name.startsWith('workspace:')).every((name) => name.includes('-x-'))).toBe(
            true,
        );
        expect(displaced.every((name) => name.startsWith('max-workspace:') || name.startsWith('workspace:'))).toBe(
            true,
        );
    });

    describe('a merged arrival', () => {
        const merged: ClientNotification = {
            ...mail,
            id: 'n-merged',
            statement: { cause: 'MailArrived', counted: 8, outOf: null },
            title: 'New mail',
            body: '8 new messages arrived.',
            source: 'Fabrikam',
            target: { kind: 'Screen', screen: 'Mail' },
        };

        it('marks itself read, leaves the panel, and hands the Agent a question about its messages', () => {
            const handToAgent = vi.fn<(handOver: AgentHandOver) => void>();

            panel({ notifications: [merged] }, handToAgent);
            fireEvent.click(within(rowFor('8 new messages')).getByRole('button', { name: 'Summarize' }));

            expect(acts.markRead).toHaveBeenCalledWith(['n-merged'], true);
            expect(acts.hide).toHaveBeenCalledOnce();
            expect(handToAgent).toHaveBeenCalledWith({
                scope: { kind: 'notification', subject: 'n-merged' },
                title: '8 new messages · Fabrikam',
                question: 'Summarize these 8 new messages',
            });
        });

        it('offers no summary where nothing can take one, nor on a row about one message', () => {
            panel({ notifications: [merged, mail] });

            expect(screen.queryByRole('button', { name: 'Summarize' })).toBeNull();
        });

        it('offers a summary on no row about one message, whatever can take one', () => {
            panel({ notifications: [mail] }, vi.fn());

            expect(screen.queryByRole('button', { name: 'Summarize' })).toBeNull();
        });
    });

    it('says how many arrived beside its own title', () => {
        panel({ unreadCount: 4 });

        expect(screen.getByText('4 new')).toBeDefined();
    });

    // The standing rule for every list in this client: a centre already drawn is not redrawn as a centre appearing,
    // so what a poll brings back moves the row it brought and leaves the rows beside it where they were.
    it('opens the row that arrived, and only that one', () => {
        panel({ arrived: new Set(['n-mail']) });
        showAll();

        expect([...rowFor('Ada Lovelace wrote').classList]).toContain('animate-row-opening');
        expect([...rowFor('Standing meeting moved').classList]).not.toContain('animate-row-opening');
    });

    it('opens on what stands unread, the first of its two tabs, and says how much that is on the tab itself', () => {
        panel();

        const tabs = screen.getAllByRole('radio');

        expect(tabs.map((tab) => tab.closest('label')?.textContent)).toEqual(['Unread · 1', 'All']);
        expect(screen.getByRole('radio', { name: 'Unread · 1' })).toHaveProperty('checked', true);
        expect(screen.getAllByRole('listitem')).toHaveLength(1);
        expect(screen.getByText('Ada Lovelace wrote')).toBeDefined();
    });

    it('draws every notification on the tab that shows all of them', () => {
        panel();
        showAll();

        expect(screen.getAllByRole('listitem')).toHaveLength(2);
    });

    it('words how long ago a notification happened, rather than when it did', () => {
        panel();

        expect(rowFor('Ada Lovelace wrote').textContent).toContain('5 min. ago');
    });

    it('carries the instant itself behind the wording, which is what the machine-readable form is for', () => {
        panel();

        const when = rowFor('Ada Lovelace wrote').querySelector('time');

        expect(when?.getAttribute('datetime')).toBe('2026-09-04T11:55:00Z');
    });

    it('says what a notification says and where it came from', () => {
        panel();

        const row = rowFor('Ada Lovelace wrote');

        expect(row.textContent).toContain('About the engine');
        expect(row.textContent).toContain('Inbox');
    });

    it('falls back to the kind where a notification names no source of its own', () => {
        panel();
        showAll();

        expect(rowFor('Standing meeting moved').textContent).toContain('Calendar');
    });

    it('says in words that a row stands unread, for a reader who is looking at neither weight nor colour', () => {
        panel();
        showAll();

        expect(rowFor('Ada Lovelace wrote').textContent).toContain('Unread');
        expect(rowFor('Standing meeting moved').textContent).not.toContain('Unread');
    });

    it('marks one notification read from its own control, without opening it', () => {
        panel();

        fireEvent.click(screen.getByRole('button', { name: 'Mark as read' }));

        expect(acts.markRead).toHaveBeenCalledWith(['n-mail'], true);
        expect(acts.follow).not.toHaveBeenCalled();
        expect(acts.hide).not.toHaveBeenCalled();
    });

    it('marks a notification unread again from the same control, which says what it would do', () => {
        panel();
        showAll();

        fireEvent.click(screen.getByRole('button', { name: 'Mark as unread' }));

        expect(acts.markRead).toHaveBeenCalledWith(['n-meeting'], false);
    });

    describe('a card swiped aside', () => {
        /** A finger landing on the card's title, travelling across it, and lifting there. */
        function swipeAcross(title: string, across: number, pointerType = 'touch'): void {
            const card = screen.getByText(title);
            const landed = { pointerId: 1, pointerType, button: 0, clientX: 200, clientY: 100 };
            const travelled = { ...landed, clientX: 200 + across };

            fireEvent.pointerDown(card, landed);
            fireEvent.pointerMove(card, travelled);
            fireEvent.pointerUp(card, travelled);
        }

        it('marks an unread notification read, rightward as well as leftward, without opening it', () => {
            panel();

            swipeAcross('Ada Lovelace wrote', 120);
            swipeAcross('Ada Lovelace wrote', -120);

            expect(acts.markRead).toHaveBeenNthCalledWith(1, ['n-mail'], true);
            expect(acts.markRead).toHaveBeenNthCalledWith(2, ['n-mail'], true);
            expect(acts.follow).not.toHaveBeenCalled();
        });

        it('marks a read notification unread again, as the toggle beside it would', () => {
            panel();
            showAll();

            swipeAcross('Standing meeting moved', -120);

            expect(acts.markRead).toHaveBeenCalledWith(['n-meeting'], false);
        });

        it('springs back short of the threshold, leaving the read state as it was', () => {
            panel();

            swipeAcross('Ada Lovelace wrote', 60);

            expect(acts.markRead).not.toHaveBeenCalled();
        });

        it('is never carried by a mouse, which has the toggle and the menu for the same act', () => {
            panel();

            swipeAcross('Ada Lovelace wrote', 120, 'mouse');

            expect(acts.markRead).not.toHaveBeenCalled();
        });
    });

    it('marks the whole centre read in one act', () => {
        panel();

        fireEvent.click(screen.getByRole('button', { name: 'Mark all' }));

        expect(acts.markAllRead).toHaveBeenCalledOnce();
    });

    it('offers nothing to mark where nothing stands unread', () => {
        panel({ unreadCount: 0 });

        expect(screen.queryByRole('button', { name: 'Mark all' })).toBeNull();
    });

    it('goes where a notification leads when its row is pressed, and takes the panel off the screen with it', () => {
        panel();

        fireEvent.pointerDown(screen.getByText('Ada Lovelace wrote'), { pointerType: 'mouse', button: 0 });

        expect(acts.follow).toHaveBeenCalledWith(mail);
    });

    it('leaves the panel by its own control', () => {
        panel();

        fireEvent.click(screen.getByRole('button', { name: 'Close notifications' }));

        expect(acts.hide).toHaveBeenCalledOnce();
    });

    it('leaves the panel through the centre rather than round it when the dialog is cancelled', () => {
        panel();

        fireEvent(screen.getByRole('dialog'), new Event('cancel', { bubbles: false, cancelable: true }));

        expect(acts.hide).toHaveBeenCalledOnce();
    });

    it('opens on Unread again after it was closed on All, because the filter lasts only while the panel is open', () => {
        panel();
        showAll();

        fireEvent(screen.getByRole('dialog'), new Event('close'));

        expect(screen.getByRole('radio', { name: /^Unread/u })).toHaveProperty('checked', true);
        expect(screen.getByRole('radio', { name: 'All' })).toHaveProperty('checked', false);
    });

    it('says that nothing is waiting, rather than drawing an empty list', () => {
        panel({ notifications: [], unreadCount: 0 });

        expect(screen.getByText('Nothing new. All read.')).toBeDefined();
    });

    it('says there are no notifications at all, rather than that all were read, on the All tab of an empty centre', () => {
        panel({ notifications: [], unreadCount: 0 });

        showAll();

        expect(screen.getByText('No notifications yet.')).toBeDefined();
        expect(screen.queryByText('Nothing new. All read.')).toBeNull();
    });

    it('says it is reading while the first page is still in flight', () => {
        panel({ notifications: [], reading: true, unreadCount: 0 });

        expect(screen.getByRole('status').textContent).toBe('Reading what has happened…');
    });

    it('says what failed and what it means, rather than that something went wrong', () => {
        panel({ notifications: [], failure: 'unavailable', unreadCount: 0 });

        expect(screen.getByRole('alert').textContent).toContain('Your notifications could not be read');
    });

    it('picks a row out under a held modifier rather than opening it', () => {
        panel();

        fireEvent.pointerDown(screen.getByText('Ada Lovelace wrote'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });

        expect(acts.follow).not.toHaveBeenCalled();
        expect(screen.getByRole('toolbar', { name: 'Actions on the notifications selected' })).toBeDefined();
    });

    it('acts on everything picked out, in one request rather than one per row', () => {
        panel();
        showAll();

        fireEvent.pointerDown(screen.getByText('Ada Lovelace wrote'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });
        fireEvent.pointerDown(screen.getByText('Standing meeting moved'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });
        const bar = within(screen.getByRole('toolbar', { name: 'Actions on the notifications selected' }));

        expect(bar.getByRole('status').textContent).toBe('2 selected');

        fireEvent.click(bar.getByRole('button', { name: 'Mark as read' }));

        expect(acts.markRead).toHaveBeenCalledWith(['n-mail', 'n-meeting'], true);
    });

    // The confirmation stands over the panel that opened it, so a control is looked for inside the question rather than
    // on the screen: the selection bar behind it names the same act.
    function questionAsked(heading: string): HTMLElement {
        const dialog = screen.getByRole('heading', { name: heading }).closest('dialog');

        if (!(dialog instanceof HTMLElement)) {
            throw new Error(`No question was standing under the heading “${heading}”.`);
        }

        return dialog;
    }

    // A row that has gone does not come back, so the question stands in front of every delete and names the row rather
    // than counting it.
    it('asks about the row by name before taking it out of the centre', () => {
        panel();

        fireEvent.contextMenu(screen.getByText('Ada Lovelace wrote'));
        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete notification' }));

        expect(screen.getByRole('heading', { name: 'Delete notification?' })).toBeDefined();
        expect(screen.getByText('“Ada Lovelace wrote” will disappear from the notification centre.')).toBeDefined();
        expect(acts.remove).not.toHaveBeenCalled();
    });

    it('takes the row out once the question has been answered', () => {
        panel();

        fireEvent.contextMenu(screen.getByText('Ada Lovelace wrote'));
        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete notification' }));
        fireEvent.click(screen.getByRole('button', { name: 'Delete' }));

        expect(acts.remove).toHaveBeenCalledWith(['n-mail']);
    });

    it('leaves the centre as it was where the question is answered by keeping the row', () => {
        panel();

        fireEvent.contextMenu(screen.getByText('Ada Lovelace wrote'));
        fireEvent.click(screen.getByRole('menuitem', { name: 'Delete notification' }));
        fireEvent.click(screen.getByRole('button', { name: 'Keep it' }));

        expect(acts.remove).not.toHaveBeenCalled();
    });

    it('counts what a selection would take rather than naming one of the rows in it', () => {
        panel();
        showAll();

        fireEvent.pointerDown(screen.getByText('Ada Lovelace wrote'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });
        fireEvent.pointerDown(screen.getByText('Standing meeting moved'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });

        const bar = within(screen.getByRole('toolbar', { name: 'Actions on the notifications selected' }));
        fireEvent.click(bar.getByRole('button', { name: 'Delete' }));

        expect(screen.getByText('2 notifications will disappear from the centre.')).toBeDefined();

        // Scoped to the question rather than to the screen, because the bar that opened it is still behind it and both
        // controls are called what the act is called.
        fireEvent.click(within(questionAsked('Delete notifications?')).getByRole('button', { name: 'Delete' }));

        expect(acts.remove).toHaveBeenCalledWith(['n-mail', 'n-meeting']);
    });

    it('asks about one notification when the selection holds one, however it was picked out', () => {
        panel();

        fireEvent.pointerDown(screen.getByText('Ada Lovelace wrote'), {
            pointerType: 'mouse',
            button: 0,
            ctrlKey: true,
        });

        const bar = within(screen.getByRole('toolbar', { name: 'Actions on the notifications selected' }));
        fireEvent.click(bar.getByRole('button', { name: 'Delete' }));

        expect(screen.getByRole('heading', { name: 'Delete notification?' })).toBeDefined();
        expect(screen.getByText('1 notification will disappear from the centre.')).toBeDefined();
    });

    it('opens a row’s own menu on a right-click, where a selection starts and the source is opened from', () => {
        panel();

        fireEvent.contextMenu(screen.getByText('Ada Lovelace wrote'));

        expect(screen.getByRole('menuitem', { name: 'Select notifications' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Mark as read' })).toBeDefined();
        expect(screen.getByRole('menuitem', { name: 'Open the source' })).toBeDefined();
    });

    it('leaves the source out of the menu on a notification that leads nowhere', () => {
        panel();
        showAll();

        fireEvent.contextMenu(screen.getByText('Standing meeting moved'));

        expect(screen.queryByRole('menuitem', { name: 'Open the source' })).toBeNull();
        expect(screen.getByRole('menuitem', { name: 'Mark as unread' })).toBeDefined();
    });

    // The panel covers what it was opened over, so the back gesture reaches it before it navigates and a change of
    // destination leaves it behind. It is the panel's own way out that is recorded rather than a second one, which is
    // what makes the gesture clear what was picked as well as close the panel.
    it('leaves by its own way out when the back gesture reaches it', () => {
        const shell = panel();

        expect(shell.current.depth).toBe(1);

        act(() => {
            shell.current.closeTop();
        });

        expect(acts.hide).toHaveBeenCalled();
    });

    it('records nothing over the screen while the centre is not being shown', () => {
        const shell = panel({ shown: false });

        expect(shell.current.depth).toBe(0);
    });
});

describe('NotificationCentre, a failure’s row', () => {
    const refusal: ClientNotification = {
        ...mail,
        id: 'n-refusal',
        title: 'Not sent - it waits in the Outbox',
        body: 'Re: Contract addendum',
        statement: { cause: 'SendRefused', counted: 1, outOf: null },
        target: { kind: 'OutgoingEmail', outgoingEmailId: 'o-1' },
    };

    const change: ClientNotification = {
        ...mail,
        id: 'n-change',
        title: 'A change did not reach your mailbox',
        statement: { cause: 'MailboxChangeAbandoned', counted: 2, outOf: null },
        target: { kind: 'MailboxChange', storedEmailId: 'm-9', changeRecordId: 'r-1' },
    };

    it('takes a refused send to its Outbox to be edited there, reading the row once the Outbox has taken it', () => {
        const decided = vi.fn<(outgoingEmailId: string, decision: 'again' | 'edit', onTaken: () => void) => void>();

        panel({ notifications: [refusal] }, null, decided);

        fireEvent.click(screen.getByRole('button', { name: 'Edit' }));

        expect(acts.hide).toHaveBeenCalled();
        expect(decided).toHaveBeenCalledExactlyOnceWith('o-1', 'edit', expect.any(Function));
        expect(acts.markRead).not.toHaveBeenCalled();

        decided.mock.calls[0]?.[2]();

        expect(acts.markRead).toHaveBeenCalledExactlyOnceWith(['n-refusal'], true);
    });

    it('asks a change again once however often its pill is pressed while the message is still being read', async () => {
        const asked: string[] = [];
        const transport: MailFathomTransport = (request) => {
            asked.push(request.path);

            return new Promise(() => undefined);
        };

        panel(
            { notifications: [change] },
            null,
            undefined,
            { ...nothingPending, abandoned: [{ recordId: 'r-1', storedEmailId: 'm-9', act: 'flag' }] },
            { session: { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' }, transport },
        );

        const again = screen.getByRole('button', { name: 'Ask again' });

        fireEvent.click(again);
        fireEvent.click(again);
        await act(async () => {
            await vi.advanceTimersByTimeAsync(1);
        });

        expect(again.getAttribute('aria-disabled')).toBe('true');
        expect(asked).toHaveLength(1);
    });

    it('offers a given-up move as moved again, which leads to its message to choose the folder once more', () => {
        const move: ClientNotification = {
            ...change,
            statement: { cause: 'MailboxChangeAbandoned', counted: 4, outOf: null },
        };

        panel({ notifications: [move] }, null, undefined, {
            ...nothingPending,
            abandoned: [{ recordId: 'r-1', storedEmailId: 'm-9', act: 'move' }],
        });

        fireEvent.click(screen.getByRole('button', { name: 'Move again…' }));

        expect(screen.queryByRole('button', { name: 'Ask again' })).toBeNull();
        expect(acts.follow).toHaveBeenCalledExactlyOnceWith(move);
    });

    it('puts focus on the row’s own control once a decision on it is taken, rather than leaving it nowhere', () => {
        panel({ notifications: [change] }, null, undefined, {
            ...nothingPending,
            abandoned: [{ recordId: 'r-1', storedEmailId: 'm-9', act: 'flag' }],
            settle: vi.fn(),
        });
        showAll();

        fireEvent.click(screen.getByRole('button', { name: 'Let it go' }));

        expect(document.activeElement).toBe(
            within(rowFor('A change did not reach your mailbox')).getAllByRole('button')[0],
        );
    });

    it('says where a failure came from without the identifier the deployment wrote there', () => {
        panel({ notifications: [refusal] });

        expect(screen.getByText('Outbox')).toBeDefined();
    });

    it('lets go of a change the mailbox did not take from its row', () => {
        const settle = vi.fn();

        panel({ notifications: [change] }, null, undefined, {
            ...nothingPending,
            abandoned: [{ recordId: 'r-1', storedEmailId: 'm-9', act: 'flag' }],
            settle,
        });

        fireEvent.click(screen.getByRole('button', { name: 'Let it go' }));

        expect(settle).toHaveBeenCalledExactlyOnceWith('r-1', 'letGo');
    });

    it('offers nothing to decide about a change no longer waiting on anybody', () => {
        panel({ notifications: [change] });

        expect(screen.getAllByRole('listitem')).toHaveLength(1);
        expect(screen.queryByRole('button', { name: 'Let it go' })).toBeNull();
    });
});
