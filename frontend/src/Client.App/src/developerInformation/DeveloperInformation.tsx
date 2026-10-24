// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef } from 'react';
import { createPortal } from 'react-dom';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer } from '../shell/screenLayers';
import { useDesktopComposition, useDesktopCompositionIn, useTwoPanes } from '../shell/useWideWorkspace';
import { useTheme } from '../theme/useTheme';
import {
    closeDeveloperInformation,
    dressDeveloperWindow,
    returnFocusToOpener,
    useDeveloperAccess,
} from './developerAccess';
import { DeveloperSections, type DeveloperContext } from './DeveloperSections';

// Where the developer information surface stands while it is open: in the window `developerAccess.ts` opened for it,
// or inside the page where it opened none. It is mounted only while it is open, and what it shows begins watching as it
// mounts and stops as it goes, so a closed surface does no work at all.

export function DeveloperInformation({ context }: { readonly context: DeveloperContext }) {
    const { open, window: standing } = useDeveloperAccess();

    // After the surface is gone rather than as it goes, because a modal surface leaves its opener inert until it is.
    useEffect(() => {
        if (!open) {
            returnFocusToOpener();
        }
    }, [open]);

    if (!open) {
        return null;
    }

    return standing === null ? (
        <DeveloperDialog context={context} />
    ) : (
        <DeveloperWindow context={context} into={standing} />
    );
}

/**
 * The surface drawn into a window of its own, as a portal from this tree.
 *
 * A portal rather than a second application, because the window is the client's own blank document: everything it shows
 * is read here, from the same record, the same session, and the same contexts, so nothing has to be sent across. What
 * the window does need is the client's stylesheets, its theme, and its language, which are copied onto its document.
 */
function DeveloperWindow({ context, into }: { readonly context: DeveloperContext; readonly into: Window }) {
    const { translate, locale } = useLocalization();
    const { theme } = useTheme();
    const sideBySide = useDesktopCompositionIn(into);

    const title = `${translate('developer.title')} · ${translate('shell.title')}`;

    useEffect(() => {
        dressDeveloperWindow(into, title, locale, theme);
    }, [into, title, locale, theme]);

    useEffect(() => {
        const leaveOnEscape = (event: KeyboardEvent): void => {
            if (event.key === 'Escape') {
                closeDeveloperInformation();
            }
        };

        into.document.addEventListener('keydown', leaveOnEscape);

        return () => {
            into.document.removeEventListener('keydown', leaveOnEscape);
        };
    }, [into]);

    return createPortal(
        <div className="h-dvh">
            <DeveloperSections context={context} sideBySide={sideBySide} onClose={closeDeveloperInformation} />
        </div>,
        into.document.body,
    );
}

/**
 * The surface inside the page, which is what a touch screen is given.
 *
 * Where the window has two panes it is a large window that leaves the page usable behind it, so the control that opened
 * it in the rail closes it again; below that it takes the whole screen, as a modal over a page there is no room to use.
 * It is the platform's own `dialog` either way, so focus moves in as it opens, and goes back to its opener once it has closed.
 */
function DeveloperDialog({ context }: { readonly context: DeveloperContext }) {
    const { translate } = useLocalization();
    const twoPanes = useTwoPanes();
    const sideBySide = useDesktopComposition();

    const shownAs = useRef<'beside' | 'over' | null>(null);

    // The same dialog is shown again rather than drawn again when the window crosses the pane breakpoint, so what it has
    // gathered and the section it is on survive a tablet being turned.
    const arrived = useCallback(
        (dialog: HTMLDialogElement | null) => {
            const wanted = twoPanes ? 'beside' : 'over';

            if (dialog === null || shownAs.current === wanted) {
                return;
            }

            if (dialog.open) {
                dialog.close();
            }

            if (twoPanes) {
                dialog.show();
            } else {
                dialog.showModal();
            }

            shownAs.current = wanted;
        },
        [twoPanes],
    );

    useScreenLayer(true, closeDeveloperInformation);

    return (
        <dialog
            ref={arrived}
            aria-label={translate('developer.title')}
            onClose={(event) => {
                // The close a switch between beside and over makes is followed at once by the dialog showing again.
                if (!event.currentTarget.open) {
                    closeDeveloperInformation();
                }
            }}
            onKeyDown={(event) => {
                // A modal dialog closes on Escape on its own; one opened beside the page has to be told.
                if (event.key === 'Escape' && twoPanes) {
                    event.preventDefault();
                    closeDeveloperInformation();
                }
            }}
            className="fixed inset-0 z-50 m-0 h-auto max-h-none w-auto max-w-none overflow-hidden rounded-none border-0 bg-panel p-0 text-text open:flex open:flex-col panes:m-auto panes:h-file-window-tall panes:w-file-window panes:rounded-lg panes:border panes:border-line panes:shadow-dialog"
        >
            <DeveloperSections context={context} sideBySide={sideBySide} onClose={closeDeveloperInformation} />
        </dialog>
    );
}
