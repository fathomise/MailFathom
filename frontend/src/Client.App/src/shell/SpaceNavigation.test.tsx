// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { spaces, type Space } from '../routing/spaces';
import { SpaceNavigation } from './SpaceNavigation';
import type { SpaceReport } from './spaceReport';

const handedTheAccount = 'The account control this navigation was handed.';
const handedTheBell = 'The bell this navigation was handed.';
const handedTheRefresh = 'The refresh this navigation was handed.';
const handedTheDeveloper = 'The developer control this navigation was handed.';

// The one thing about this component jsdom cannot answer for, because it decides which of the two shapes is in the
// document rather than how one of them is laid out. It answers narrow to every query unless a test says otherwise, so
// a test about the rail states the width it is about instead of inheriting one.
function atWorkspaceWidth(wide: boolean): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: wide,
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

function renderNavigation(
    offered: readonly Space[] = spaces,
    current: Space | null = 'mail',
    reports: Partial<Record<Space, SpaceReport>> = {},
): void {
    render(
        <LocalizationProvider>
            <SpaceNavigation
                offered={offered}
                current={current}
                account={<button>{handedTheAccount}</button>}
                notifications={<button>{handedTheBell}</button>}
                refresh={<button>{handedTheRefresh}</button>}
                developer={<button>{handedTheDeveloper}</button>}
                reports={reports}
                onPointerDown={() => undefined}
                onClickCapture={() => undefined}
            />
        </LocalizationProvider>,
    );
}

/** Every link in the document, the overflow's included: jsdom draws a closed popover hidden, exactly as a browser does. */
function everyLink(): [string | null, string | null][] {
    return screen
        .getAllByRole('link', { hidden: true })
        .map((space) => [space.textContent, space.getAttribute('href')]);
}

describe('SpaceNavigation', () => {
    afterEach(() => {
        atWorkspaceWidth(false);
    });

    it('offers every space as a link, each at an address of its own', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        expect(everyLink()).toEqual([
            ['Discover', '#/discover'],
            ['Agent', '#/agent'],
            ['Mail', '#/mail'],
            ['Tasks', '#/tasks'],
            ['Calendar', '#/calendar'],
            ['People', '#/people'],
        ]);
    });

    // The design separates the two spaces that ask from the four that hold, with a hairline in the rail and nothing in
    // the bar, whose places are counted; a reader hears no separator, which is decoration rather than structure.
    it('rules the spaces that ask off from the spaces that hold in the rail, and hides the rule from the tree', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        const rule = screen.getByRole('separator', { hidden: true });

        expect(screen.queryByRole('separator')).toBeNull();
        expect(screen.getByRole('link', { name: 'Agent' }).compareDocumentPosition(rule)).toBe(
            Node.DOCUMENT_POSITION_FOLLOWING,
        );
        expect(rule.compareDocumentPosition(screen.getByRole('link', { name: 'Mail' }))).toBe(
            Node.DOCUMENT_POSITION_FOLLOWING,
        );
    });

    it('draws no rule in the bar', () => {
        renderNavigation();

        expect(screen.queryByRole('separator', { hidden: true })).toBeNull();
    });

    it('names a space by what it is, with nothing appended', () => {
        renderNavigation();

        expect(screen.getByRole('link', { name: 'Mail' })).toBeDefined();
    });

    it('marks the space being shown as the current one, and no other', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        expect(screen.getByRole('link', { current: 'page' }).textContent).toBe('Mail');
    });

    it('is named, so it is one landmark a reader can move to rather than three loose links', () => {
        renderNavigation();

        expect(screen.getByRole('navigation', { name: 'Spaces' })).toBeDefined();
    });

    it('offers only what it was given, so a space this credential may not open is absent from the rail', () => {
        atWorkspaceWidth(true);
        renderNavigation(['mail', 'tasks']);

        expect(everyLink().map(([name]) => name)).toEqual(['Mail', 'Tasks']);
    });

    it('places the account control after the spaces, so it is last in the rail', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        const navigation = screen.getByRole('navigation', { name: 'Spaces' });
        const account = screen.getByRole('button', { name: handedTheAccount });
        const lastLink = screen.getAllByRole('link').at(-1);

        expect(navigation.contains(account)).toBe(true);
        expect(lastLink?.compareDocumentPosition(account)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });

    it('stands the developer control it was handed in the rail, above the account', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        const navigation = screen.getByRole('navigation', { name: 'Spaces' });
        const developer = screen.getByRole('button', { name: handedTheDeveloper });

        expect(navigation.contains(developer)).toBe(true);
        expect(developer.compareDocumentPosition(screen.getByRole('button', { name: handedTheAccount }))).toBe(
            Node.DOCUMENT_POSITION_FOLLOWING,
        );
    });

    it('stands the bell beside the account, after every space and before the account itself', () => {
        atWorkspaceWidth(true);
        renderNavigation();

        const bell = screen.getByRole('button', { name: handedTheBell });
        const account = screen.getByRole('button', { name: handedTheAccount });
        const lastLink = screen.getAllByRole('link').at(-1);

        expect(lastLink?.compareDocumentPosition(bell)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
        expect(bell.compareDocumentPosition(account)).toBe(Node.DOCUMENT_POSITION_FOLLOWING);
    });

    it('still places the account control while the deployment has not said which spaces there are', () => {
        atWorkspaceWidth(true);
        renderNavigation([], null);

        expect(screen.queryAllByRole('link')).toEqual([]);
        expect(screen.getByRole('button', { name: handedTheAccount })).toBeDefined();
    });

    // The bar has five places and the rail has as many as the session offers, which is the one difference between the
    // two shapes that is not a matter of laying the same nodes out differently.
    it('spends the bar’s five places on three spaces, the bell, and the overflow', () => {
        renderNavigation();

        const bar = screen.getByRole('navigation', { name: 'Spaces' });

        // Every control the bar holds, in the order it draws them. The overflow's own sheet is inside the navigation
        // without standing in it — the platform draws a closed popover nowhere and an open one in the top layer.
        const places = Array.from(bar.querySelectorAll('a, button')).filter(
            (place) => place.closest('[popover]') === null,
        );

        expect(places.map((place) => place.textContent)).toEqual(['Discover', 'Agent', 'Mail', handedTheBell, 'More']);
    });

    it('hands the bar’s overflow every space it had no place for, and the account after them', () => {
        renderNavigation();

        const sheet = document.getElementById('space-overflow');
        const behindIt = screen.getAllByRole('link', { hidden: true }).filter((link) => sheet?.contains(link) === true);

        expect(behindIt.map((link) => link.textContent)).toEqual(['Tasks', 'Calendar', 'People']);
        expect(sheet?.contains(screen.getByRole('button', { name: handedTheAccount, hidden: true }))).toBe(true);
    });

    it('keeps the overflow even where every offered space fits the bar, the account standing behind it', () => {
        renderNavigation(['mail', 'agent'], 'mail');

        expect(everyLink().map(([name]) => name)).toEqual(['Mail', 'Agent']);
        expect(screen.getByRole('button', { name: 'More' })).toBeDefined();
    });
});

