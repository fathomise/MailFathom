// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientNotification, MailAccount, NotificationCause } from '@mailfathom/client-backend';
import { wordReminderRemaining } from '../reminders/reminderWords';
import { accountNameIn } from '../shell/accountNames';
import { abandonedActOf, failureOf, type AbandonedAct, type Failure } from './failures';
import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';

// What a notification says, in the language the person reading it has. The deployment sends the condition and the
// numbers it is stated with; the sentence is the client's, which is what `frontend/src/AGENTS.md` § *The package
// boundary* means by language being a rendering decision — the same row read in two languages is one record and two
// screens rather than two records.
//
// Each cause is a sentence with holes rather than fragments joined here, because where a count falls in a sentence and
// what form the noun around it takes are both the language's business. `Intl.PluralRules` selects the form, which
// Polish needs for every counted sentence below and English hides.
//
// **The English the deployment sent is the fallback and nothing more.** A record written before this deployment kept
// conditions has no statement, and so does one raised for a condition a newer deployment knows and this client does
// not. Neither is a defect: the row is still worth drawing, and the service's own two lines are what it is drawn with
// until retention has taken the first kind and an upgrade has answered the second.

/** The two lines a row is drawn with, whichever of the two sources they came from. */
export interface NotificationWords {
    readonly title: string;
    readonly body: string;
}

/**
 * How each cause is titled.
 *
 * Most causes are a condition, and a condition's headline is the same sentence for everybody it happens to. A
 * reminder names the record it announces: what it is about is one event or one task, and what that record is called is
 * the person's own text rather than anything a catalogue could hold — so its title is a sentence with the record's
 * name in it, filled from the headline the deployment already derived. Mail that arrived is the other exception, in
 * both of its shapes: one message is headed by its sender, which is the message's own words and no sentence at all,
 * and merged arrivals are headed by how many there were. A cause whose title changes shape fails to compile here until
 * somebody has decided which.
 */
type CauseTitle =
    | { readonly said: MessageKey }
    | { readonly naming: MessageKey }
    | { readonly counted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> }
    | { readonly sender: true };

const causeTitles: Readonly<Record<NotificationCause, CauseTitle>> = {
    MailArrived: { counted: forms('notifications.said.mailArrived.title') },
    SynchronizationIncomplete: { said: 'notifications.said.synchronizationIncomplete.title' },
    CredentialRefused: { said: 'notifications.said.credentialRefused.title' },
    CalendarReminderDue: { naming: 'notifications.said.calendarReminderDue.title' },
    TaskReminderDue: { naming: 'notifications.said.taskReminderDue.title' },
    MessageArrived: { sender: true },
    SendRefused: { said: 'notifications.said.sendRefused.title' },
    MailboxChangeAbandoned: { said: 'notifications.said.mailboxChangeAbandoned.title' },
    DraftCopyNotKept: { said: 'notifications.said.draftCopyNotKept.title' },
};

/**
 * How each cause's second line is said.
 *
 * A cause that counts nothing carries one sentence, and a cause that counts carries the forms its count takes. The
 * two shapes are a union rather than an optional field so that a cause cannot declare forms nothing selects between,
 * and so that reading one is a switch rather than a check for which field is present.
 */
type CauseBody =
    | { readonly said: MessageKey }
    | { readonly counted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> }
    | { readonly remaining: true }
    | { readonly subject: MessageKey }
    | { readonly act: true };

