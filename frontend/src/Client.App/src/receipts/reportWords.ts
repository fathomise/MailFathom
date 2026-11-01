// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailReport } from '@mailfathom/client-backend';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import type { Translate } from '../localization/useLocalization';
import { deliveryOutcomeOf, receiptInk, receiptMarksDrawn, wordOf, type RecipientOutcome } from './receiptMarks';

// A report that came back as mail is drawn from what it reports, never from the subject the reader's mail app or a mail
// server wrote: a headline in the subject's place on its row and at the head of its conversation, and one line the
// message is folded to until it is pressed. The words and the symbol are the receipt mark's, and so is the summary over
// several recipients.
//
// A report this rule cannot word — a read receipt saying the message was deleted unread, say, or a delivery report
// naming no outcome it knows — is drawn as the message it is, under its own subject, rather than as a report.

/** How a report is drawn wherever it stands. */
export interface ReportWords {
    readonly icon: IconName;

    /** The ink its symbol is drawn in. */
    readonly ink: string;

    /** What takes the subject's place on its row and at the head of its conversation. */
    readonly headline: string;

    /** The one line the message is folded to. */
    readonly line: string;
}

const deliveryWhat: Readonly<Record<Exclude<RecipientOutcome, 'read' | 'nothing'>, MessageKey>> = {
    failed: 'report.what.failed',
    delayed: 'report.what.delayed',
    delivered: 'report.what.delivered',
};

const deliveryOrder = ['failed', 'delayed', 'delivered'] as const;

/**
 * The words a report is drawn with, or `null` for a message that is no report or one this rule cannot word.
 *
 * @param report What the message was recognized as.
 * @param reader Who sent a read receipt, as the message names its own sender.
 * @param when When the report was written, already worded, or `null` where it carried no time.
 */
export function reportWordsOf(
    report: MailReport | null,
    reader: string,
    when: string | null,
    translate: Translate,
    locale: string,
): ReportWords | null {
    if (report === null) {
        return null;
    }

    const subject = report.ownMessageSubject ?? translate('list.noSubject');
    const dated = (what: string): string => (when === null ? what : translate('report.dated', { what, at: when }));

    if (report.kind === 'Read') {
        if (report.recipients.some((recipient) => recipient.outcome !== 'Displayed')) {
            return null;
        }

        return {
            icon: receiptMarksDrawn.read.icon,
            ink: receiptInk[receiptMarksDrawn.read.tone],
            headline: report.namesOwnMessage
                ? translate('report.headline.read', { reader, subject })
                : translate('report.headline.readStray', { reader }),
            line: dated(translate('report.line.read', { reader })),
        };
    }

    const outcomes = report.recipients.map((recipient) => deliveryOutcomeOf(recipient.outcome));
    const mark = deliveryOrder.find((outcome) => outcomes.includes(outcome));
    const only = report.recipients.length === 1 ? report.recipients[0] : undefined;

    if (mark === undefined || outcomes.includes(null)) {
        return null;
    }

    const what =
        only === undefined
            ? wordOf(
                  {
                      mark,
                      count: outcomes.filter((outcome) => outcome === mark).length,
                      of: outcomes.length,
                  },
                  translate,
                  locale,
                  { counted: true },
              )
            : translate(deliveryWhat[mark], { address: only.address });

    return {
        icon: receiptMarksDrawn[mark].icon,
        ink: receiptInk[receiptMarksDrawn[mark].tone],
        headline: report.namesOwnMessage
            ? translate('report.headline.delivery', { what, subject })
            : translate('report.headline.deliveryStray', { what: lowerFirst(what, locale) }),
        line: dated(what),
    };
}

/**
 * Who sent a read receipt: the name its sender gave, else the address it came from, else the one recipient it names.
 *
 * The reader of a read receipt is that receipt's own sender, so this is the message's author rather than a field of the
 * report — and a delivery report, which names no reader, never reads it.
 */
export function readerOf(
    report: MailReport,
    senderDisplayName: string | null,
    senderAddress: string | null,
    translate: Translate,
): string {
    return senderDisplayName ?? senderAddress ?? report.recipients[0]?.address ?? translate('list.senderUnknown');
}

/** The message a headline is worded for: what it was recognized as, and who sent it. */
export interface ReportedMessage {
    readonly report: MailReport | null;
    readonly senderDisplayName: string | null;
    readonly senderAddress: string | null;
}

/**
 * What takes the subject's place for a report on its row and at the head of its conversation, or `null` for a message
 * that keeps its own subject — every message that is no report, and a report this rule cannot word.
 */
export function reportHeadlineOf(message: ReportedMessage | null, translate: Translate, locale: string): string | null {
    const report = message?.report ?? null;

    if (message === null || report === null) {
        return null;
    }

    const reader = readerOf(report, message.senderDisplayName, message.senderAddress, translate);

    return reportWordsOf(report, reader, null, translate, locale)?.headline ?? null;
}

// The headline that names no message carries what the report says in the middle of a sentence, where it begins with
// the small letter it would take there.
function lowerFirst(words: string, locale: string): string {
    return words.charAt(0).toLocaleLowerCase(locale) + words.slice(1);
}
