// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ReactNode } from 'react';
import { useTip } from './useTip';

// A control drawn as its symbol alone — the cross that takes a chip away, the mark that opens a sheet — whose shape is
// its caller's. Its name is the control's, read out and shown in `useTip`'s bubble to a mouse resting on it, a key
// focusing it and a finger holding it, so no symbol in the client is one somebody has to press to learn the meaning of.
//
// The shapes the design repeats are `Control`'s and `SurfaceControl`'s; this is for the one-off symbol beside something
// else, where a shape table would be a row per caller.

export function SymbolControl({
    label,
    className,
    pressed,
    expanded,
    focusable = true,
    ref,
    onPress,
    children,
}: {
    readonly label: string;
    readonly className: string;
    readonly pressed?: boolean;
    readonly expanded?: boolean;

    /** Whether Tab stops here, which is off for a control inside a composite widget that moves focus by arrow keys. */
    readonly focusable?: boolean;

    /** Hands the button to a caller that has to place the keyboard on it, beside the tip's own hold on it. */
    readonly ref?: (element: HTMLButtonElement | null) => void;

    readonly onPress: () => void;

    /** The symbol itself. */
    readonly children: ReactNode;
}) {
    const { anchor, tip } = useTip({ says: label, onPress });

    return (
        <>
            <button
                {...anchor}
                ref={(element) => {
                    anchor.ref(element);
                    ref?.(element);
                }}
                type="button"
                aria-label={label}
                aria-pressed={pressed}
                aria-expanded={expanded}
                tabIndex={focusable ? undefined : -1}
                className={className}
            >
                {children}
            </button>
            {tip}
        </>
    );
}
