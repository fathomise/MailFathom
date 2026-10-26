// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientNotification, MailAccount, NotificationStatement } from '@mailfathom/client-backend';
import { catalogues, type Locale } from '../localization/locale';
import type { Translate } from '../localization/useLocalization';
import { wordNotification, wordNotificationSource, wordNotificationToast } from './notificationWords';

// What the deployment sends is a condition and its numbers, so this is where it becomes a sentence — and the sentence
// is asserted as the literal words each language actually reads, in both of them. Anything less would pass for a
// client that answered a Polish reader in English, which is the defect this exists to refuse.
//
// The catalogue is read directly rather than through a rendered provider, because `wordNotification` takes the
// translation as a parameter: that is the seam, and reaching it needs no screen.

const englishArrival = {
    id: 'n-1',
    kind: 'Mail',
    title: 'New mail',
    body: '3 new messages arrived.',
    source: 'work',
    target: { kind: 'Screen', screen: 'Mail' },
    occurredAt: '2026-09-04T11:55:00+00:00',
    read: false,
} as const;

function said(statement: NotificationStatement | null, locale: Locale) {
    const notification: ClientNotification = { ...englishArrival, statement };

    return wordNotification(notification, locale, translating(locale));
}

// The digits a count is written in, asked of `Intl` the same way the sentence asks it rather than typed out here.
const digits = new Intl.NumberFormat('pl');

/** Reads the catalogue the way the provider does, which is a lookup and a hole filled. */
function translating(locale: Locale): Translate {
    return (key, values) =>
        catalogues[locale][key].replace(/\{(\w+)\}/gu, (hole: string, name: string) => values?.[name] ?? hole);
}

describe('wordNotification', () => {
    it('says merged arrivals in English, counting every message they stand for', () => {
        expect(said({ cause: 'MailArrived', counted: 8, outOf: null }, 'en')).toEqual({
            title: '8 new messages',
            body: 'More than five arrived unread, so they are gathered here.',
        });
    });

    // Polish inflects the noun by the count, which is what `Intl.PluralRules` selects between and what a hand-written
    // plural would get wrong for every number but the one it was written for. Which form it lands on is named as the
    // entry rather than as the words that entry holds: the catalogue is the one place Polish prose is written, and a
    // copy of a sentence here would be a second place to keep it in step. The body beside it stays literal, so the
    // whole assertion is still that the Polish catalogue reached the screen rather than the English.
    it.each([
        [6, 'notifications.said.mailArrived.title.many'],
        [22, 'notifications.said.mailArrived.title.few'],
    ] as const)('says merged arrivals in Polish in the form their count takes: %i', (count, form) => {
        expect(said({ cause: 'MailArrived', counted: count, outOf: null }, 'pl')).toEqual({
            title: translating('pl')(form, { count: digits.format(count) }),
            body: 'Nieprzeczytanych przyszło więcej niż pięć, więc są zebrane tutaj.',
        });
    });

    // One message's row is its sender and its subject, which are the message's own words rather than a sentence, so
    // both languages draw them as they arrived — and only a missing subject is the client's to say.
    it.each(['en', 'pl'] as const)('says one arrived message in %s as its sender and its subject', (locale) => {
        const message: ClientNotification = {
            ...englishArrival,
            title: 'Anna Kowalska',
            body: 'Re: Contract addendum',
            target: { kind: 'Message', storedEmailId: '0197a3c0-0000-7000-8000-000000000003' },
            statement: { cause: 'MessageArrived', counted: 1, outOf: null },
        };

        expect(wordNotification(message, locale, translating(locale))).toEqual({
            title: 'Anna Kowalska',
            body: 'Re: Contract addendum',
        });
    });

    it.each([
        ['en', 'No subject'],
        ['pl', 'Bez tematu'],
    ] as const)('says a message that carried no subject in %s in its reader’s language', (locale, body) => {
        const message: ClientNotification = {
            ...englishArrival,
            title: 'Anna Kowalska',
            body: 'No subject',
            target: { kind: 'Message', storedEmailId: '0197a3c0-0000-7000-8000-000000000003' },
            statement: { cause: 'MessageArrived', counted: 0, outOf: null },
        };

        expect(wordNotification(message, locale, translating(locale)).body).toBe(body);
    });

    it('says an unfinished run in English, against how much it had scheduled', () => {
        expect(said({ cause: 'SynchronizationIncomplete', counted: 2, outOf: 5 }, 'en')).toEqual({
            title: 'Some mail could not be fetched',
            body: '2 of 5 folders did not finish. MailFathom will try again.',
        });
    });

    // The noun agrees with what the run scheduled rather than with what failed, so one folder out of one reads
    // differently from one folder out of five — which is the case a sentence counted by the wrong number gets wrong.
    it.each([
        [1, 1, 'notifications.said.synchronizationIncomplete.body.one'],
        [2, 5, 'notifications.said.synchronizationIncomplete.body.many'],
    ] as const)('says an unfinished run in Polish against its scheduled count: %i of %i', (failed, scheduled, form) => {
        expect(said({ cause: 'SynchronizationIncomplete', counted: failed, outOf: scheduled }, 'pl')).toEqual({
            title: 'Nie udało się pobrać części poczty',
            body: translating('pl')(form, { count: digits.format(failed), outOf: digits.format(scheduled) }),
        });
    });

    it.each([
        ['en', 'This account needs signing in again'],
        ['pl', 'To konto wymaga ponownego zalogowania'],
    ] as const)('says a refused credential in %s without counting anything', (locale, title) => {
        expect(said({ cause: 'CredentialRefused', counted: null, outOf: null }, locale).title).toBe(title);
    });

    // The one cause whose headline names the record it is about, because what a reminder is about is one event and
    // what that event is called is the person's own text rather than anything a catalogue could hold. The deployment
    // sends it as the headline, and each language puts it where its own sentence wants it.
    it.each([
        ['en', 'Reminder: Design review'],
        ['pl', 'Przypomnienie: Design review'],
    ] as const)('says a due reminder in %s with the event it is about in the headline', (locale, title) => {
        const reminder: ClientNotification = {
            ...englishArrival,
            kind: 'Calendar',
            title: 'Design review',
            body: '15 minutes left.',
            target: { kind: 'CalendarEvent', calendarEventId: '0197a3c0-0000-7000-8000-000000000001' },
            statement: { cause: 'CalendarReminderDue', counted: 15, outOf: null },
        };

        expect(wordNotification(reminder, locale, translating(locale)).title).toBe(title);
    });

    // A task's due date announces the same way, because what a reminder is about is the headline and the target
    // rather than a second sentence: the record it names is a task, and the row says so by leading to one.
    it.each([
        ['en', 'Reminder: Send the counter-proposal'],
        ['pl', 'Przypomnienie: Send the counter-proposal'],
    ] as const)('says a due task reminder in %s with the task it is about in the headline', (locale, title) => {
        const reminder: ClientNotification = {
            ...englishArrival,
            kind: 'Task',
            title: 'Send the counter-proposal',
            body: '1 day left.',
            target: { kind: 'PersonalTask', taskId: '0197a3c0-0000-7000-8000-000000000002' },
            statement: { cause: 'TaskReminderDue', counted: 24 * 60, outOf: null },
        };

        expect(wordNotification(reminder, locale, translating(locale)).title).toBe(title);
    });

    it('says what is left before a task the same way it says it before an event', () => {
        expect(said({ cause: 'TaskReminderDue', counted: 24 * 60, outOf: null }, 'en').body).toBe('1 day left.');
    });

    // The lead is said in the coarsest whole unit that states it exactly, which is the same reading the panel a lead
    // was set in uses — so the row and the chip cannot come to word one lead two ways.
    it.each([
        [0, 'en', 'Starting now.'],
        [15, 'en', '15 minutes left.'],
        [120, 'en', '2 hours left.'],
        [2 * 24 * 60, 'pl', 'Zostały 2 dni.'],
        [0, 'pl', 'Zaczyna się teraz.'],
    ] as const)('says what is left before an event %i minutes ahead, in %s', (minutesBefore, locale, body) => {
        expect(said({ cause: 'CalendarReminderDue', counted: minutesBefore, outOf: null }, locale).body).toBe(body);
    });

    // A record written before this deployment kept conditions, and one raised for a condition a newer deployment
    // knows, both reach a screen the same way: as the service's own English, which is better than a row nobody draws.
    it('falls back to what the deployment sent where it named no condition', () => {
        expect(said(null, 'pl')).toEqual({ title: 'New mail', body: '3 new messages arrived.' });
    });
});

