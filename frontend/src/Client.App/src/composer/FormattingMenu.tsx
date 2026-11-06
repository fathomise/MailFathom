// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { placedWithin } from '../contextMenu/menuPlacement';
import { Icon } from '../controls/Icon';
import { useScreenLayer } from '../shell/screenLayers';
import { useCoarsePointer } from '../shell/useWideWorkspace';

// What a formatting control that takes a choice opens, as the design draws it: a menu standing under the bar, headed
// by its name, whose items are one of a set of which one is taken. It stands inside the composer rather than over the
// window, because what it acts on is the selection in the words below it and the design keeps the two in one frame.
//
// It is not the context menu: that one is a row answering a press, opened at a point on a dimmed window, and this is a
// control's own choices opened under it with nothing dimmed. What the two share is how a menu is walked and left.

export function FormattingMenu({
    named,
    width,
    under,
    openedFrom,
    onClose,
    children,
}: {
    /** The menu's name, drawn as its head and carried as its accessible name. */
    readonly named: string;

    /** How wide it is drawn: as wide as the colour or the emoji grid, the table's width, or the one the rest take. */
    readonly width: FormattingMenuWidth;

    /** Where the control it opened from starts, measured from the start of the box the menu stands in. */
    readonly under: number;

    /** The control it opened from, whose own press is what closes it rather than a press outside it. */
    readonly openedFrom: HTMLElement | null;

    /**
     * Closes the menu without a choice, which is escape, Tab, the back gesture, and a press anywhere else. All but the
     * last put focus back on the control afterwards; a press elsewhere takes focus where it pressed.
     */
    readonly onClose: () => void;

    readonly children: ReactNode;
}) {
    const coarse = useCoarsePointer();
    const panel = useRef<HTMLDivElement>(null);
    const [start, setStart] = useState<number | null>(null);

    // Under a mouse it stands under its control and is kept inside the composer, drawn back by its own padding so its
    // choices start where the control does; under a finger it spans the composer, which the stylesheet says and
    // nothing here measures.
    useLayoutEffect(() => {
        const menu = panel.current;
        const box = menu?.offsetParent;

        if (menu === null || !(box instanceof HTMLElement) || coarse) {
            return;
        }

        setStart(
            placedWithin(
                { x: under - parseFloat(getComputedStyle(menu).paddingInlineStart), y: 0 },
                { width: menu.offsetWidth, height: 0 },
                { width: box.clientWidth, height: 0 },
            ).x,
        );
    }, [under, coarse]);

    // Focus goes to the choice it holds, so the keyboard starts where the reader's eye does — a choice among several,
    // that is, rather than a switch that happens to be on.
    useEffect(() => {
        const items = walkable(panel.current);

        (
            items.find(
                (item) => item.getAttribute('role') === 'menuitemradio' && item.getAttribute('aria-checked') === 'true',
            ) ?? items[0]
        )?.focus();
    }, []);

    // Leaving it without a choice puts focus back on the control it opened from, except where the whole screen is
    // being cleared and focus is the next screen's to place.
    function leave(clearingTheScreen = false): void {
        onClose();

        if (!clearingTheScreen) {
            openedFrom?.focus();
        }
    }

    useScreenLayer(true, leave);

    useEffect(() => {
        function pressedElsewhere(event: Event): void {
            const target = event.target as Node;

            if (panel.current?.contains(target) !== true && openedFrom?.contains(target) !== true) {
                onClose();
            }
        }

        document.addEventListener('pointerdown', pressedElsewhere, true);

        return () => {
            document.removeEventListener('pointerdown', pressedElsewhere, true);
        };
    }, [onClose, openedFrom]);

    function onKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
        // A field inside a menu — the hex value a colour of somebody's own is typed as, the lightness slider — keeps
        // its own keys, and only Escape still leaves the menu from it.
        if (event.target instanceof HTMLInputElement && event.key !== 'Escape') {
            return;
        }

        const items = walkable(panel.current);
        const standing = items.indexOf(document.activeElement as HTMLElement);

        switch (event.key) {
            case 'Escape':
            case 'Tab':
                leave();
                break;
            case 'ArrowDown':
            case 'ArrowRight':
                items[(standing + 1) % items.length]?.focus();
                break;
            case 'ArrowUp':
            case 'ArrowLeft':
                items[(standing - 1 + items.length) % items.length]?.focus();
                break;
            case 'Home':
                items[0]?.focus();
                break;
            case 'End':
                items[items.length - 1]?.focus();
                break;
            default:
                return;
        }

        event.preventDefault();
    }

    return (
        <div
            ref={panel}
            role="menu"
            aria-label={named}
            style={start === null || coarse ? undefined : { left: start }}
            className={`absolute top-full z-30 mt-1 flex animate-pop-in flex-col gap-0.5 rounded-lg border border-line bg-panel p-2 text-text shadow-overlay pointer-coarse:inset-x-3 ${
                menuWidths[width]
            } pointer-coarse:w-auto`}
            onKeyDown={onKeyDown}
        >
            <p aria-hidden="true" className="px-2.5 pt-1 pb-1.5 text-sm font-semibold text-muted">
                {named}
            </p>
            {children}
        </div>
    );
}

/** One choice in a formatting menu: what it is called, how it is drawn, and whether it is the one the control holds. */
export function FormattingChoice({
    named,
    chosen,
    onChoose,
    children,
}: {
    readonly named: string;
    readonly chosen: boolean;
    readonly onChoose: () => void;
    readonly children: ReactNode;
}) {
    return (
        <button
            type="button"
            role="menuitemradio"
            aria-checked={chosen}
            aria-label={named}
            tabIndex={-1}
            className={`flex min-h-8 items-center gap-2.5 rounded-md px-2.5 text-start text-base text-text transition hover:bg-hover pointer-coarse:min-h-11 ${
                chosen ? 'bg-accent-soft' : ''
            }`}
            onMouseDown={(event) => {
                // The selection the choice acts on is the editor's, and a press that moved the focus here would not
                // collapse it — but one that reached the editor first would.
                event.preventDefault();
            }}
            onClick={onChoose}
        >
            {children}
            <Icon
                name="check"
                className={`ms-auto size-icon-lg shrink-0 text-accent-deep ${chosen ? '' : 'invisible'}`}
            />
        </button>
    );
}

/** The widths a formatting menu is drawn at under a mouse; under a finger every one of them spans the composer. */
export type FormattingMenuWidth = 'colour' | 'emoji' | 'table' | 'narrow';

const menuWidths: Readonly<Record<FormattingMenuWidth, string>> = {
    colour: 'w-73.5',
    emoji: 'w-77',
    table: 'w-70',
    narrow: 'w-55',
};

/** The items as the keyboard reaches them, read out of the document so nothing here holds a second list of them. */
function walkable(panel: HTMLElement | null): HTMLElement[] {
    return panel === null
        ? []
        : Array.from(
              panel.querySelectorAll<HTMLElement>(
                  '[role="menuitem"], [role="menuitemradio"], [role="menuitemcheckbox"]',
              ),
          );
}
