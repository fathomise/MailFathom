// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useRef, useState } from 'react';

// What somebody has selected inside a draft, followed from the document's own selection rather than from the draft's
// events. A finger selects by a long press and its handles, which end in no release the draft hears, and a mouse drag
// may end outside the draft altogether — so a mouse is read on its release, wherever that lands, and anything else once
// the selection has settled.
//
// It reports a selection only where both its ends are inside the draft's text. One reaching out of the draft —
// into the conversation around it, or across two drafts — raises nothing, because there is no one draft to rewrite.

/** Where on the screen a selection stands, in viewport pixels: its whole box, and the box of its last line. */
export interface SelectionBox {
    readonly top: number;
    readonly bottom: number;
    readonly left: number;
    readonly endTop: number;
    readonly endBottom: number;
    readonly endRight: number;
}

/** A selection inside a draft. */
export interface SelectedPassage {
    /** The selected words with their spacing collapsed, as the bar quotes them. */
    readonly words: string;

    /** Where the selection starts and ends in the draft's text, as the surface counts it. */
    readonly from: number;
    readonly to: number;
    readonly box: SelectionBox;
}

// How long a selection made by anything but a mouse is left to settle before the button follows it: a handle being
// dragged changes the selection on every frame.
const settling = 350;

/**
 * Follows the document's selection inside a draft.
 *
 * @param placeOf Reads where a place in the document stands in the draft's text, or `null` where it is outside it.
 * @param active Whether a selection there is one that can be rewritten now.
 * @param docked Whether the bar docks rather than floating over the passage, in which case scrolling leaves it be.
 * @returns The selection, whether its bar is open, and the two acts on it.
 */
export function useSelectedPassage(
    placeOf: (node: Node, offset: number) => number | null,
    active: boolean,
    docked: boolean,
): {
    readonly selected: SelectedPassage | null;
    readonly open: boolean;
    readonly openBar: () => void;
    readonly close: () => void;
} {
    const [selected, setSelected] = useState<SelectedPassage | null>(null);
    const [open, setOpen] = useState(false);
    const mouseHeld = useRef(false);

    function close(): void {
        window.getSelection()?.removeAllRanges();
        setSelected(null);
        setOpen(false);
    }

    const follow = useEffectEvent(() => {
        const selection = window.getSelection();
        const words =
            selection === null || selection.isCollapsed ? '' : selection.toString().replace(/\s+/g, ' ').trim();

        // An emptied selection takes the button with it. The open bar stays, since focusing its own field is what
        // empties the selection; its × and Escape are what close it.
        if (selection === null || words === '' || selection.rangeCount === 0) {
            if (!open) {
                setSelected(null);
            }

            return;
        }

        const range = selection.getRangeAt(0);
        const from = placeOf(range.startContainer, range.startOffset);
        const to = placeOf(range.endContainer, range.endOffset);

        if (from === null || to === null) {
            if (!open) {
                setSelected(null);
            }

            return;
        }

        const whole = range.getBoundingClientRect();
        const end = [...range.getClientRects()].at(-1) ?? whole;

        if (selected?.words === words && open) {
            return;
        }

        setOpen(false);
        setSelected({
            words,
            from,
            to,
            box: {
                top: whole.top,
                bottom: whole.bottom,
                left: whole.left,
                endTop: end.top,
                endBottom: end.bottom,
                endRight: end.right,
            },
        });
    });

    const scrolled = useEffectEvent(() => {
        if (!docked && (selected !== null || open)) {
            close();
        }
    });

    useEffect(() => {
        if (!active) {
            return undefined;
        }

        let waiting: ReturnType<typeof setTimeout> | undefined;

        function selectionChanged(): void {
            clearTimeout(waiting);

            if (!mouseHeld.current) {
                waiting = setTimeout(follow, settling);
            }
        }

        function pointer(event: PointerEvent): void {
            if (event.pointerType !== 'mouse') {
                return;
            }

            mouseHeld.current = event.type === 'pointerdown';

            if (!mouseHeld.current) {
                clearTimeout(waiting);
                follow();
            }
        }

        function scrolledSomewhere(): void {
            scrolled();
        }

        document.addEventListener('selectionchange', selectionChanged);
        document.addEventListener('pointerdown', pointer, true);
        document.addEventListener('pointerup', pointer, true);
        document.addEventListener('scroll', scrolledSomewhere, { capture: true, passive: true });

        return () => {
            clearTimeout(waiting);
            // A selection read before the words were written over would come back at offsets naming other words.
            setSelected(null);
            setOpen(false);
            document.removeEventListener('selectionchange', selectionChanged);
            document.removeEventListener('pointerdown', pointer, true);
            document.removeEventListener('pointerup', pointer, true);
            document.removeEventListener('scroll', scrolledSomewhere, { capture: true });
        };
    }, [active]);

    return {
        selected: active ? selected : null,
        open: active && open && selected !== null,
        openBar: () => {
            setOpen(true);
        },
        close,
    };
}
