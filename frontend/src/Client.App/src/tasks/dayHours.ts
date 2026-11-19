// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { sendTimeAt } from '../composer/sendLater';

/** What a task row carries while it is dragged onto an hour: its identity, under a type of this client's own. */
export const carriedTaskType = 'application/x-mailfathom-task';

/** The hours a day is drawn as where a task is planned into it — the design's eight in the morning to five. */
export const dayHours: readonly number[] = [8, 9, 10, 11, 12, 13, 14, 15, 16, 17];

/**
 * The hour an instant begins in on one day, on the reader's own clock, or `null` where it falls on another day.
 *
 * @param instant The instant, as the deployment stated it.
 * @param timeZone The zone the reader's record states, or `null` for the runtime's own.
 * @param day The day the hours are drawn for, as `yyyy-mm-dd`.
 */
export function hourOf(instant: string, timeZone: string | null, day: string): number | null {
    const at = Date.parse(instant);

    if (Number.isNaN(at)) {
        return null;
    }

    const clock = sendTimeAt(at, timeZone);

    return clock.date === day ? Number(clock.time.slice(0, 2)) : null;
}
