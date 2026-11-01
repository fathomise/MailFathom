// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type {
    MailParticipant,
    MailReceiptOutcome,
    MailReceipts,
    MailReceiptSummary,
    MailRecipientReceipts,
} from '@mailfathom/client-backend';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import type { Translate } from '../localization/useLocalization';

// The one mark a message the account sent carries for what came back, chosen by one rule wherever it is drawn: on a Sent
// row from the counts the row carries, and on an opened message from every recipient's own reports.
//
// A recipient's outcome is the furthest a report took it — read, then failed, delayed or delivered, then nothing back —
// and the message's mark is the outcome that most needs the sender: a failure, then a delay, then read, then delivered,
// then nothing back. A failure outranks a read because it is the one that asks for something. With several recipients
// the word counts the ones it names and drops the count where it names them all.

/** What came back for one recipient, the furthest a report took it. */
export type RecipientOutcome = 'failed' | 'delayed' | 'read' | 'delivered' | 'nothing';

/** The mark a message carries, which is the outcome that most needs its sender. */
export type ReceiptMark = 'failed' | 'delayed' | 'read' | 'delivered' | 'requested';

/** A message's mark, and how many of its recipients the mark's word names out of how many there are. */
export interface ReceiptStanding {
    readonly mark: ReceiptMark;
    readonly count: number;
    readonly of: number;
}

type Tone = 'error' | 'warning' | 'healthy' | 'plain' | 'faint';

/** How each mark is drawn: its symbol, its tone, its word, and the word that counts. */
export const receiptMarksDrawn: Readonly<
    Record<
        ReceiptMark,
        {
            readonly icon: IconName;
            readonly tone: Tone;
            readonly word: MessageKey;
            readonly counted: MessageKey;
        }
    >
> = {
    failed: { icon: 'error', tone: 'error', word: 'receipt.word.failed', counted: 'receipt.counted.failed' },
    delayed: {
        icon: 'hourglass_top',
        tone: 'warning',
        word: 'receipt.word.delayed',
        counted: 'receipt.counted.delayed',
    },
    read: { icon: 'done_all', tone: 'healthy', word: 'receipt.word.read', counted: 'receipt.counted.read' },
    delivered: {
        icon: 'done',
        tone: 'plain',
        word: 'receipt.word.delivered',
        counted: 'receipt.counted.delivered',
    },
    requested: {
        icon: 'schedule',
        tone: 'faint',
        word: 'receipt.word.requested',
        counted: 'receipt.word.requested',
    },
};

/** How one recipient's outcome is drawn in the details and named in the mark's name. */
export const recipientOutcomesDrawn: Readonly<
    Record<
        RecipientOutcome,
        { readonly icon: IconName; readonly tone: Tone; readonly word: MessageKey; readonly each: MessageKey }
    >
> = {
    failed: { icon: 'error', tone: 'error', word: 'receipt.word.failed', each: 'receipt.each.failed' },
    delayed: { icon: 'hourglass_top', tone: 'warning', word: 'receipt.word.delayed', each: 'receipt.each.delayed' },
    read: { icon: 'done_all', tone: 'healthy', word: 'receipt.word.read', each: 'receipt.each.read' },
    delivered: { icon: 'done', tone: 'plain', word: 'receipt.word.delivered', each: 'receipt.each.delivered' },
    nothing: { icon: 'schedule', tone: 'faint', word: 'receipt.word.nothing', each: 'receipt.each.nothing' },
};

/** The ink a mark's symbol is drawn in where it stands alone. */
export const receiptInk: Readonly<Record<Tone, string>> = {
    error: 'text-error-text',
    warning: 'text-warning-text',
    healthy: 'text-healthy-text',
    plain: 'text-muted',
    faint: 'text-muted',
};

/** The fill and ink of a badge, and of the tile the details open with. */
export const receiptPill: Readonly<Record<Tone, string>> = {
    error: 'border-error bg-error-soft text-error-text',
    warning: 'border-warning bg-warning-soft text-warning-text',
    healthy: 'border-transparent bg-healthy-soft text-healthy-text',
    plain: 'border-line bg-sunken text-muted',
    faint: 'border-dashed border-line-strong bg-transparent text-muted',
};

const markOrder: readonly Exclude<RecipientOutcome, 'nothing'>[] = ['failed', 'delayed', 'read', 'delivered'];

