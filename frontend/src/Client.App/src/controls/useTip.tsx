// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import {
    useEffect,
    useId,
    useLayoutEffect,
    useRef,
    useState,
    type FocusEvent,
    type KeyboardEvent,
    type MouseEvent,
    type PointerEvent,
    type ReactNode,
} from 'react';
import { createPortal } from 'react-dom';
import { placedWithin, type MenuPoint } from '../contextMenu/menuPlacement';
import { claimPress, pressCancelled, pressedByFinger, pressOpensAfter } from '../contextMenu/rowPress';

// The sentence an element says about itself — a symbol's name, the reason a control cannot act, what a chip's state
// means — drawn as the one bubble the design gives every such sentence. It is the client's only tooltip, and `title`
// is not one: no touch browser draws a title at all, so a name kept there exists only under a mouse.
//
// **Every way in reaches the same sentence.** A mouse rests on the element, a key focuses it, and a finger either holds
// it — on an element that does something, whose tap is that thing — or taps it, on one that does nothing else. A
// sentence a finger or a tap brought up goes by itself, since a finger has no hover to leave.
//
// **One gesture is one surface.** Only one sentence stands at a time, any press anywhere else takes it away — so a
// press that opens a menu or starts a swipe closes it first — and the platform's own menu gesture that follows a held
// finger is that same hold rather than a second one, so it never reaches the row the element sits in. The tap a hold
// ends in is swallowed, so holding a control to learn its name never also presses it.

/** How long a mouse rests on an element before its sentence stands, so a pointer crossing a toolbar does not flicker. */
export const tipRestsFor = 400;

/** How long a sentence a finger brought up stands before it goes by itself. */
export const tipStandsFor = 4000;

/** How far the bubble stands from the element it speaks for, in CSS pixels. */
const tipGap = 6;

/** The one sentence standing anywhere in the client, and how to take it away. */
let standing: { readonly owner: object; readonly hide: () => void } | null = null;

/** What an element binds to speak for itself, spread onto it as it stands. */
export interface TipAnchor {
    readonly ref: (element: HTMLElement | null) => void;
    readonly 'aria-describedby'?: string | undefined;
    readonly onPointerEnter: (event: PointerEvent) => void;
    readonly onPointerLeave: (event: PointerEvent) => void;
    readonly onPointerDown: (event: PointerEvent) => void;
    readonly onPointerMove: (event: PointerEvent) => void;
    readonly onPointerUp: () => void;
    readonly onPointerCancel: () => void;
    readonly onFocus: (event: FocusEvent) => void;
    readonly onBlur: () => void;
    readonly onKeyDown: (event: KeyboardEvent) => void;
    readonly onClick: (event: MouseEvent) => void;
    readonly onContextMenu: (event: MouseEvent) => void;
}

export interface Tip {
    readonly anchor: TipAnchor;

    /** The bubble itself, rendered beside the element; it stands over everything while it is shown. */
    readonly tip: ReactNode;
}

