// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import { wordRecentInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';

// The instant a message was recorded, as the design words it on a row: the time alone for today, the word for
// yesterday, and the day and month for anything older. The clock is read once, when the row is first drawn, because a
// render is pure and the clock is not — so a row that stays on the screen across midnight words yesterday as today
// until it is drawn again, which a windowed list does for every row it scrolls back to.

/**
 * The instant a message was recorded, or nothing at all where the message carries none this client can read.
 *
 * @param emphasized Whether the row it stands on is unread, which sets the time in the text's own colour and heavier.
 */
export function ReceivedAt({ at, emphasized = false }: { readonly at: string | null; readonly emphasized?: boolean }) {
    const { locale } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());
    const when = wordRecentInstant(at, locale, now, timeZone);

    if (at === null || when === null) {
        return null;
    }

    return (
        <time
            dateTime={at}
            className={`shrink-0 text-xs tabular-nums ${emphasized ? 'font-semibold text-text' : 'text-faint'}`}
        >
            {when}
        </time>
    );
}