/** What a delivery report's action amounts to for its recipient, or `null` for an action that is no delivery outcome. */
export function deliveryOutcomeOf(outcome: MailReceiptOutcome): Exclude<RecipientOutcome, 'read' | 'nothing'> | null {
    switch (outcome) {
        case 'Delivered':
        case 'Relayed':
        case 'Expanded':
            return 'delivered';
        case 'Failed':
            return 'failed';
        case 'Delayed':
            return 'delayed';
        default:
            return null;
    }
}

/** The furthest one recipient's reports took them. Only a message reported displayed is read. */
export function recipientOutcomeOf(recipient: MailRecipientReceipts): RecipientOutcome {
    if (recipient.read?.outcome === 'Displayed') {
        return 'read';
    }

    return (recipient.delivery === null ? null : deliveryOutcomeOf(recipient.delivery.outcome)) ?? 'nothing';
}

/**
 * The mark an opened message carries for what came back, which names a request nothing answered yet — or `null` for a
 * message that asked for nothing and got nothing.
 */
export function standingOfReceipts(receipts: MailReceipts | null): ReceiptStanding | null {
    if (receipts === null || receipts.recipients.length === 0) {
        return null;
    }

    const outcomes = receipts.recipients.map(recipientOutcomeOf);
    const counted = (outcome: RecipientOutcome): number => outcomes.filter((each) => each === outcome).length;

    return (
        standingOf(
            {
                failed: counted('failed'),
                delayed: counted('delayed'),
                read: counted('read'),
                delivered: counted('delivered'),
            },
            outcomes.length,
        ) ??
        (receipts.deliveryRequested || receipts.readRequested
            ? { mark: 'requested', count: 0, of: outcomes.length }
            : null)
    );
}

/**
 * The mark a Sent row carries, from the counts a row is served — or `null` where nothing came back, which a row draws
 * nothing for: most read receipts never come, so a mark for none would stand for ever beside every request.
 */
export function standingOfSummary(summary: MailReceiptSummary | null): ReceiptStanding | null {
    if (summary === null) {
        return null;
    }

    return standingOf(
        {
            failed: summary.delivery.failed,
            delayed: summary.delivery.delayed,
            read: summary.read.read,
            delivered: summary.delivery.delivered,
        },
        summary.recipients,
    );
}

/**
 * The mark's word: the count it names out of every recipient, or the word alone where it names them all — and alone
 * for a request nothing answered, which counts nobody. A report naming several recipients counts them even where it
 * names them all, which is what `counted` asks for.
 */
export function wordOf(
    standing: ReceiptStanding,
    translate: Translate,
    locale: string,
    { counted = false }: { readonly counted?: boolean } = {},
): string {
    const drawn = receiptMarksDrawn[standing.mark];
    const numbers = new Intl.NumberFormat(locale);

    return standing.mark === 'requested' || (standing.count === standing.of && !counted)
        ? translate(drawn.word)
        : translate(drawn.counted, { count: numbers.format(standing.count), of: numbers.format(standing.of) });
}

/** Who one recipient is, as the message names them among its recipients, or their address where it gives no name. */
export function recipientNameOf(address: string, participants: readonly MailParticipant[]): string {
    const named = participants.find(
        (participant) =>
            (participant.role === 'To' || participant.role === 'Cc' || participant.role === 'Bcc') &&
            participant.address.toLowerCase() === address.toLowerCase(),
    );

    return named?.displayName ?? address;
}

/**
 * The mark's whole name: its word, then every recipient's own outcome — what a screen reader hears for the badge, and
 * what the word beside its symbol shortens.
 */
export function receiptNameOf(
    standing: ReceiptStanding,
    receipts: MailReceipts,
    participants: readonly MailParticipant[],
    translate: Translate,
    locale: string,
): string {
    const recipients = new Intl.ListFormat(locale, { type: 'unit', style: 'short' }).format(
        receipts.recipients.map((recipient) =>
            translate(recipientOutcomesDrawn[recipientOutcomeOf(recipient)].each, {
                name: recipientNameOf(recipient.address, participants),
            }),
        ),
    );

    return translate('receipt.name', {
        word: standing.mark === 'requested' ? translate('receipt.requestedName') : wordOf(standing, translate, locale),
        recipients,
    });
}

function standingOf(counts: Readonly<Record<(typeof markOrder)[number], number>>, of: number): ReceiptStanding | null {
    const mark = markOrder.find((outcome) => counts[outcome] > 0);

    return mark === undefined ? null : { mark, count: counts[mark], of };
}