const work: MailAccount = {
    id: 'work',
    displayName: 'Nordwind · work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: null,
    behind: false,
    filesJunk: false,
};

const refusal: ClientNotification = {
    ...englishArrival,
    title: 'Not sent - it waits in the Outbox',
    body: 'Re: Contract addendum',
    statement: { cause: 'SendRefused', counted: 1, outOf: null },
    target: { kind: 'OutgoingEmail', outgoingEmailId: 'o-1' },
};

describe('wordNotificationSource', () => {
    it('names the account a failure came from by the name a reader knows it by, never its identifier', () => {
        expect(wordNotificationSource(refusal, [work], translating('en'))).toBe('Outbox · Nordwind · work');
    });

    it('says the place alone where the client holds no name for the account', () => {
        expect(wordNotificationSource(refusal, [], translating('en'))).toBe('Outbox');
    });

    it('keeps the deployment’s own line for a notification that reports no failure', () => {
        expect(wordNotificationSource({ ...englishArrival, statement: null }, [], translating('en'))).toBe('work');
    });
});

describe('wordNotificationToast', () => {
    it('places a refused send in its account’s Outbox and offers the way there', () => {
        expect(wordNotificationToast(refusal, [work], 'en', translating('en'))).toEqual({
            title: 'Not sent - it waits in the Outbox',
            body: '“Re: Contract addendum” is in the Outbox of Nordwind · work. Send it again or edit it there.',
            action: 'Show in Outbox',
        });
    });

    it('says which act a change the mailbox did not take was, and offers its message', () => {
        const change: ClientNotification = {
            ...englishArrival,
            statement: { cause: 'MailboxChangeAbandoned', counted: 2, outOf: null },
            target: { kind: 'MailboxChange', storedEmailId: 'e-1', changeRecordId: 'r-1' },
        };

        expect(wordNotificationToast(change, [work], 'en', translating('en'))).toMatchObject({
            title: 'A change did not reach your mailbox',
            action: 'Show message',
        });
    });
});