const causeBodies: Readonly<Record<NotificationCause, CauseBody>> = {
    MailArrived: { said: 'notifications.said.mailArrived.body' },
    SynchronizationIncomplete: { counted: forms('notifications.said.synchronizationIncomplete.body') },
    CredentialRefused: { said: 'notifications.said.credentialRefused.body' },
    // A third shape rather than a fourth set of counted forms, because a lead is said in the coarsest whole unit it
    // states exactly — minutes, hours, or days — and which of the three that is follows the number rather than the
    // cause. `reminders/reminderWords.ts` is the one place that reading lives, so the panel a lead was set in and the
    // row it is announced on cannot come to word it differently — and the two kinds of reminder share it, because how
    // long is left is the same reading whatever the lead was measured back from.
    CalendarReminderDue: { remaining: true },
    TaskReminderDue: { remaining: true },
    // The subject is the message's own words, as its sender is; what a message without one says is the client's
    // sentence, which is what the count is there to tell apart. A refused send and a draft out of step are read the
    // same way, each with its own sentence for a message nobody gave a subject.
    MessageArrived: { subject: 'list.noSubject' },
    SendRefused: { subject: 'notifications.said.sendRefused.untitled' },
    DraftCopyNotKept: { subject: 'notifications.said.draftCopyNotKept.untitled' },
    // A given-up change carries no mail at all — it leads to the message, which says what it is — so what it says is
    // which act did not happen, counted as the statement's number.
    MailboxChangeAbandoned: { act: true },
};

/** What a given-up change says it was, one sentence per act, because the act is where the sentence starts. */
const abandonedActBodies: Readonly<Record<AbandonedAct, MessageKey>> = {
    markRead: 'notifications.said.mailboxChangeAbandoned.markRead',
    markUnread: 'notifications.said.mailboxChangeAbandoned.markUnread',
    flag: 'notifications.said.mailboxChangeAbandoned.flag',
    unflag: 'notifications.said.mailboxChangeAbandoned.unflag',
    move: 'notifications.said.mailboxChangeAbandoned.move',
    delete: 'notifications.said.mailboxChangeAbandoned.delete',
    copy: 'notifications.said.mailboxChangeAbandoned.copy',
    changeTags: 'notifications.said.mailboxChangeAbandoned.changeTags',
};

/**
 * Where each failure says it came from, with the account it belongs to.
 *
 * The deployment writes the account's identifier there, and an identifier is never shown: the account is named by
 * what a reader knows it by, and where the client holds no name for it the place is said alone.
 */
const failureSources: Readonly<Record<Failure['kind'], { readonly place: MessageKey; readonly named: MessageKey }>> = {
    send: { place: 'folder.outbox', named: 'notifications.source.outbox' },
    change: { place: 'notifications.kind.mail', named: 'notifications.source.mail' },
    draft: { place: 'folder.drafts', named: 'notifications.source.drafts' },
};

/**
 * The source line one notification is drawn with: where a failure came from, the deployment's own line for anything
 * else, or `null` where it states none and the kind is the whole of it.
 */
export function wordNotificationSource(
    notification: ClientNotification,
    accounts: readonly MailAccount[],
    translate: Translate,
): string | null {
    const failure = failureOf(notification);

    if (failure === null) {
        return notification.source;
    }

    const said = failureSources[failure.kind];
    const account = notification.source === null ? null : accountNameIn(accounts, notification.source);

    return account === null ? translate(said.place) : translate(said.named, { account });
}

/** What a toast about one notification says and the act it offers, which for a failure is not its row's second line. */
export interface NotificationToastWords {
    readonly title: string;
    readonly body: string;
    readonly action: string;
}

/**
 * Says what the toast raised for one notification says, while the client is open as it arrives.
 *
 * A failure's toast says where the thing now stands and the way to it, which is more than its row says: a refused
 * send is named and placed in its account's Outbox. A given-up change carries no subject to name, so its toast says
 * what its row says. Every other notification's toast is its row.
 */