export function useTip({
    says,
    describes = false,
    onPress,
    tapReveals = true,
}: {
    readonly says: string;

    /**
     * Whether the sentence is the element's description, which it is wherever it says something the element's name does
     * not. A symbol whose name is the sentence leaves this off, or a reader would hear the name twice.
     */
    readonly describes?: boolean;

    /**
     * What the element does when it is pressed. Present, a finger holds it to read the sentence; absent, a tap does.
     * An element whose press is the platform's own — a submit button, a disclosure — hands a press that does nothing
     * more, which still claims the hold.
     */
    readonly onPress?: (() => void) | undefined;

    /**
     * Whether a tap brings the sentence up on an element that does nothing else. Off for an element inside something
     * a tap already acts on — a row — whose sentence a finger reaches in that row's menu instead.
     */
    readonly tapReveals?: boolean;
}): Tip {
    const id = useId();
    const [owner] = useState(() => ({}));
    const element = useRef<HTMLElement | null>(null);
    const bubble = useRef<HTMLSpanElement>(null);
    const [shown, setShown] = useState(false);
    const [host, setHost] = useState<HTMLElement | null>(null);
    const [placed, setPlaced] = useState<MenuPoint | null>(null);
    const clock = useRef<number | undefined>(undefined);
    const heldAt = useRef<{ readonly x: number; readonly y: number } | null>(null);
    const byFinger = useRef(false);
    const revealedByHold = useRef(false);

    const forget = () => {
        window.clearTimeout(clock.current);
        clock.current = undefined;
    };

    const hide = () => {
        forget();
        setShown(false);
        if (standing?.owner === owner) {
            standing = null;
        }
    };

    const show = (goesAfter?: number) => {
        forget();
        if (standing !== null && standing.owner !== owner) {
            standing.hide();
        }

        standing = { owner, hide };
        // A modal dialog stands in the top layer, so a bubble for something inside one is drawn inside it too.
        setHost(element.current?.closest('dialog') ?? document.body);
        setShown(true);
        if (goesAfter !== undefined) {
            clock.current = window.setTimeout(hide, goesAfter);
        }
    };

    useEffect(
        () => () => {
            window.clearTimeout(clock.current);
            if (standing?.owner === owner) {
                standing = null;
            }
        },
        [owner],
    );

    // A press anywhere but here takes the sentence away, and so does anything that moves what it was placed against.
    // Both are listened for on the document itself, in the capture phase, so a press something else stops from
    // propagating still reaches it.
    useEffect(() => {
        if (!shown) {
            return;
        }

        const gone = () => {
            window.clearTimeout(clock.current);
            clock.current = undefined;
            setShown(false);
            if (standing?.owner === owner) {
                standing = null;
            }
        };
        const pressed = (event: Event) => {
            if (!(event.target instanceof Node) || element.current?.contains(event.target) !== true) {
                gone();
            }
        };

        document.addEventListener('pointerdown', pressed, true);
        window.addEventListener('scroll', gone, true);
        window.addEventListener('resize', gone);
        return () => {
            document.removeEventListener('pointerdown', pressed, true);
            window.removeEventListener('scroll', gone, true);
            window.removeEventListener('resize', gone);
        };
    }, [shown, owner]);

    // Under the element, from its start edge, and above it where the window has no room below; either way inside the
    // window. Measured once it is drawn and before it is painted, which is a read of the layout and so an effect's.
    useLayoutEffect(() => {
        if (!shown || element.current === null || bubble.current === null) {
            setPlaced(null);
            return;
        }

        const from = element.current.getBoundingClientRect();
        const drawn = bubble.current.getBoundingClientRect();
        const below = from.bottom + tipGap;
        const y = below + drawn.height > window.innerHeight ? from.top - tipGap - drawn.height : below;
        setPlaced(
            placedWithin(
                { x: from.left, y },
                { width: drawn.width, height: drawn.height },
                { width: window.innerWidth, height: window.innerHeight },
            ),
        );
    }, [shown, says]);

    const anchor: TipAnchor = {
        ref: (drawn) => {
            element.current = drawn;
        },
        'aria-describedby': describes ? id : undefined,
        onPointerEnter: (event) => {
            if (event.pointerType === 'mouse') {
                forget();
                clock.current = window.setTimeout(() => {
                    show();
                }, tipRestsFor);
            }
        },
        onPointerLeave: (event) => {
            if (event.pointerType === 'mouse') {
                hide();
            }
        },
        onPointerDown: (event) => {
            byFinger.current = pressedByFinger(event.pointerType);
            revealedByHold.current = false;
            if (!byFinger.current) {
                // Pressing a control is acting rather than reading, so its name gets out of the way; a press on
                // something that only explains itself leaves the explanation to the hover that brought it up.
                if (onPress !== undefined) {
                    hide();
                }
                return;
            }

            if (onPress !== undefined) {
                claimPress(event);
                heldAt.current = { x: event.clientX, y: event.clientY };
                forget();
                clock.current = window.setTimeout(() => {
                    revealedByHold.current = true;
                    show(tipStandsFor);
                }, pressOpensAfter);
            }
        },
        onPointerMove: (event) => {
            const at = heldAt.current;
            if (at === null || !pressCancelled(event.clientX - at.x, event.clientY - at.y)) {
                return;
            }

            // A finger that starts travelling is swiping or scrolling, which is another gesture: the hold is off, and
            // a name it already brought up goes rather than riding along with the swipe.
            heldAt.current = null;
            if (revealedByHold.current) {
                hide();
            } else {
                forget();
            }
        },
        onPointerUp: () => {
            heldAt.current = null;
            if (!revealedByHold.current) {
                forget();
            }
        },
        onPointerCancel: () => {
            // A press the browser took back — into a scroll, say — was no tap, so the click a key sends later is not
            // a finger's.
            byFinger.current = false;
            heldAt.current = null;
            if (!revealedByHold.current) {
                forget();
            }
        },
        onFocus: (event) => {
            if (focusedByKey(event.currentTarget)) {
                show();
            }
        },
        onBlur: hide,
        onKeyDown: (event) => {
            if (event.key === 'Escape' && shown) {
                event.stopPropagation();
                hide();
            }
        },
        onClick: (event) => {
            const finger = byFinger.current;
            byFinger.current = false;
            if (revealedByHold.current) {
                // Including what the element would do by itself — a submit, a disclosure — rather than only what it
                // was handed to do.
                revealedByHold.current = false;
                event.preventDefault();
                return;
            }

            if (onPress !== undefined) {
                onPress();
            } else if (finger && tapReveals) {
                show(tipStandsFor);
            }
        },
        onContextMenu: (event) => {
            if (byFinger.current && onPress !== undefined) {
                event.preventDefault();
                event.stopPropagation();
            }
        },
    };

    const bubbleClass =
        'pointer-events-none fixed z-80 w-max max-w-72 rounded-md bg-text px-2.5 py-1.75 text-base leading-snug text-pretty text-panel shadow-raised';

    const sentence = (
        <span
            className={bubbleClass}
            hidden={!shown}
            id={id}
            ref={bubble}
            role="tooltip"
            style={placed === null ? { left: 0, top: 0, visibility: 'hidden' } : { left: placed.x, top: placed.y }}
        >
            {says}
        </span>
    );

    return {
        anchor,
        tip: shown && host !== null ? createPortal(sentence, host) : describes ? sentence : null,
    };
}

/** Whether focus arrived from the keyboard, which is the one focus that should bring a sentence up by itself. */
function focusedByKey(target: Element): boolean {
    try {
        return target.matches(':focus-visible');
    } catch {
        return true;
    }
}
