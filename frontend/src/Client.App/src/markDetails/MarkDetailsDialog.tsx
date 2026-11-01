// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ReactNode, RefObject } from 'react';
import { useTopLayerSurface } from '../shell/screenLayers';
import { pressedOutside } from '../shell/usePressOutsideSurface';
import { useDesktopComposition } from '../shell/useWideWorkspace';
import { detailsPlacement, type DetailsShape, type MarkAnchor } from './detailsPlacement';

// The surface a mark on a message opens its details in — the mark on who sent it, and the mark on what came back for
// it. **Anchored under the mark at the desktop, a sheet rising from the foot of the window below it**, which is the
// design's own for both: a popup beside a list row reads as about that row where there is room for both, and a finger
// wants the whole width. One `dialog` either way, so the platform traps focus, answers Escape, and hands focus back to
// whatever opened it. Opened from somewhere with no mark to stand under — a row's menu — it stands in the middle.

export function MarkDetailsDialog({
    dialog,
    anchor,
    shape,
    widthClass,
    names,
    onOpened,
    children,
}: {
    readonly dialog: RefObject<HTMLDialogElement | null>;

    /** Where the mark that opened them stands, or `null` where nothing on the screen did. */
    readonly anchor: MarkAnchor | null;
    readonly shape: DetailsShape;

    /** The desktop width the shape names, as the token that holds it. */
    readonly widthClass: string;

    /** The element naming the details. */
    readonly names: string;
    readonly onOpened?: () => void;
    readonly children: ReactNode;
}) {
    const closesOnBack = useTopLayerSurface();
    const desktop = useDesktopComposition();

    // The one value here a token cannot hold: where the mark that asked happens to stand.
    const placed =
        desktop && anchor !== null
            ? detailsPlacement(anchor, { width: window.innerWidth, height: window.innerHeight }, shape)
            : null;

    return (
        <dialog
            ref={dialog}
            onToggle={(event) => {
                closesOnBack(event);

                if (event.newState === 'open') {
                    onOpened?.();
                }
            }}
            aria-labelledby={names}
            data-pull="down"
            onClick={(event) => {
                // Below the desktop a press beside the sheet is the frame's to answer, as for every sheet; at the
                // desktop nothing dims the screen behind the details, so a press beside them is a press elsewhere.
                const surface = event.currentTarget;

                if (desktop && event.target === surface && pressedOutside(surface, event.clientX, event.clientY)) {
                    surface.close();
                }
            }}
            style={
                placed === null
                    ? undefined
                    : {
                          left: placed.left,
                          top: placed.top ?? 'auto',
                          bottom: placed.bottom ?? 'auto',
                          maxHeight: placed.maxHeight,
                      }
            }
            className={`max-desktop:surface-sheet desktop:surface-pop fixed m-0 hidden flex-col overflow-hidden bg-panel p-0 text-text open:flex max-desktop:inset-x-0 max-desktop:top-auto max-desktop:bottom-0 max-desktop:mx-auto max-desktop:max-h-sheet-height max-desktop:w-full max-desktop:max-w-150 max-desktop:rounded-t-xl max-desktop:border-0 max-desktop:pb-safe-bottom max-desktop:shadow-dialog max-desktop:backdrop:bg-scrim ${widthClass} desktop:max-w-full desktop:rounded-lg desktop:border desktop:border-line desktop:shadow-dialog desktop:backdrop:bg-transparent ${
                placed === null ? 'desktop:m-auto' : ''
            }`}
        >
            {/* The handle the design draws at the head of the sheet, which says a finger can push it away. */}
            <span aria-hidden="true" className="flex shrink-0 justify-center py-2 desktop:hidden">
                <span className="h-1 w-9.5 rounded-xs bg-line-strong" />
            </span>

            {children}
        </dialog>
    );
}
