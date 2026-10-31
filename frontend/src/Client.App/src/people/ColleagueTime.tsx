// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useCurrentMinute } from '../notifications/notificationAge';
import { colleagueClock, wordClockDifference } from './colleagueClock';

// The card beside a colleague's *Contact details* that says what time it is where they are, and how far that stands
// from the reader's own — *09:41, 1 h behind you*. It is drawn only for a colleague who states a zone: where none is
// stated no time of theirs is known, and the card would hold nothing. The clock moves with the minute, because a time
// read once at opening is wrong by the time somebody comes back to the page.

// Which sentence the offset takes, by which side of the reader's clock theirs stands.
function clockSaid(minutesAhead: number): MessageKey {
    if (minutesAhead === 0) {
        return 'people.clockSame';
    }

    return minutesAhead > 0 ? 'people.clockAhead' : 'people.clockBehind';
}

export function ColleagueTime({ timeZone }: { readonly timeZone: string }) {
    const { locale, translate } = useLocalization();
    const readerZone = useReadingZone();
    const now = useCurrentMinute();
    const clock = colleagueClock(timeZone, readerZone, now, locale);

    if (clock === null) {
        return null;
    }

    return (
        <section className="flex min-w-0 flex-col gap-2.5 rounded-lg border border-line bg-panel px-4.25 py-3.75">
            <h3 className="text-xs leading-natural font-semibold text-muted">{translate('people.timeCard')}</h3>

            <p className="flex min-w-0 items-center gap-2.5 border-t border-line-soft pt-2.25">
                <Icon name="schedule" className="size-icon-md shrink-0 text-muted" />
                <span className="text-md leading-natural tabular-nums">
                    {translate(clockSaid(clock.minutesAhead), {
                        time: clock.time,
                        difference: wordClockDifference(clock.minutesAhead, locale),
                    })}
                </span>
            </p>
        </section>
    );
}
