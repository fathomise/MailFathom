// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useContext, useEffect, useId, useLayoutEffect, useRef, useState, type KeyboardEvent } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { DepartureContext } from '../shell/departure';
import { useScreenLayer } from '../shell/screenLayers';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { placedWithin, type MenuPoint } from './menuPlacement';
import { pressSuppressesTapFor } from './rowPress';

// What a row answers a press with, which the design draws on six of its lists: a header naming the row, then
// the acts that row offers, the one that cannot be taken back drawn as what it is. It is one component for all six
// rather than a menu per screen, because a second arrangement of a header and a column of items is how a client comes
// to have two menus that resemble each other — and because what a menu *is* has nothing to do with what a row holds.
//
// So what a list supplies is its items, and everything else is here: where the menu stands, that it stays on the
// screen, that it can be walked without a pointer, and that leaving it puts the reader back where they were.
//
// **The two compositions are the design's own.** Given room, the menu opens at the point the gesture happened
// and is pushed back inside the pane rather than off its edge; given a phone, it stands in the middle of the screen,
// because a menu anchored under a thumb at the foot of a narrow window has nowhere to go. Neither is a question about
// which head this is — it is the width the window has, asked once.
//
// **It announces as a menu**, which is what makes every item reachable by name rather than by position: the header
// names it, the items are its own, and nothing else is inside the element carrying the role.

/** One thing a row offers, as the menu draws it. */
export interface ContextMenuItem {
    readonly icon: IconName;
    readonly label: string;

    /** Whether this is the act that cannot be taken back, which the design draws apart from the others. */
    readonly destroys?: boolean;

    /**
     * Why the item cannot act now, where it cannot — a write while the server is out of reach. It stays in the menu,
     * drawn as an item that cannot act and saying why in place of its name, so the reason is read before it is pressed
     * rather than the act failing after; choosing it does nothing and leaves the menu open.
     */
    readonly refused?: string | null;

    readonly choose: () => void;
}

const menuItem =
    'flex min-h-9 cursor-pointer items-center gap-2.75 rounded-md px-3 text-start text-base transition hover:bg-hover pointer-coarse:min-h-12 pointer-coarse:text-md';

