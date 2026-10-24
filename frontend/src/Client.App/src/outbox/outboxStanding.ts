// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { OutgoingMail, OutgoingMailPreview, OutgoingMailRecipient } from '@mailfathom/client-backend';
import type { IconName } from '../controls/icons';
import { wordInstant } from '../localization/instants';
import type { Locale } from '../localization/locale';
import type { MessageKey } from '../localization/en';
import type { Translate } from '../localization/useLocalization';

// Where one send in an Outbox stands, as the design draws it: six states out of the service's three stages, because a
// person deciding about a send needs to know more than the stage says. A send still recorded is waiting, written to
// leave later, or being tried again; one whose transmission began is being sent right now or is one whose answer never
// came back. The service publishes the facts each of those is read from, and this is the one place that reads them.

/** One of the six states a row and a preview draw. */
export type OutboxStanding = 'waiting' | 'scheduled' | 'retrying' | 'sending' | 'unknown' | 'refused';

/** What a person may decide about a send at a state: withdraw it, offer it again or dismiss it, or nothing at all. */
export type OutboxDecisions = 'cancel' | 'decide' | null;

/** How a state is coloured, which the design names as four tones. */
export type OutboxTone = 'neutral' | 'accent' | 'warning' | 'error';

interface StandingShape {
    readonly icon: IconName;
    readonly tone: OutboxTone;
    readonly decisions: OutboxDecisions;

    /** Whether the glyph pulses, which is the one state that is moving while somebody looks at it. */
    readonly moving: boolean;
}

export const standingShapes: Readonly<Record<OutboxStanding, StandingShape>> = {
    waiting: { icon: 'schedule', tone: 'neutral', decisions: 'cancel', moving: false },
    scheduled: { icon: 'schedule_send', tone: 'neutral', decisions: 'cancel', moving: false },
    retrying: { icon: 'update', tone: 'warning', decisions: 'cancel', moving: false },
    sending: { icon: 'send', tone: 'accent', decisions: null, moving: true },
    unknown: { icon: 'help', tone: 'warning', decisions: 'decide', moving: false },
    refused: { icon: 'error', tone: 'error', decisions: 'decide', moving: false },
};

type Standing = Pick<OutgoingMail, 'stage' | 'heldByAttempt' | 'dueAt' | 'attemptCount'>;

/**
 * Where a send stands, or `null` for one that has left the Outbox — delivered or withdrawn — which a preview read by its
 * identifier can still answer with.
 */
export function standingOf(send: Standing): OutboxStanding | null {
    switch (send.stage) {
        case 'Recorded':
            if (send.heldByAttempt) {
                return 'sending';
            }

            if (send.attemptCount > 0) {
                return 'retrying';
            }

            return send.dueAt === null ? 'waiting' : 'scheduled';
        case 'TransmissionBegun':
            return send.heldByAttempt ? 'sending' : 'unknown';
        case 'Refused':
            return 'refused';
        case 'Sent':
        case 'Cancelled':
            return null;
    }
}

const lineKeys: Readonly<Record<OutboxStanding, MessageKey>> = {
    waiting: 'outbox.stage.waiting',
    scheduled: 'outbox.stage.scheduled',
    retrying: 'outbox.stage.retrying',
    sending: 'outbox.stage.sending',
    unknown: 'outbox.stage.unknown',
    refused: 'outbox.stage.refused',
};

const attemptCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'outbox.attempts.other',
    one: 'outbox.attempts.one',
    two: 'outbox.attempts.other',
    few: 'outbox.attempts.few',
    many: 'outbox.attempts.many',
    other: 'outbox.attempts.other',
};


/** What wording a send needs: the language, the reader's zone, and the catalogue. */
export interface OutboxWording {
    readonly locale: Locale;
    readonly timeZone: string | null;
    readonly translate: Translate;
}

