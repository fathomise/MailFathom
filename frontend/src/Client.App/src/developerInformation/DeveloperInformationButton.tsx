// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useCoarsePointer } from '../shell/useWideWorkspace';
import { toggleDeveloperInformation, useDeveloperAccess } from './developerAccess';

// The control that opens and closes the developer information surface, drawn only once the version line in Settings
// has unlocked it — so on an ordinary run the navigation holds nothing here at all. It is drawn the two ways the
// refresh control above it is: the symbol alone in the rail, and the symbol with its name in the overflow sheet.

const resting =
    'text-muted hover:bg-hover hover:text-text workspace:border-line workspace:bg-panel workspace:text-muted workspace:hover:bg-hover workspace:hover:text-text';
const showing =
    'bg-accent-soft text-accent-deep hover:bg-hover hover:text-text workspace:border-accent-line workspace:bg-accent-soft workspace:text-accent-deep workspace:hover:bg-hover workspace:hover:text-text';

export function DeveloperInformationButton() {
    const { translate } = useLocalization();
    const { unlocked, open } = useDeveloperAccess();
    const coarsePointer = useCoarsePointer();

    if (!unlocked) {
        return null;
    }

    return (
        <button
            type="button"
            aria-label={translate('developer.title')}
            aria-expanded={open}
            title={translate('developer.title')}
            className={`flex flex-1 cursor-pointer flex-col items-center gap-0.75 rounded-xl px-0.5 py-1.75 text-sm transition workspace:size-8.5 workspace:flex-none workspace:justify-center workspace:gap-0 workspace:rounded-lg workspace:border workspace:px-0 workspace:py-0 ${open ? showing : resting}`}
            onClick={() => {
                toggleDeveloperInformation(!coarsePointer);
            }}
        >
            <Icon name="code" className="size-icon-lg" />
            <span className="max-w-full truncate workspace:hidden">{translate('developer.title')}</span>
        </button>
    );
}
