// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailAccountOutbox } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { OutboxWording } from './outboxStanding';

// What the mark on an account's Sent row says: how many sends have not left that account yet, and its name, which
// says both of the numbers the service reports — waiting and refused — because the count drawn adds them up.

/** The mark as a row draws it, or nothing where no send waits and none was refused. */
export interface OutboxCount {
    /** The two numbers added up, in the reader's own digits. */
    readonly count: string;

    /** The mark's name, which says what the count is made of. */
    readonly said: string;

    /** Whether anything in it was refused, which is what turns the mark the colour of a failure. */
    readonly refused: boolean;
}

const waitingCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'outbox.waiting.other',
    one: 'outbox.waiting.one',
    two: 'outbox.waiting.other',
    few: 'outbox.waiting.few',
    many: 'outbox.waiting.many',
    other: 'outbox.waiting.other',
};

const refusedCounts: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'outbox.refused.other',
    one: 'outbox.refused.one',
    two: 'outbox.refused.other',
    few: 'outbox.refused.few',
    many: 'outbox.refused.many',
    other: 'outbox.refused.other',
};

export function outboxCountOf(
    outbox: MailAccountOutbox | null,
    { locale, translate }: Pick<OutboxWording, 'locale' | 'translate'>,
): OutboxCount | null {
    if (outbox === null || outbox.waiting + outbox.refused === 0) {
        return null;
    }

    const numbers = new Intl.NumberFormat(locale);
    const plurals = new Intl.PluralRules(locale);
    const waiting = translate(waitingCounts[plurals.select(outbox.waiting)], {
        count: numbers.format(outbox.waiting),
    });
    const refused = translate(refusedCounts[plurals.select(outbox.refused)], {
        count: numbers.format(outbox.refused),
    });

    const said =
        outbox.waiting > 0 && outbox.refused > 0
            ? translate('outbox.markBoth', { waiting, refused })
            : translate('outbox.markOne', { part: outbox.waiting > 0 ? waiting : refused });

    return { count: numbers.format(outbox.waiting + outbox.refused), said, refused: outbox.refused > 0 };
}