/** How many attempts a send has taken, in words. */
export function attemptsSaid(count: number, { locale, translate }: OutboxWording): string {
    return translate(attemptCounts[new Intl.PluralRules(locale).select(count)], {
        count: new Intl.NumberFormat(locale).format(count),
    });
}

/** A clock time on the reader's own clock, which is how the design names when something happened today. */
function timeOf(instant: string, { locale, timeZone }: OutboxWording): string {
    return wordInstant(instant, locale, 'time', timeZone) ?? '';
}

/** When a send was written to leave, which may be any day, so the day is said beside the time. */
function leavesAt(instant: string | null, { locale, timeZone }: OutboxWording): string {
    return wordInstant(instant, locale, 'stamp', timeZone) ?? '';
}

/** The state's own words, which name it in a row and head it in a preview. */
export function standingTitle(
    standing: OutboxStanding,
    send: OutgoingMail | OutgoingMailPreview,
    wording: OutboxWording,
): string {
    if (standing === 'refused') {
        return wording.translate('outbox.title.refused');
    }

    return stageWords(standing, send, wording);
}

function stageWords(standing: OutboxStanding, send: OutgoingMail | OutgoingMailPreview, wording: OutboxWording): string {
    return wording.translate(lineKeys[standing], {
        leaves: leavesAt(send.dueAt, wording),
        at: timeOf(send.availableAt, wording),
    });
}

/** The line a row draws under its subject, with how many attempts it took wherever one was made. */
export function standingLine(standing: OutboxStanding, send: OutgoingMail, wording: OutboxWording): string {
    const stage = stageWords(standing, send, wording);

    return send.attemptCount > 0 && standing !== 'sending'
        ? wording.translate('outbox.withAttempts', { stage, attempts: attemptsSaid(send.attemptCount, wording) })
        : stage;
}

/** The sentence a preview's state box says about the send. */
export function standingText(standing: OutboxStanding, send: OutgoingMailPreview, wording: OutboxWording): string {
    const { translate } = wording;
    const at = timeOf(send.recordedAt, wording);

    switch (standing) {
        case 'waiting':
            return translate('outbox.text.waiting', { at });
        case 'scheduled':
            return translate('outbox.text.scheduled', { at, leaves: leavesAt(send.dueAt, wording) });
        case 'retrying':
            return translate('outbox.text.retrying', {
                reason: send.failure ?? translate('outbox.notTaken'),
                attempts: attemptsSaid(send.attemptCount, wording),
            });
        case 'sending':
            return translate('outbox.text.sending');
        case 'unknown':
            return translate('outbox.text.unknown', { at });
        case 'refused':
            return send.failure ?? translate('outbox.notAccepted');
    }
}

/**
 * Whom a row names: the first recipient in `To`, else in `Cc`, else in `Bcc`, with how many more it is addressed to.
 *
 * The service already chose the recipients a row carries in that order, so the first of them is the one to name.
 */
export function addresseeOf(
    recipients: readonly OutgoingMailRecipient[],
    others: number,
    { locale, translate }: OutboxWording,
): string {
    const first = recipients[0]?.address ?? '';
    const more = recipients.length - 1 + others;

    return more > 0 ? translate('outbox.andOthers', { first, count: new Intl.NumberFormat(locale).format(more) }) : first;
}

/** The ink each tone draws a state's glyph and line in. */
export const toneInk: Readonly<Record<OutboxTone, string>> = {
    neutral: 'text-muted',
    accent: 'text-accent-deep',
    warning: 'text-warning-text',
    error: 'text-error-text',
};

/** The box a preview states the send's state in, and the ink of its title. */
export const toneBox: Readonly<Record<OutboxTone, { readonly box: string; readonly title: string }>> = {
    neutral: { box: 'border-line bg-rail', title: 'text-text' },
    accent: { box: 'border-accent-line bg-accent-soft', title: 'text-text' },
    warning: { box: 'border-warning bg-warning-soft', title: 'text-warning-text' },
    error: { box: 'border-error bg-error-soft', title: 'text-error-text' },
};
