// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What happened to the person while nobody was looking at their screen, and what each of the two ways of marking one
// read answers with.
//
// The three targets are all stated, because which one a producer chose is what a row is drawn by: a notification
// leading to a message, one leading to a screen, and one leading nowhere are three different rows rather than one row
// with a field that is sometimes absent.
//
// So are the shapes a row is *said* in. Each condition a deployment raises is here with the numbers it is stated
// with, because the sentence is the client's to compose and a corpus stating only one of them would leave the others
// undrawn; and one row names no condition at all, which is what a deployment upgraded over its own rows answers with
// and what a client draws the service's own two lines for.
//
// Three rows are failures that landed after the request asking for them had already answered — a send the server
// refused, a change the mailbox never took, and a draft whose copy on the server may be missing — and each leads to the
// thing somebody has to decide about rather than to a space. Their source is the account the failure belongs to, by
// its identifier, which is what the service writes there.

import { workAccount } from './deployment';
import { outOfStepDraft } from './drafts';
import { newsletterId } from './messages';
import { refusedOutgoingSend } from './outbox';

/** The identity the unread notification below is addressed by. */
export const notificationId = '00000000-0000-4000-8000-0000000000f0';

/** The change record the abandoned change below names, which is what a tab following that change knows it by. */
export const abandonedChangeRecordId = '00000000-0000-4000-8000-0000000000c1';

/** A send the mail server refused, leading to its entry in its account's Outbox. */
export const refusedSendNotification = {
    id: '00000000-0000-4000-8000-0000000000f5',
    kind: 'Mail',
    statement: { cause: 'SendRefused', counted: 1, outOf: null },
    title: 'Not sent - it waits in the Outbox',
    body: refusedOutgoingSend.subject,
    source: workAccount.id,
    target: { kind: 'OutgoingEmail', outgoingEmailId: refusedOutgoingSend.outgoingEmail },
    occurredAt: '2026-08-31T09:53:00+00:00',
    read: false,
};

/** A move the mail server never took, leading to the message it was asked of and naming the change record. */
export const abandonedChangeNotification = {
    id: '00000000-0000-4000-8000-0000000000f6',
    kind: 'Mail',
    statement: { cause: 'MailboxChangeAbandoned', counted: 4, outOf: null },
    title: 'A change did not reach your mailbox',
    body: 'Move - the mail server would not take it, so the message is still where the server holds it.',
    source: workAccount.id,
    target: { kind: 'MailboxChange', messageId: newsletterId, changeRecordId: abandonedChangeRecordId },
    occurredAt: '2026-08-31T09:40:00+00:00',
    read: false,
};

/** A draft whose copy in the drafts folder on the mail server may be missing, leading to the draft itself. */
export const draftCopyNotification = {
    id: '00000000-0000-4000-8000-0000000000f7',
    kind: 'Mail',
    statement: { cause: 'DraftCopyNotKept', counted: 1, outOf: null },
    title: 'Draft out of step with your server',
    body: outOfStepDraft.subject,
    source: workAccount.id,
    target: { kind: 'MailDraft', draftId: outOfStepDraft.draftId },
    occurredAt: '2026-08-31T09:20:00+00:00',
    read: false,
};

/**
 * One page of the centre, newest first, holding one row of each kind of target and both read states. The mail row is
 * a merged arrival, which is what more than five unread messages in one account become.
 */
export const notificationPage = {
    notifications: [
        refusedSendNotification,
        abandonedChangeNotification,
        draftCopyNotification,
        {
            id: notificationId,
            kind: 'Mail',
            statement: { cause: 'MailArrived', counted: 8, outOf: null },
            title: 'New mail',
            body: '8 new messages arrived.',
            source: 'Work',
            target: { kind: 'Screen', screen: 'Mail' },
            occurredAt: '2026-08-31T08:03:00+00:00',
            read: false,
        },
        {
            id: '00000000-0000-4000-8000-0000000000f1',
            kind: 'System',
            statement: { cause: 'CredentialRefused', counted: null, outOf: null },
            title: 'This account needs signing in again',
            body: 'The mail server refused the credential MailFathom holds, so this account is no longer being fetched.',
            source: 'Club',
            target: { kind: 'Screen', screen: 'Settings' },
            occurredAt: '2026-08-30T18:12:00+00:00',
            read: false,
        },
        {
            id: '00000000-0000-4000-8000-0000000000f2',
            kind: 'System',
            statement: { cause: 'SynchronizationIncomplete', counted: 2, outOf: 5 },
            title: 'Some mail could not be fetched',
            body: '2 of 5 folders did not finish. MailFathom will try again.',
            source: null,
            target: { kind: 'Nothing' },
            occurredAt: '2026-08-29T11:00:00+00:00',
            read: true,
        },
        {
            id: '00000000-0000-4000-8000-0000000000f3',
            kind: 'Task',
            statement: null,
            title: 'Four messages were filed into Receipts',
            body: 'The rule that files them ran while nobody was looking.',
            source: null,
            target: { kind: 'Message', messageId: newsletterId },
            occurredAt: '2026-08-28T07:40:00+00:00',
            read: true,
        },
    ],
    nextCursor: null,
};

/**
 * A notification raised after the page above was read, which is what a client polling the count finds arriving:
 * one message that came in, said by its sender and its subject and leading to the message itself rather than to the
 * space it sits in.
 */
export const arrivingNotification = {
    id: '00000000-0000-4000-8000-0000000000f4',
    kind: 'Mail',
    statement: { cause: 'MessageArrived', counted: 1, outOf: null },
    title: 'Lena Brandt',
    body: 'Quarterly figures for your review',
    source: 'Work',
    target: { kind: 'Message', messageId: newsletterId },
    occurredAt: '2026-08-31T09:12:00+00:00',
    read: false,
};

/** What a centre with nothing in it answers with, which a panel says something of its own about. */
export const emptyNotificationPage = { notifications: [], nextCursor: null };

/** What the count route answers on its own, which is the whole of what the bell draws. */
export const unreadNotificationCount = { unreadCount: 5 };

/** What marking {@link notificationId} read answers with: the row's new state, and what it leaves on the bell. */
export const notificationMarkedRead = { id: notificationId, read: true, unreadCount: 4 };

/** What marking the whole centre read answers with, which a client redraws both the panel and the bell from. */
export const everyNotificationMarkedRead = { markedRead: 5, unreadCount: 0 };
