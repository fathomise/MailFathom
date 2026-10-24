// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { StrictMode, type ReactNode } from 'react';
import { render, screen, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import { ComposingContext } from '../composer/useComposing';
import { LocalizationProvider } from '../localization/Localization';
import { spaces, type Space as SpaceName } from '../routing/spaces';
import { WorkspaceProvider } from '../workspace/Workspace';
import { Space } from './Space';

// Not catalogue entries: each stands for whatever the frame composes for Mail, which is the point of the three props.
const handedToMail = 'The mail this space was handed.';
const handedTheFolders = 'The folder tree this space was handed.';
const handedTheTabs = 'The tab strip this space was handed.';
const handedToTheCalendar = 'the calendar handed to the frame';
const handedToTasks = 'The task list this space was handed.';
const handedToPeople = 'The address book this space was handed.';
const handedTheList = 'The message list this space was handed.';
const handedToDiscover = 'The answer this space was handed.';
const handedToTheAgent = 'The conversation this space was handed.';
const handedTheStatus = 'The connection this space was handed.';

// Every case here renders under `StrictMode`, which is what `main.tsx` mounts and what makes React invoke an effect
// twice on the first mount. A focus rule written against "has this effect run before" passes without it and moves
// focus on landing with it, so the wrapper is the point of the test rather than a detail of the harness.
// The toolbar and the corner control both ask whether writing a message is offered. Nothing here is about writing one,
// so nothing offers it and both stand as the planned controls they were.
const nothingBeingWritten = {
    offered: false,
    drafts: false,
    opening: null,
    compose: () => undefined,
    close: () => undefined,
};

function inStrictMode(space: SpaceName, offered: readonly SpaceName[] = spaces): ReactNode {
    return (
        <StrictMode>
            <LocalizationProvider>
                <WorkspaceProvider>
                    <ComposingContext value={nothingBeingWritten}>
                        <Space
                            offered={offered}
                            space={space}
                            readingFooted={false}
                            status={<p>{handedTheStatus}</p>}
                            folders={<p>{handedTheFolders}</p>}
                            list={<p>{handedTheList}</p>}
                            mail={<p>{handedToMail}</p>}
                            tabs={<p>{handedTheTabs}</p>}
                            discover={<p>{handedToDiscover}</p>}
                            agent={<p>{handedToTheAgent}</p>}
                            tasks={<p>{handedToTasks}</p>}
                            people={<p>{handedToPeople}</p>}
                            calendar={<p>{handedToTheCalendar}</p>}
                            person="reader"
                        />
                    </ComposingContext>
                </WorkspaceProvider>
            </LocalizationProvider>
        </StrictMode>
    );
}

// The region is read at the width the workspace opens out at, so that Mail draws all three of its regions at once:
// jsdom lays nothing out, so the width is answered here rather than measured.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

beforeEach(() => {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.includes('min-width'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
});

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

describe('Space', () => {
    it('leaves focus where it was on landing, rather than pulling it into the content nobody navigated to', () => {
        render(inStrictMode('discover'));

        expect(document.activeElement).toBe(document.body);
    });

    // Both directions, because the region focused is the one the single ref is handed to and that ref moves between
    // regions that are all already mounted: a rule that only ever reached the space it was written for would pass on
    // the first move and leave focus behind on the second.
    it('puts focus at the start of the new content when the address changes', () => {
        const { rerender } = render(inStrictMode('discover'));

        rerender(inStrictMode('mail'));

        expect(document.activeElement).toBe(screen.getByRole('main', { name: 'Mail' }));

        rerender(inStrictMode('tasks'));

        expect(document.activeElement).toBe(screen.getByRole('main', { name: 'Tasks' }));
    });

    it('draws Mail without a heading, as the design does, and names the landmark instead', () => {
        render(inStrictMode('mail'));

        expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
        expect(screen.getByRole('main', { name: 'Mail' })).toBeDefined();
    });

    it('draws the Discover space it was handed, under no heading of the frame', () => {
        render(inStrictMode('discover'));

        expect(screen.getByText(handedToDiscover)).toBeDefined();
        expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
    });

    it('draws the Agent space it was handed, with neither of the frame’s two regions beneath it', () => {
        render(inStrictMode('agent'));

        const agent = screen.getByRole('main', { name: 'Agent' });

        expect(within(agent).getByText(handedToTheAgent)).toBeDefined();
        expect(within(agent).queryByText(handedTheStatus)).toBeNull();
    });

    // The other space the design draws the connection in: it stands in this screen's own head above the question
    // field, so the space is handed the connection where it is built rather than given it here as well.
    it('stands no connection beneath Discover, which is handed it instead', () => {
        render(inStrictMode('discover'));

        expect(screen.queryByText(handedTheStatus)).toBeNull();
    });

    it('carries the connection into Mail and into none of the spaces composing their own columns', () => {
        for (const space of ['tasks', 'people', 'calendar'] as const) {
            const { unmount } = render(inStrictMode(space));

            expect(screen.queryByText(handedTheStatus)).toBeNull();
            unmount();
        }

        render(inStrictMode('mail'));

        expect(screen.getByText(handedTheStatus)).toBeDefined();
    });

    it('shows what the frame composed for Mail in the Mail space', () => {
        render(inStrictMode('mail'));

        expect(screen.getByText(handedToMail)).toBeDefined();
    });

    it('names the space at the top of a phone, which has no rail beside it saying where the reader is', () => {
        Object.defineProperty(window, 'matchMedia', {
            configurable: true,
            value: (query: string) => ({
                media: query,
                matches: false,
                addEventListener: () => undefined,
                removeEventListener: () => undefined,
            }),
        });

        render(inStrictMode('people'));

        expect(screen.getByRole('heading', { level: 1, name: 'People' })).toBeDefined();
    });

    it('draws People without a heading either, and names its landmark the same way', () => {
        render(inStrictMode('people'));

        expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
        expect(screen.getByRole('main', { name: 'People' })).toBeDefined();
    });

    it('shows what the frame composed for Tasks in the Tasks space', () => {
        render(inStrictMode('tasks'));

        expect(within(screen.getByRole('main', { name: 'Tasks' })).getByText(handedToTasks)).toBeDefined();
    });

    it('shows what the frame composed for People in the People space', () => {
        render(inStrictMode('people'));

        expect(within(screen.getByRole('main', { name: 'People' })).getByText(handedToPeople)).toBeDefined();
    });

    it('draws the Calendar without a heading either, and names its landmark the same way', () => {
        render(inStrictMode('calendar'));

        expect(screen.queryByRole('heading', { level: 1 })).toBeNull();
        expect(screen.getByRole('main', { name: 'Calendar' })).toBeDefined();
    });

    it('shows what the frame composed for the Calendar in the Calendar space', () => {
        render(inStrictMode('calendar'));

        expect(within(screen.getByRole('main', { name: 'Calendar' })).getByText(handedToTheCalendar)).toBeDefined();
    });

    it('shows the scope the Mail space is drawn against beside what it is drawn from', () => {
        render(inStrictMode('mail'));

        expect(screen.getByText(handedTheFolders)).toBeDefined();
    });

    // The mechanism, stated as a count: a space the deployment offers is a space that is on the screen from the first
    // render, whichever one the address names.
    it('mounts every space the deployment offers, not only the one in front', () => {
        render(inStrictMode('tasks', ['discover', 'mail', 'tasks']));

        expect(document.querySelectorAll('main')).toHaveLength(3);
        expect(screen.getByLabelText('Discover')).toBeDefined();
        expect(screen.getByLabelText('Mail')).toBeDefined();
    });

    it('mounts no space the deployment does not offer', () => {
        render(inStrictMode('mail', ['mail']));

        expect(document.querySelectorAll('main')).toHaveLength(1);
        expect(screen.queryByLabelText('Tasks')).toBeNull();
    });

    // What says a space is aside is what a reader would find: one landmark rather than six, nothing in the others to
    // tab into, and no live region in one reaching somebody standing on another.
    it('leaves exactly the space in front reachable by the keyboard and by an accessible name', () => {
        render(inStrictMode('tasks', ['discover', 'mail', 'tasks']));

        expect(screen.getAllByRole('main')).toHaveLength(1);
        expect(screen.getByRole('main', { name: 'Tasks' })).toBeDefined();

        for (const aside of ['Discover', 'Mail'] as const) {
            const region = screen.getByLabelText(aside);

            expect(region.getAttribute('aria-hidden')).toBe('true');
            expect(region.hasAttribute('inert')).toBe(true);
        }
    });

    it('keeps a space on the screen while another is in front of it, with everything it was handed still in it', () => {
        render(inStrictMode('tasks'));

        const mail = screen.getByLabelText('Mail');

        expect(within(mail).getByText(handedTheList)).toBeDefined();
        expect(within(mail).getByText(handedTheFolders)).toBeDefined();
        expect(within(mail).getByText(handedToMail)).toBeDefined();
    });

    // The assertion that says *not rebuilt from zero*: the same element, not an element drawing the same thing. A space
    // taken down and put back reads again from its leading end and puts the reader at the top of it, and every node in
    // it is a new node — so node identity is what tells the two apart, and it holds under the extra mount `StrictMode`
    // performs, which a count of mounts would not.
    it('gives back the Mail space that was left rather than a rebuilt one', () => {
        const { rerender } = render(inStrictMode('mail'));
        const before = screen.getByText(handedTheList);

        rerender(inStrictMode('tasks'));
        rerender(inStrictMode('mail'));

        expect(screen.getByText(handedTheList)).toBe(before);
        expect(screen.getByRole('main', { name: 'Mail' })).toBeDefined();
    });

    it('gives back the space that was left whichever space it is, not only Mail', () => {
        const { rerender } = render(inStrictMode('tasks'));
        const before = screen.getByText(handedToTasks);

        rerender(inStrictMode('mail'));
        rerender(inStrictMode('tasks'));

        expect(screen.getByText(handedToTasks)).toBe(before);
    });

    // The scroll offset is the platform's rather than this client's: it belongs to the scroller's box, so it survives
    // exactly as long as that box does. jsdom keeps what is written to `scrollTop` without laying anything out, so the
    // number here says the element was never rebuilt — a space put back would be a new element reading zero. That the
    // aside space also keeps its *box*, by standing aside with `visibility` rather than leaving the layout, is a
    // rendered-layout claim and is the browser suite's rather than this one's.
    it('keeps a space its scroll offset across a visit to another space and back', () => {
        const { rerender } = render(inStrictMode('tasks'));
        const region = screen.getByLabelText('Tasks');

        region.scrollTop = 120;
        rerender(inStrictMode('mail'));
        rerender(inStrictMode('tasks'));

        expect(screen.getByLabelText('Tasks')).toBe(region);
        expect(region.scrollTop).toBe(120);
    });

    // What the frame composes for the space in front is handed to that one and to nothing else: a second connection
    // would be a second live region.
    it('hands the connection to Mail in front and to nothing behind it', () => {
        render(inStrictMode('mail'));

        expect(screen.getAllByText(handedTheStatus)).toHaveLength(1);
        expect(within(screen.getByRole('main', { name: 'Mail' })).getByText(handedTheStatus)).toBeDefined();
    });

    it('takes the connection away with Mail when another space is arrived at', () => {
        const { rerender } = render(inStrictMode('mail'));

        rerender(inStrictMode('tasks'));

        expect(screen.queryByText(handedTheStatus)).toBeNull();
    });
});
