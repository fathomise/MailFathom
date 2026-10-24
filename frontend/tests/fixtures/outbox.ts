// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What has not left the corpus mailbox yet: its Outbox, one send in each state a row is drawn in.
//
// Four of the five are still on their way and one was refused, which is what the folders route counts for the mailbox
// in `deployment.ts` — the mark on the Sent row and the list it opens describe the same five sends.
//
// A row names its recipients as the service does: those of the first role that names anybody — `To`, else `Cc`, else
// `Bcc` — and a count of the rest. Everyone a send is for is the preview's to name, so the recipients a row leaves to
// that count are kept beside the rows rather than on them.

import { workAccount } from './deployment';

/** A send handed over a moment ago, with no attempt made on it yet. */
export const waitingSend = {
    outgoingEmail: '00000000-0000-4000-8000-0000000000f3',
    account: workAccount.id,
    stage: 'Recorded',
    heldByAttempt: false,
    recipients: [{ address: 't.bak@fabrikam.example', role: 'To' }],
    otherRecipientCount: 0,
    subject: 'Re: UAT testing date',
    recordedAt: '2026-08-31T10:44:00+00:00',
    dueAt: null,
    dueTimeZone: null,
    availableAt: '2026-08-31T10:44:00+00:00',
    attemptCount: 0,
    lastFailureCode: null,
    lastReplyCode: null,
    failure: null,
};

/** A send an attempt holds right now, which nobody may decide anything about until the attempt ends. */
export const sendingSend = {
    ...waitingSend,
    outgoingEmail: '00000000-0000-4000-8000-0000000000f2',
    heldByAttempt: true,
    recipients: [{ address: 'm.nowak@company.example', role: 'To' }],
    subject: 'Budget line for Q4 renewals',
    recordedAt: '2026-08-31T10:41:00+00:00',
    availableAt: '2026-08-31T10:41:00+00:00',
    attemptCount: 1,
};

/** A send whose attempts failed before the message went out, waiting for the next one. */
export const retryingSend = {
    ...waitingSend,
    outgoingEmail: '00000000-0000-4000-8000-0000000000f6',
    recipients: [{ address: 'p.zielinski@company.example', role: 'To' }],
    otherRecipientCount: 1,
    subject: 'Re: rollout schedule',
    recordedAt: '2026-08-31T10:12:00+00:00',
    availableAt: '2026-08-31T10:55:00+00:00',
    attemptCount: 2,
    lastFailureCode: 2,
    failure: 'The mail server did not answer.',
};

/** A send whose message went out with no answer coming back, so nobody knows whether it arrived. */
export const unknownSend = {
    ...waitingSend,
    outgoingEmail: '00000000-0000-4000-8000-0000000000f7',
    stage: 'TransmissionBegun',
    recipients: [{ address: 'a.kowalska@contoso.example', role: 'To' }],
    subject: 'Re: proposed terms for 2027',
    recordedAt: '2026-08-31T09:58:00+00:00',
    availableAt: '2026-08-31T09:58:00+00:00',
    attemptCount: 1,
};

/** A send the mail server would not take, carrying its reason in words. */
export const refusedOutgoingSend = {
    ...waitingSend,
    outgoingEmail: '00000000-0000-4000-8000-0000000000f1',
    stage: 'Refused',
    recipients: [{ address: 'a.kowalska@contoso.example', role: 'To' }],
    otherRecipientCount: 1,
    subject: 'Re: Contract addendum - signatures',
    recordedAt: '2026-08-31T09:52:00+00:00',
    availableAt: '2026-08-31T09:52:00+00:00',
    attemptCount: 1,
    lastFailureCode: 5,
    lastReplyCode: 554,
    failure: 'The mail server would not take it: this account has sent as much mail as it may today.',
};

/** The recipients each send's row counts rather than names, which only its preview names. */
const countedOnTheRow: Readonly<Record<string, readonly { readonly address: string; readonly role: string }[]>> = {
    [retryingSend.outgoingEmail]: [{ address: 'j.wrona@company.example', role: 'Cc' }],
    [refusedOutgoingSend.outgoingEmail]: [{ address: 'm.nowak@company.example', role: 'Cc' }],
};

/** What the Outbox route answers for the corpus mailbox, newest first. */
export const outboxPage = {
    sends: [waitingSend, sendingSend, retryingSend, unknownSend, refusedOutgoingSend],
    nextCursor: null,
};

/** What the Outbox route answers for a mailbox with nothing waiting. */
export const emptyOutboxPage = { sends: [], nextCursor: null };

/**
 * What one send's preview answers: the row it is listed as, with who it was from, what each recipient's server said,
 * the message itself as text, and what it carries.
 *
 * It is a function of the send the reader opened, because a preview is asked about one send and answers about that
 * one — as it stands now, which is whatever the consumer's own reading of the Outbox says about it.
 */
export function outgoingMailPreview(send: {
    readonly outgoingEmail: string;
    readonly account: string;
    readonly stage: string;
    readonly heldByAttempt: boolean;
    readonly recipients: readonly { readonly address: string; readonly role: string }[];
    readonly subject: string | null;
    readonly recordedAt: string;
    readonly dueAt: string | null;
    readonly dueTimeZone: string | null;
    readonly availableAt: string;
    readonly attemptCount: number;
    readonly lastFailureCode: number | null;
    readonly lastReplyCode: number | null;
    readonly failure: string | null;
}) {
    const refused = send.stage === 'Refused';

    return {
        outgoingEmail: send.outgoingEmail,
        account: send.account,
        stage: send.stage,
        heldByAttempt: send.heldByAttempt,
        inOutbox: true,
        dismissedAt: null,
        subject: send.subject,
        sender: { role: 'From', address: 'k.kowalska@nordwind.example', displayName: 'Karolina Kowalska' },
        recipients: [...send.recipients, ...(countedOnTheRow[send.outgoingEmail] ?? [])].map((recipient) => ({
            address: recipient.address,
            role: recipient.role,
            status: refused ? 'Refused' : 'Pending',
            lastReplyCode: refused ? 554 : null,
            answeredAt: refused ? send.recordedAt : null,
        })),
        recordedAt: send.recordedAt,
        dueAt: send.dueAt,
        dueTimeZone: send.dueTimeZone,
        availableAt: send.availableAt,
        attemptCount: send.attemptCount,
        lastFailureCode: send.lastFailureCode,
        lastReplyCode: send.lastReplyCode,
        failure: send.failure,
        body: {
            availability: 'Readable',
            plainText: {
                text: 'Hello,\n\nThe dates you suggested work for us.\n\nBest regards,\nKarolina',
                originalCharacterCount: 66,
                truncation: 'None',
            },
            document: null,
        },
        attachments: refused
            ? [{ fileName: 'Addendum-3-signed.pdf', mediaType: 'application/pdf', sizeOctets: 319_488 }]
            : [],
    };
}

/** What each of the three decisions about a send answers once the deployment took it. */
export const outboxDecisionTaken = { outgoingEmail: refusedOutgoingSend.outgoingEmail, outcome: 'Accepted' };