describe('a rail tile’s report', () => {
    afterEach(() => {
        atWorkspaceWidth(false);
    });

    const opened = vi.fn();

    const working: SpaceReport = {
        dot: 'working',
        counts: ['1 working', '1 unread'],
        sections: [
            {
                label: 'Working on',
                kind: 'working',
                rows: [{ key: 'a', title: 'Overdue invoices', detail: 'Searching your mail…', open: opened }],
            },
            {
                label: 'Unread',
                kind: 'waiting',
                rows: [{ key: 'b', title: 'Northwind reports', detail: '10:12', open: () => undefined }],
            },
        ],
        empty: 'Nothing running and nothing unread.',
    };

    function dotOf(space: string): string | null {
        return (
            screen
                .getByRole('link', { name: new RegExp(`^${space}`) })
                .querySelector('[data-space-dot]')
                ?.getAttribute('data-space-dot') ?? null
        );
    }

    it('draws a pulsing dot while something runs, a still one while something only waits, and none otherwise', () => {
        atWorkspaceWidth(true);
        renderNavigation(spaces, 'mail', {
            agent: working,
            discover: { ...working, dot: 'waiting' },
            mail: { ...working, dot: null, counts: [], sections: [] },
        });

        expect(dotOf('Agent')).toBe('working');
        expect(dotOf('Discover')).toBe('waiting');
        expect(dotOf('Mail')).toBeNull();
    });

    it('carries the counts in the tile’s name, so a reader who cannot see the dot hears them', () => {
        atWorkspaceWidth(true);
        renderNavigation(spaces, 'mail', { agent: working });

        expect(screen.getByRole('link', { name: 'Agent - 1 working, 1 unread' }).getAttribute('href')).toBe('#/agent');
    });

    it('slides out the peek under a mouse resting on the rail’s tile, and a row opens what it names', () => {
        atWorkspaceWidth(true);
        renderNavigation(spaces, 'mail', { agent: working });

        fireEvent.pointerOver(screen.getByRole('link', { name: /^Agent/ }), { pointerType: 'mouse' });
        const peek = screen.getByRole('dialog', { name: 'Agent' });

        expect(within(peek).getByText('Working on')).toBeTruthy();
        expect(within(peek).getByText('Unread')).toBeTruthy();

        fireEvent.click(within(peek).getByRole('button', { name: /Overdue invoices/ }));

        expect(opened).toHaveBeenCalledTimes(1);
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('never opens the peek under a finger, which has the dot and the place itself', () => {
        atWorkspaceWidth(true);
        renderNavigation(spaces, 'mail', { agent: working });

        fireEvent.pointerOver(screen.getByRole('link', { name: /^Agent/ }), { pointerType: 'touch' });

        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('never opens the peek from the bar, which has no room beside it', () => {
        atWorkspaceWidth(false);
        renderNavigation(spaces, 'mail', { agent: working });

        fireEvent.pointerOver(screen.getByRole('link', { name: /^Agent/ }), { pointerType: 'mouse' });

        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('says so where nothing runs and nothing waits', () => {
        atWorkspaceWidth(true);
        renderNavigation(spaces, 'mail', { agent: { ...working, dot: null, counts: [], sections: [] } });

        fireEvent.pointerOver(screen.getByRole('link', { name: 'Agent' }), { pointerType: 'mouse' });

        expect(
            within(screen.getByRole('dialog', { name: 'Agent' })).getByText('Nothing running and nothing unread.'),
        ).toBeTruthy();
    });
});
