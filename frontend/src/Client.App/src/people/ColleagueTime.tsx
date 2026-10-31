// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ColleagueWorkingHours } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useCurrentMinute } from '../notifications/notificationAge';
import { colleagueClock, wordClockDifference } from './colleagueClock';
import { wordColleagueHours } from './colleagueHours';

// The card beside a colleague's *Contact details* that says what time it is where they are, and when they work. The
// clock row — *09:41, 1 h behind you* — is drawn only for a colleague who states a zone this runtime knows. The hours
// row says the days and hours on their own clock, then the same hours on the reader's where the two differ, and the
// card is headed *Outside working hours* while it is outside them where the colleague is. Without a zone the hours are
// shown as stated, with a note that they are not converted, and nobody can say whether it is outside them. The card is
// drawn where there is a clock or hours to show, and it moves with the minute, because a time read once at opening is
// wrong by the time somebody comes back to the page.

// Which sentence the offset takes, by which side of the reader's clock theirs stands.
function clockSaid(minutesAhead: number): MessageKey {
    if (minutesAhead === 0) {
        return 'people.clockSame';
    }

    return minutesAhead > 0 ? 'people.clockAhead' : 'people.clockBehind';
}

export function ColleagueTime({
    timeZone,
    workingHours,
    displayName,
}: {
    readonly timeZone: string | null;
    readonly workingHours: ColleagueWorkingHours | null;
    readonly displayName: string;
}) {
    const { locale, translate } = useLocalization();
    const readerZone = useReadingZone();
    const now = useCurrentMinute();
    const clock = timeZone === null ? null : colleagueClock(timeZone, readerZone, now, locale);
    const hours =
        workingHours === null
            ? null
            : wordColleagueHours(workingHours, timeZone, readerZone, now, locale, (first, last) =>
                  translate('people.workingRun', { first, last }),
              );

    if (clock === null && hours === null) {
        return null;
    }

    return (
        <section className="flex min-w-0 flex-col gap-2.5 rounded-lg border border-line bg-panel px-4.25 py-3.75">
            <div className="flex items-center gap-2.25">
                <h3 className="text-xs leading-natural font-semibold text-muted">{translate('people.timeCard')}</h3>
                {hours?.outside === true ? (
                    <p className="ml-auto flex items-center gap-1 rounded-sm bg-warning-soft px-1.75 py-0.5 text-xs leading-natural font-semibold whitespace-nowrap text-warning-text">
                        <Icon name="bedtime" className="size-icon-xs" />
                        {translate('people.outsideWorkingHours')}
                    </p>
                ) : null}
            </div>

            {clock === null ? null : (
                <p className="flex min-w-0 items-center gap-2.5 border-t border-line-soft pt-2.25">
                    <Icon name="schedule" className="size-icon-md shrink-0 text-muted" />
                    <span className="text-md leading-natural tabular-nums">
                        {translate(clockSaid(clock.minutesAhead), {
                            time: clock.time,
                            difference: wordClockDifference(clock.minutesAhead, locale),
                        })}
                    </span>
                </p>
            )}

            {hours === null ? null : (
                <div className="flex min-w-0 items-start gap-2.5 border-t border-line-soft pt-2.25">
                    <Icon name="work_history" className="size-icon-md shrink-0 text-muted" />
                    <div className="flex min-w-0 flex-col gap-0.75">
                        <p className="text-md leading-natural tabular-nums">
                            {translate('people.workingHours', { days: hours.days, hours: hours.own })}
                            {hours.yours === null ? null : (
                                <span className="text-muted">
                                    {' '}
                                    {translate('people.workingHoursYours', { hours: hours.yours })}
                                </span>
                            )}
                        </p>
                        {timeZone === null ? (
                            <p className="text-sm text-muted text-pretty">
                                {translate('people.workingHoursNoZone', { name: displayName.split(' ')[0] ?? '' })}
                            </p>
                        ) : null}
                    </div>
                </div>
            )}
        </section>
    );
}