export function wordNotificationToast(
    notification: ClientNotification,
    accounts: readonly MailAccount[],
    locale: Locale,
    translate: Translate,
): NotificationToastWords {
    const said = wordNotification(notification, locale, translate);
    const failure = failureOf(notification);
    const subject = notification.statement?.counted === 1 ? notification.body : null;
    const account = notification.source === null ? null : accountNameIn(accounts, notification.source);

    switch (failure?.kind) {
        case 'send':
            return {
                title: said.title,
                body:
                    account === null
                        ? said.body
                        : subject === null
                          ? translate('notifications.toast.sendRefusedUntitled', { account })
                          : translate('notifications.toast.sendRefused', { subject, account }),
                action: translate('compose.showInOutbox'),
            };
        case 'change':
            return { ...said, action: translate('notifications.showMessage') };
        case 'draft':
            return {
                title: said.title,
                body: subject === null ? said.body : translate('notifications.toast.draftCopyNotKept', { subject }),
                action: translate('notifications.openDraft'),
            };
        case undefined:
            return { ...said, action: translate('notifications.show') };
    }
}

/**
 * Says what one notification says, in the reader's own language where the deployment named a condition.
 *
 * @param notification What happened.
 * @param locale The language and the region the count is selected and formatted under.
 * @param translate Reads one message under that language.
 * @returns The two lines the row is drawn with.
 */
export function wordNotification(
    notification: ClientNotification,
    locale: Locale,
    translate: Translate,
): NotificationWords {
    const statement = notification.statement;

    if (statement === null) {
        return { title: notification.title, body: notification.body };
    }

    // Which number the sentence is counted by is the cause's own: mail counts what arrived, and an unfinished run
    // counts against how much it scheduled, which is the number the noun in that sentence agrees with.
    const counted = statement.cause === 'SynchronizationIncomplete' ? statement.outOf : statement.counted;
    const holes = { count: number(statement.counted, locale), outOf: number(statement.outOf, locale) };
    const form = new Intl.PluralRules(locale).select(counted ?? 0);

    return {
        title: wordTitle(causeTitles[statement.cause], notification.title, form, holes, translate),
        body: wordBody(
            causeBodies[statement.cause],
            notification.body,
            statement.counted,
            form,
            holes,
            locale,
            translate,
        ),
    };
}

function wordTitle(
    title: CauseTitle,
    headline: string,
    form: Intl.LDMLPluralRule,
    holes: Readonly<Record<string, string>>,
    translate: Translate,
): string {
    if ('said' in title) {
        return translate(title.said);
    }

    if ('naming' in title) {
        return translate(title.naming, { subject: headline });
    }

    return 'counted' in title ? translate(title.counted[form], holes) : headline;
}

function wordBody(
    body: CauseBody,
    line: string,
    counted: number | null,
    form: Intl.LDMLPluralRule,
    holes: Readonly<Record<string, string>>,
    locale: Locale,
    translate: Translate,
): string {
    if ('said' in body) {
        return translate(body.said);
    }

    if ('remaining' in body) {
        return wordReminderRemaining(counted ?? 0, locale, translate);
    }

    if ('subject' in body) {
        return counted === 0 ? translate(body.subject) : line;
    }

    if ('act' in body) {
        const act = abandonedActOf(counted);

        return act === null ? line : translate(abandonedActBodies[act]);
    }

    return translate(body.counted[form], holes);
}

/** The four forms one counted line is said in, which every counted line declares the same way. */
function forms(
    key: 'notifications.said.mailArrived.title' | 'notifications.said.synchronizationIncomplete.body',
): Readonly<Record<Intl.LDMLPluralRule, MessageKey>> {
    return {
        zero: `${key}.other`,
        one: `${key}.one`,
        two: `${key}.other`,
        few: `${key}.few`,
        many: `${key}.many`,
        other: `${key}.other`,
    };
}

/**
 * Writes one of a statement's numbers in the reader's own digits and grouping.
 *
 * A hole a cause does not fill is written as nothing rather than as a zero, so a sentence that names it would read as
 * an obviously missing value instead of as a count somebody could act on.
 */
function number(value: number | null, locale: Locale): string {
    return value === null ? '' : new Intl.NumberFormat(locale).format(value);
}
