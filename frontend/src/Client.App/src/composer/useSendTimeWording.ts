// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { wordSendTime, type SendTime, type SendTimeWording } from './sendLater';

// The catalogue entries each way of saying a time is written with, the same day apart from every other day.
const sentences: Readonly<Record<keyof SendTimeWording, { readonly today: MessageKey; readonly otherDay: MessageKey }>> =
    {
        short: { today: 'compose.sendTimeShortToday', otherDay: 'compose.sendTimeShort' },
        date: { today: 'compose.sendTimeDateToday', otherDay: 'compose.sendTimeDate' },
        long: { today: 'compose.sendTimeLongToday', otherDay: 'compose.sendTimeLong' },
    };

/** Says a time to send at in the reader's language, against one instant and the zone the reader's record states. */
export function useSendTimeWording(now: number, zone: string | null): (chosen: SendTime) => SendTimeWording {
    const { locale, translate } = useLocalization();

    return (chosen) =>
        wordSendTime(chosen, now, zone, locale, (shape, today, pieces) =>
            translate(today ? sentences[shape].today : sentences[shape].otherDay, { ...pieces }),
        );
}
