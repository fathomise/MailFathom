// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId } from 'react';
import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';
import { useCoarsePointer } from '../shell/useWideWorkspace';
import { toggleDeveloperInformation, useDeveloperAccess } from './developerAccess';

// The control that opens and closes the developer information surface, drawn only once the version line in Settings
// has switched developer mode on — so on an ordinary run the navigation holds nothing here at all. It is drawn the two
// ways the refresh control above it is: the symbol alone in the rail, and the symbol with its name in the overflow
// sheet. While it is drawn developer mode is recording, open or closed, so it carries a mark saying so; pressing it
// only shows or hides what is already being kept.

const resting =
    'text-muted hover:bg-hover hover:text-text rail:border-line rail:bg-panel rail:text-muted rail:hover:bg-hover rail:hover:text-text';
const showing =
    'bg-accent-soft text-accent-deep hover:bg-hover hover:text-text rail:border-accent-line rail:bg-accent-soft rail:text-accent-deep rail:hover:bg-hover rail:hover:text-text';

export function DeveloperInformationButton() {
    const { translate } = useLocalization();
    const { unlocked, open } = useDeveloperAccess();
    const coarsePointer = useCoarsePointer();
    const recordingId = useId();
    // In the rail the symbol stands alone, so its name is the bubble's; in the overflow sheet it is drawn beside it.
    const { anchor, tip } = useTip({
        says: translate('developer.title'),
        onPress: () => {
            toggleDeveloperInformation(!coarsePointer);
        },
    });

    if (!unlocked) {
        return null;
    }

    return (
        <>
            <button
                {...anchor}
                type="button"
                aria-label={translate('developer.title')}
                aria-expanded={open}
                aria-describedby={recordingId}
                className={`relative flex flex-1 cursor-pointer flex-col items-center gap-0.75 rounded-xl px-0.5 py-1.75 text-sm transition rail:size-8.5 rail:flex-none rail:justify-center rail:gap-0 rail:rounded-lg rail:border rail:px-0 rail:py-0 ${open ? showing : resting}`}
            >
                <Icon name="code" className="size-icon-lg" />
                <span
                    aria-hidden="true"
                    className="absolute end-1.75 top-1.75 size-2 animate-working rounded-full bg-error ring-2 ring-panel rail:end-0.75 rail:ring-rail rail:top-0.75"
                />
                <span id={recordingId} hidden>
                    {translate('developer.recordingInBackground')}
                </span>
                <span className="max-w-full truncate rail:hidden">{translate('developer.title')}</span>
            </button>
            {tip}
        </>
    );
}
