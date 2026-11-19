// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { dueDayOffsetMinutes, type PersonalTaskRecord } from '@mailfathom/client-backend';
import { calendarDayIn, dayFrom } from '../localization/instants';
import { readyTimes } from './sendLater';

// *If nobody replies*: a message that wants an answer is sent with a day, and becomes a task waiting for whoever it
// went to first, due that day and announced at the start of it. Closing it when the reply arrives is the service's,
// which reads the replies a thread receives; what is written here is only the wait.

export const followUpKeys = ['tomorrow', 'monday', 'week'] as const;

export type FollowUpKey = (typeof followUpKeys)[number];

/** The day a message waits for its reply until, and which of the offered days it is. */
export interface FollowUp {
    readonly key: FollowUpKey;

    /** As `yyyy-mm-dd`. */
    readonly dueOn: string;
}

/**
 * The days offered, as the reader's zone counts them: tomorrow, the next Monday — a week on from a Monday, as the
 * ready times to send at read it — and a week from today.
 */
export function followUpDays(now: number, zone: string | null): Readonly<Record<FollowUpKey, string>> {
    const ready = readyTimes(now, zone);

    return {
        tomorrow: ready.tomorrowMorning.date,
        monday: ready.mondayMorning.date,
        week: dayFrom(calendarDayIn(now, zone), 7),
    };
}

/**
 * The waiting task a sent message is written down as, or `null` for a message that went to nobody.
 *
 * It waits for the first recipient, named as the message they answered named them where it did, and belongs to the
 * thread the message answers, or to none for a message of its own. One lead at the start of the day is what *reminds
 * you that day* promises.
 */
export function followUpRecord({
    to,
    named,
    thread,
    dueOn,
    timeZone,
    replyFrom,
}: {
    readonly to: readonly string[];

    /** The people the answered message named, whose names a recipient is called by. */
    readonly named: readonly { readonly address: string; readonly name: string | null }[];

    /** The thread the message answers, or `null` for a message of its own. */
    readonly thread: { readonly threadId: string; readonly subject: string } | null;

    readonly dueOn: string;
    readonly timeZone: string | null;

    /** The task's line, worded around who it waits for. */
    readonly replyFrom: (who: string) => string;
}): PersonalTaskRecord | null {
    const [address] = to;

    if (address === undefined) {
        return null;
    }

    const displayName = named.find((one) => one.address.toLowerCase() === address.toLowerCase())?.name ?? null;

    return {
        title: replyFrom(displayName ?? address),
        dueOn,
        reminders: [0],
        dueDayOffsetMinutes: dueDayOffsetMinutes(dueOn, timeZone),
        sourceMessageId: null,
        direction: 'Awaited',
        estimatedMinutes: null,
        plan: null,
        anchor: thread === null ? null : { kind: 'Thread', id: thread.threadId, label: thread.subject },
        counterpart: { address, displayName },
    };
}
