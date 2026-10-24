// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useLocalization } from '../localization/useLocalization';
import { useScreenLayer } from '../shell/screenLayers';
import { useTwoPanes } from '../shell/useWideWorkspace';
import { useTheme } from '../theme/useTheme';
import { closeDeveloperInformation, dressDeveloperWindow, useDeveloperAccess } from './developerAccess';
import { DeveloperSections, type DeveloperContext } from './DeveloperSections';

// Where the developer information surface stands while it is open: in the window `developerAccess.ts` opened for it,
// or inside the page where it opened none. It is mounted only while it is open, and what it shows begins watching as it
// mounts and stops as it goes, so a closed surface does no work at all.

export function DeveloperInformation({ context }: { readonly context: DeveloperContext }) {
    const { open, window: standing } = useDeveloperAccess();

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

    const title = `${translate('developer.title')} · ${translate('shell.title')}`;

    useEffect(() => {
        dressDeveloperWindow(into, title, locale, theme);
    }, [into, title, locale, theme]);

    return createPortal(
        <div className="h-dvh">
            <DeveloperSections context={context} onClose={closeDeveloperInformation} />
        </div>,
        into.document.body,
    );
}

/**
 * The surface inside the page, which is what a touch screen is given.
 *
 * Where the window has two panes it is a large window that leaves the page usable behind it, so the control that opened
 * it in the rail closes it again; below that it takes the whole screen, as a modal over a page there is no room to use.
 * It is the platform's own `dialog` either way, so focus moves in as it opens and back to its opener as it closes.
 */
function DeveloperDialog({ context }: { readonly context: DeveloperContext }) {
    const { translate } = useLocalization();
    const twoPanes = useTwoPanes();

    const arrived = useCallback(
        (dialog: HTMLDialogElement | null) => {
            if (dialog !== null && !dialog.open) {
                if (twoPanes) {
                    dialog.show();
                } else {
                    dialog.showModal();
                }
            }
        },
        [twoPanes],
    );

    useScreenLayer(true, closeDeveloperInformation);

    return (
        <dialog
            key={twoPanes ? 'beside' : 'over'}
            ref={arrived}
            aria-label={translate('developer.title')}
            onClose={closeDeveloperInformation}
            onKeyDown={(event) => {
                // A modal dialog closes on Escape on its own; one opened beside the page has to be told.
                if (event.key === 'Escape' && twoPanes) {
                    event.preventDefault();
                    closeDeveloperInformation();
                }
            }}
            className="fixed inset-0 z-50 m-0 h-auto max-h-none w-auto max-w-none overflow-hidden rounded-none border-0 bg-panel p-0 text-text open:flex open:flex-col panes:m-auto panes:h-file-window-tall panes:w-file-window panes:rounded-lg panes:border panes:border-line panes:shadow-dialog"
        >
            <DeveloperSections context={context} onClose={closeDeveloperInformation} />
        </dialog>
    );
}
