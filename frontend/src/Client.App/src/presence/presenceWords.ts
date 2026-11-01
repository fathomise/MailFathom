// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { PresenceDuration, PresenceState, StatedWorkMode, WorkMode } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';

// What each state, each while, and each place of work is called, which is the screen's rather than the wire's.

/** The word for each of the six states, which is what stands beside every dot so the dot is never the only telling. */
export const presenceSaid: Readonly<Record<PresenceState, MessageKey>> = {
    Active: 'presence.active',
    Away: 'presence.away',
    Busy: 'presence.busy',
    DoNotDisturb: 'presence.doNotDisturb',
    Offline: 'presence.offline',
    Hidden: 'presence.hidden',
};

/** The four places *Working from* offers, in the design's order, each with what it is called. */
export const statedWorkModes: readonly {
    readonly mode: WorkMode;
    readonly today: boolean;
    readonly key: MessageKey;
}[] = [
    { mode: 'Office', today: true, key: 'presence.officeToday' },
    { mode: 'Office', today: false, key: 'presence.officeUntilChanged' },
    { mode: 'Remote', today: true, key: 'presence.remoteToday' },
    { mode: 'Remote', today: false, key: 'presence.remoteUntilChanged' },
];

/** Where somebody works from as the design words it — *Office today*, *Remote until changed*. */
export function workModeSaid(stated: StatedWorkMode): MessageKey {
    const today = stated.endsAt !== null;

    return (
        statedWorkModes.find((offered) => offered.mode === stated.mode && offered.today === today)?.key ??
        'presence.notStated'
    );
}

/** A while as the *Reset status after* select names it — *30 minutes*, *Today*. */
export function wordDuration(
    duration: PresenceDuration,
    locale: Locale,
    translate: (key: MessageKey) => string,
): string {
    // The three whiles that are a number of something are worded by `Intl`, so the plural is the language's own; the
    // two that are a period of the calendar are catalogue entries.
    switch (duration) {
        case '30m':
            return measured(locale, 'minute', 30);
        case '1h':
            return measured(locale, 'hour', 1);
        case '2h':
            return measured(locale, 'hour', 2);
        case 'today':
            return translate('presence.forToday');
        case 'week':
            return translate('presence.forWeek');
    }
}

function measured(locale: Locale, unit: 'minute' | 'hour', count: number): string {
    return new Intl.NumberFormat(locale, { style: 'unit', unit, unitDisplay: 'long' }).format(count);
}