export function ContextMenu({
    header,
    detail,
    at,
    endsAt = false,
    items,
    onClose,
}: {
    /** What the menu is about, in the row's own words, which is also the name the menu carries. */
    readonly header: string;

    /**
     * What the row draws only where a mouse can rest on it — a sender folded to an avatar, the exact time behind a
     * relative one — said under the header, since the press that opened this menu is the one a finger would have
     * used to ask.
     */
    readonly detail?: string | undefined;

    /** Where the gesture happened, in the window's own coordinates. */
    readonly at: MenuPoint;

    /**
     * Whether `at` is where the menu's end edge stands rather than its start, which is how a menu opened from a control
     * at the end of a bar is drawn: under the control, aligned to its far edge, rather than running past it.
     */
    readonly endsAt?: boolean;

    readonly items: readonly ContextMenuItem[];

    /** Closes the menu and puts focus back on what it was opened from, which is the caller's to know. */
    readonly onClose: () => void;
}) {
    const wide = useWideWorkspace();
    const names = useId();
    const within = useRef<HTMLDivElement>(null);
    const panel = useRef<HTMLDivElement>(null);
    const [placed, setPlaced] = useState<MenuPoint | null>(null);
    const leaving = useContext(DepartureContext);

    // When the menu arrived, which is what tells the press that opened it from a reader pointing somewhere else.
    const arrivedAt = useRef(0);

    useEffect(() => {
        arrivedAt.current = performance.now();
    }, []);

    // Measured rather than computed from what the items would come to: the menu's own size is the stylesheet's answer,
    // and a second copy of it here would be two numbers that have to agree. A layout effect, because this runs against
    // a commit the browser has already laid out and before it has painted one — so the menu is never drawn in the wrong
    // place and then corrected.
    useLayoutEffect(() => {
        const box = within.current;
        const menu = panel.current;

        if (box === null || menu === null || !wide) {
            return;
        }

        const bounds = box.getBoundingClientRect();

        setPlaced(
            placedWithin(
                { x: at.x - bounds.left - (endsAt ? menu.offsetWidth : 0), y: at.y - bounds.top },
                { width: menu.offsetWidth, height: menu.offsetHeight },
                { width: box.clientWidth, height: box.clientHeight },
            ),
        );
    }, [at, endsAt, wide]);

    // Focus moves into the menu whichever opener asked for it, because a menu that opened somewhere else on the screen
    // is one a reader working from the keyboard would have to hunt for — and because Escape has to reach it.
    useEffect(() => {
        walkable(panel.current)[0]?.focus();
    }, []);

    // It stands over the row it was opened from, so the back gesture closes it before it does anything else — the same
    // way out Escape and a press outside it take. The component is on the screen only while the menu is, which is why
    // it is registered unconditionally.
    useScreenLayer(!leaving, onClose);

    // Closed by a press outside it rather than by the click that follows one. The tap ending a long press arrives as a
    // click with no press of its own behind it, so a menu listening for that would close under the finger that had
    // just asked for it — and the press that opened this one is already over by the time this is listening.
    useEffect(() => {
        if (leaving) {
            return;
        }

        function pressedElsewhere(event: Event): void {
            if (panel.current?.contains(event.target as Node) !== true) {
                onClose();
            }
        }

        // A second menu gesture outside it is a reader pointing somewhere else, so this one goes — unless it is the
        // press that opened it. A finger held on a row opens the menu on the row's own timer, and the platform answers
        // that same press with a menu gesture of its own a moment later, delivered to whatever is under the finger by
        // then, which is the scrim this just drew there. Read as a second gesture, it closed the menu the press had
        // just opened, and only where the menu happened not to land under the finger did it stay.
        function pointedElsewhere(event: Event): void {
            if (
                panel.current?.contains(event.target as Node) !== true &&
                performance.now() - arrivedAt.current > pressSuppressesTapFor
            ) {
                onClose();
            }
        }

        document.addEventListener('pointerdown', pressedElsewhere, true);
        document.addEventListener('contextmenu', pointedElsewhere, true);

        return () => {
            document.removeEventListener('pointerdown', pressedElsewhere, true);
            document.removeEventListener('contextmenu', pointedElsewhere, true);
        };
    }, [onClose, leaving]);

    function onKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
        const walked = walkable(panel.current);
        const standing = walked.indexOf(document.activeElement as HTMLElement);

        switch (event.key) {
            // Tab beside Escape, because leaving by the keyboard's own way out has to close the menu rather than put
            // focus behind it, where a reader would be tabbing through a screen with a menu still open over it.
            case 'Escape':
            case 'Tab':
                onClose();
                break;
            case 'ArrowDown':
                walked[(standing + 1) % walked.length]?.focus();
                break;
            case 'ArrowUp':
                walked[(standing - 1 + walked.length) % walked.length]?.focus();
                break;
            case 'Home':
                walked[0]?.focus();
                break;
            case 'End':
                walked[walked.length - 1]?.focus();
                break;
            default:
                return;
        }

        event.preventDefault();
    }

    return (
        <>
            {/* What the page is dimmed to behind the menu. The browser's own menu is refused out here, and the panel
                refuses it on its own account below, because it paints over this rather than inside it. Whether such a
                gesture also closes this one, and everything else about leaving, is the effects above. */}
            <div
                aria-hidden="true"
                className={`fixed inset-0 z-70 bg-scrim ${leaving ? 'animate-scrim-out' : 'animate-scrim-in'}`}
                onContextMenu={(event) => {
                    event.preventDefault();
                }}
            />

            {/* The space the menu is kept inside, which is the window less whatever a notch or a rounded corner takes
                out of it. It is a box of its own so that staying on the screen is one measurement rather than an inset
                added to every number below. */}
            <div
                ref={within}
                className="pointer-events-none fixed top-safe-top right-safe-right bottom-safe-bottom left-safe-left z-70 flex items-center justify-center p-4.5 workspace:block workspace:p-0"
            >
                <div
                    ref={panel}
                    style={placed === null ? undefined : { left: placed.x, top: placed.y }}
                    className={`pointer-events-auto flex max-h-full w-75 max-w-full flex-col overflow-y-auto rounded-lg border border-line bg-panel pt-1.75 pr-1.5 pb-2 pl-1.5 text-text shadow-dialog workspace:absolute workspace:w-59 workspace:pointer-coarse:w-65.5 ${
                        wide && placed === null ? 'invisible' : ''
                    } ${leaving ? 'animate-pop-out' : 'animate-pop-in'}`}
                    onKeyDown={onKeyDown}
                    // The same gesture landing on the menu itself asks for what is already open, so it is refused and
                    // nothing else happens: closing here would take the menu away from somebody who pointed at it.
                    onContextMenu={(event) => {
                        event.preventDefault();
                    }}
                >
                    <p id={names} className="truncate px-3 pt-1 pb-1.5 text-xs font-semibold text-muted">
                        {header}
                    </p>
                    {detail === undefined ? null : (
                        <p className="-mt-1 truncate px-3 pb-1.5 text-xs text-faint">{detail}</p>
                    )}

                    {/* Nothing but the items sits inside the element carrying the role, which is what a menu is
                        allowed to hold — the header names it from outside rather than standing in it as one more
                        thing to walk past. */}
                    <div role="menu" aria-labelledby={names} className="flex flex-col gap-0.5">
                        {items.map((item) => (
                            <button
                                key={item.label}
                                type="button"
                                role="menuitem"
                                // Focus is moved by the arrow keys rather than by Tab, which is what a menu is: one
                                // stop on the way in, and the items reached from there.
                                tabIndex={-1}
                                aria-disabled={refusedItem(item) ? true : undefined}
                                className={`${menuItem} ${item.destroys ? 'text-error-text' : ''} aria-disabled:cursor-not-allowed aria-disabled:opacity-60`}
                                onClick={() => {
                                    if (refusedItem(item)) {
                                        return;
                                    }

                                    onClose();
                                    item.choose();
                                }}
                            >
                                {/* The symbol keeps its own column so every label starts in the same place, which is
                                    what the design draws and what makes the list readable as a column. */}
                                <span className="flex w-6 shrink-0 justify-center">
                                    <Icon name={item.icon} className="size-icon-lg" />
                                </span>

                                {refusedItem(item) ? (
                                    <span className="py-1.5">{item.refused}</span>
                                ) : (
                                    <span className="truncate">{item.label}</span>
                                )}
                            </button>
                        ))}
                    </div>
                </div>
            </div>
        </>
    );
}

/** Whether the item says why it cannot act rather than acting. */
function refusedItem(item: ContextMenuItem): boolean {
    return item.refused !== undefined && item.refused !== null;
}

/** The items as the keyboard reaches them, read out of the document so nothing here holds a second list of them. */
function walkable(panel: HTMLElement | null): readonly HTMLElement[] {
    return panel === null ? [] : Array.from(panel.querySelectorAll<HTMLElement>('[role="menuitem"]'));
}
