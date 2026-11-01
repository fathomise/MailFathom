// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What the user is writing, and what becomes of one of them when it is sent.
//
// A draft here is the one in the user's own drafts folder, so there is nothing held only in the client for a fixture
// to stand in for: what a save answers with is the record itself, and that is what these values are.

import { workAccount } from './deployment';
import { refusedOutgoingSend } from './outbox';

/** The identity the draft below is addressed by, everywhere the corpus names one draft. */
export const draftId = '00000000-0000-4000-8000-0000000000d0';

/** A message of the author's own, addressed to two people and carrying one staged file. */
export const draft = {
    draftId,
    account: workAccount.id,
    subject: 'The yard on the ninth',
    recipients: [
        { role: 'To', address: 'sales@nordwind.example', displayName: 'Nordwind' },
        { role: 'Cc', address: 'yard@example.invalid', displayName: null },
    ],
    attachments: [
        {
            attachmentId: '00000000-0000-4000-8000-0000000000d1',
            fileName: 'yard.pdf',
            mediaType: 'application/pdf',
            sizeOctets: 18_432,
            stagedAt: '2026-09-02T14:05:00+00:00',
        },
    ],
    serverCopy: 'Filed',
    revision: 3,
    sizeOctets: 21_504,
    composedAt: '2026-09-02T14:00:00+00:00',
    revisedAt: '2026-09-02T14:20:00+00:00',
    sentAs: null,
    divergence: null,
    lastFailureCode: null,
    requestsDeliveryNotification: false,
    requestsReadReceipt: false,
};

/**
 * A second draft, written as an answer to stored mail rather than as a message of its own.
 *
 * It is worth stating because the two are one type with two halves, and a screen listing them draws the same row for
 * both: what separates them is what the deployment derived — the subject and the threading — rather than anything the
 * author typed twice.
 */
export const answeringDraft = {
    draftId: '00000000-0000-4000-8000-0000000000d2',
    account: workAccount.id,
    subject: 'Re: The racking quote',
    recipients: [{ role: 'To', address: 'sales@nordwind.example', displayName: 'Nordwind' }],
    attachments: [],
    serverCopy: 'Filed',
    revision: 1,
    sizeOctets: 1_120,
    composedAt: '2026-09-03T09:10:00+00:00',
    revisedAt: '2026-09-03T09:10:00+00:00',
    sentAs: null,
    divergence: null,
    lastFailureCode: null,
    requestsDeliveryNotification: false,
    requestsReadReceipt: false,
};

/**
 * A draft MailFathom keeps whose copy in the drafts folder on the mail server may be missing: its filing was never
 * answered. It is what the Drafts list draws under *Out of step with your server* and what the draft notification
 * opens, and it asks for a read receipt, so opening it reaches the composer with a request already on.
 */
export const outOfStepDraft = {
    draftId: '00000000-0000-4000-8000-0000000000d3',
    account: workAccount.id,
    subject: 'Q4 supplier review - agenda',
    recipients: [
        { role: 'To', address: 'marta@nordwind.example', displayName: 'Marta' },
        { role: 'To', address: 'olek@nordwind.example', displayName: null },
    ],
    attachments: [],
    serverCopy: 'AppendIssued',
    revision: 2,
    sizeOctets: 2_048,
    composedAt: '2026-09-03T07:40:00+00:00',
    revisedAt: '2026-09-03T07:52:00+00:00',
    sentAs: null,
    divergence: 'AppendOutcomeUnknown',
    lastFailureCode: null,
    requestsDeliveryNotification: false,
    requestsReadReceipt: true,
};

/** What listing the work account's drafts answers with: every draft, newest edit first, and the most it would list. */
export const draftList = { drafts: [answeringDraft, outOfStepDraft, draft], maximumCount: 200 };

/**
 * The draft a refused send is taken back into, which is the message it was sent as: the same subject and the same
 * people, a draft again and filed as one.
 */
export const reopenedDraft = {
    ...draft,
    draftId: '00000000-0000-4000-8000-0000000000d4',
    subject: refusedOutgoingSend.subject,
    recipients: refusedOutgoingSend.recipients.map(({ address, role }) => ({ role, address, displayName: null })),
    attachments: [],
    revision: 1,
};

/** What taking the refused send back to edit answers with: the send that left the Outbox and the draft to open. */
export const reopenedSend = {
    outgoingEmail: refusedOutgoingSend.outgoingEmail,
    outcome: 'Accepted',
    draftId: reopenedDraft.draftId,
};

/** What opening the reopened draft answers with. */
export const reopenedDraftReading = {
    draft: reopenedDraft,
    plainTextBody: 'Signed copies attached, as agreed on the call.',
    htmlBody: null,
    document: null,
};

/** What opening the out-of-step draft answers with: the record and the words its stored message carries. */
export const outOfStepDraftReading = {
    draft: outOfStepDraft,
    plainTextBody: 'Agenda for Thursday: volumes, the racking quote, and the yard on the ninth.',
    htmlBody: null,
    document: null,
};

/** What a save answers with, which is the record wrapped the way the surface is documented as free to wrap it. */
export const savedDraft = { draft };

/** What a send answers with once the message is queued: an identity to watch in the outbox and nothing else. */
export const queuedSend = { outgoingEmail: '00000000-0000-4000-8000-0000000000e0' };

/** What the outbox answers a queued send taken back before it began transmitting with. */
export const withdrawnSend = { outgoingEmail: queuedSend.outgoingEmail, outcome: 'Accepted' };

/**
 * What a send answers with when a rule of the deployment refused the message that was written.
 *
 * The code is what a client matches rather than the sentence beside it: the sentence is written for an operator and is
 * not a contract, so a fixture whose refusal was recognised by its wording would prove a client nobody wrote.
 */
export const refusedSend = {
    errorCode: 59_001,
    detail: 'The content scanner refused this message.',
};

/** That this deployment writes a draft for somebody, which is what decides whether the composer offers the block. */
export const draftsReplies = { draftsReplies: true };

/**
 * One drafted reply, as the composer puts it on the screen.
 *
 * It answers the racking quote the mail corpus states, so the claims below cite a message that exists there rather
 * than an identity nothing else names — a citation nobody can follow is the shape this directory exists to end.
 */
export const draftedReply = {
    drafted: true,
    body: 'Yes — 4 200 net still stands, and Tuesday at ten works for us.\n\nBest regards',
    claims: [
        {
            text: 'The agreed price is 4 200 net.',
            supported: true,
            sources: [{ kind: 'email', email: '00000000-0000-4000-8000-000000000001' }],
        },
        { text: 'Tuesday at ten is free.', supported: false, sources: [] },
    ],
    proposedRecipients: [{ address: 'sales@nordwind.example', displayName: 'Nordwind' }],
};
