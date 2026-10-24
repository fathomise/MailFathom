// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord, parseEach } from './json';
import { mostAttachmentsInDraft, mostRecipientsInDraft } from './mailDrafts';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// Each account's outbox: the sends written down for it that have not left yet, and the ones a server refused that
// nobody dismissed. It is read-only as a mailbox — it is no folder, so nothing here moves, flags, or files anything —
// and a send in it is named by the identifier its send answered with rather than as a stored email. What a person
// decides about one send is the three decisions below, each of which reports what became of the send rather than
// refusing, because a person acting on a screen a moment old is exactly who asks.
//
// Every route here is `mailfathom.mail.send`, and reading a page or a preview takes `mailfathom.mail.read` beside it,
// because who a send is addressed to and what it says are mail.

/** The route one page of an account's outbox is read from, relative to the client prefix. */
export const mailOutboxRoute = '/outbox';

/** The route one queued send is withdrawn at, relative to the client prefix. */
export const mailOutboxCancellationRoute = '/outbox/cancellation';

/** The route one send is offered again at, relative to the client prefix. */
export const mailOutboxRequeueRoute = '/outbox/requeue';

/** The route one send is taken out of its account's outbox at, relative to the client prefix. */
export const mailOutboxDismissalRoute = '/outbox/dismissal';

/** The route one send is read from, relative to the client prefix. */
export function outgoingMailRoute(outgoingEmailId: string): string {
    return `${mailOutboxRoute}/${encodeURIComponent(outgoingEmailId)}`;
}

/**
 * How many sends one reading of an outbox asks for, which is the most the service serves in a page.
 *
 * One page and no continuation: an outbox holds what has not left yet, which is a handful, and a list at this length is
 * still one a screen draws whole. An outbox holding more than this shows its newest sends, and reading further is a
 * cursor the page already carries for when that stops being true.
 */
export const mailOutboxPageSize = 200;

/**
 * Where a send stands.
 *
 * A page holds the first three, which are the outbox; a send read by its identifier may stand at the last two as well,
 * once it has left — delivered, or withdrawn — which is how a screen holding it learns it has gone.
 */
export type OutgoingMailStage = 'Recorded' | 'TransmissionBegun' | 'Refused' | 'Sent' | 'Cancelled';

/** The header a recipient is named in. */
export type OutgoingMailRecipientRole = 'To' | 'Cc' | 'Bcc';

/** What a server last settled one recipient at: offered next, reached, or permanently refused. */
export type OutgoingMailRecipientStatus = 'Pending' | 'Accepted' | 'Refused';

/** One recipient as a row of the outbox names them. */
export interface OutgoingMailRecipient {
    readonly address: string;
    readonly role: OutgoingMailRecipientRole;
}

/** One recipient of a send read whole, with what the server said about them. */
export interface OutgoingMailRecipientOutcome extends OutgoingMailRecipient {
    readonly status: OutgoingMailRecipientStatus;
}

/** What a send and the row naming it carry alike. */
interface OutgoingMailStanding {
    /** The identifier the send answered with, which every decision about it names. */
    readonly outgoingEmailId: string;

    readonly account: string;
    readonly stage: OutgoingMailStage;

    /** Whether an attempt holds it right now, which at `TransmissionBegun` tells one being sent from one nobody knows the fate of. */
    readonly heldByAttempt: boolean;

    /** The subject, or `null` where it could not be read. */
    readonly subject: string | null;

    /** When it was handed over. */
    readonly recordedAt: string;

    /** When it was written to leave at, or `null` where it was written to leave at once. */
    readonly dueAt: string | null;

    /** The earliest a delivery pass may take it, which for a send left to try again is when it is tried. */
    readonly availableAt: string;

    readonly attemptCount: number;

    /** The failure it stopped on, in words in the reader's language, or `null` while none is recorded. */
    readonly failure: string | null;
}

/** One row of an account's outbox. */
export interface OutgoingMail extends OutgoingMailStanding {
    /** Up to three recipients from `To`, or from `Cc` and then `Bcc` where `To` is empty. */
    readonly recipients: readonly OutgoingMailRecipient[];

    /** How many more people it is addressed to. */
    readonly otherRecipientCount: number;
}

/** One page of an account's outbox, newest first. */
export interface MailOutboxPage {
    readonly sends: readonly OutgoingMail[];

    /** Whether the outbox holds more than this page. */
    readonly continues: boolean;
}

/** One file a send carries, described and never served. */
export interface OutgoingMailAttachment {
    readonly fileName: string | null;
    readonly mediaType: string;
    readonly sizeOctets: number;
}

/** One send read whole: everything its row says, every recipient, who it is from, and the message itself. */
export interface OutgoingMailPreview extends OutgoingMailStanding {
    /** Whether it is still in its account's outbox: not left yet, or refused and not dismissed. */
    readonly inOutbox: boolean;

    /** The address it is sent as, or `null` where the message could not be read. */
    readonly sender: string | null;

    readonly recipients: readonly OutgoingMailRecipientOutcome[];

    /** The message as words, or `null` where it could not be read. */
    readonly text: string | null;

    readonly attachments: readonly OutgoingMailAttachment[];
}

/**
 * What became of the send a withdrawal named.
 *
 * Four outcomes rather than a refusal, because a person acting on a screen a moment old is exactly who asks: a message
 * whose transmission has begun cannot be taken back, and saying so is the answer rather than an error.
 */
export type MailSendWithdrawal = 'withdrawn' | 'alreadyBeingSent' | 'pastRecall' | 'noSuchSend';

/** What became of the send a decision named: taken, raced by an attempt, past the point it applied, unknown, or a refusal not restated. */
export type MailOutboxDecision = 'accepted' | 'alreadyBeingSent' | 'pastRecall' | 'noSuchSend' | 'refusalNotRestated';

const decisions: Readonly<Record<string, MailOutboxDecision | undefined>> = {
    Accepted: 'accepted',
    AttemptUnderWay: 'alreadyBeingSent',
    StageDoesNotAllowIt: 'pastRecall',
    RecordUnknown: 'noSuchSend',
    RefusalNotRestated: 'refusalNotRestated',
};

// A withdrawal never answers a refusal restated, which belongs to offering a send again; one that did is an answer this
// client does not act on.
const withdrawals: Readonly<Record<MailOutboxDecision, MailSendWithdrawal | null>> = {
    accepted: 'withdrawn',
    alreadyBeingSent: 'alreadyBeingSent',
    pastRecall: 'pastRecall',
    noSuchSend: 'noSuchSend',
    refusalNotRestated: null,
};

const pageStages: readonly OutgoingMailStage[] = ['Recorded', 'TransmissionBegun', 'Refused'];
const stages: readonly OutgoingMailStage[] = [...pageStages, 'Sent', 'Cancelled'];
const roles: readonly OutgoingMailRecipientRole[] = ['To', 'Cc', 'Bcc'];
const statuses: readonly OutgoingMailRecipientStatus[] = ['Pending', 'Accepted', 'Refused'];

// The most recipients one row names, which is the service's own.
const mostRecipientsInRow = 3;

// Two short fields. Generous against that and far below anything worth buffering.
const longestDecisionAnswer = 16_384;

// A preview carries the message itself, which is a body bounded the way a stored message's is, beside a reduced
// document this client does not draw and is bounded together with it.
const longestPreviewAnswer = 8 * 1024 * 1024;

/**
 * Reads the newest page of one account's outbox.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param account The account whose outbox to read, which is required: no reading spans every account.
 * @returns The page, or an expected failure as a value.
 */
export function readMailOutbox(
    session: ClientSession,
    transport: MailFathomTransport,
    account: string,
): Promise<ClientResult<MailOutboxPage>> {
    return spanned(`GET ${mailOutboxRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, mailOutboxRoute)}?account=${encodeURIComponent(account)}&pageSize=${String(mailOutboxPageSize)}`,
            headers: headersFor(session),
        });

        return answered(response, parsePage);
    });
}

/**
 * Reads one send whole.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param outgoingEmailId The send, as its send answered.
 * @returns The send, `missing` where this user holds no send under that identifier, or another expected failure.
 */
export function readOutgoingMail(
    session: ClientSession,
    transport: MailFathomTransport,
    outgoingEmailId: string,
): Promise<ClientResult<OutgoingMailPreview>> {
    return spanned(`GET ${mailOutboxRoute}/{outgoingEmailId}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, outgoingMailRoute(outgoingEmailId)),
            headers: headersFor(session),
            longestAnswer: longestPreviewAnswer,
        });

        // The one route here that names a single thing, so its absence is the absence of that thing: a send another
        // user made answers exactly as one nobody made.
        return response?.status === 404 ? failed('missing', 404) : answered(response, parsePreview);
    });
}

/**
 * Withdraws one send that has not begun transmitting.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param outgoingEmailId The send, as the queueing answered it.
 * @returns What became of it, or an expected failure as a value.
 */
export async function withdrawOutgoingMail(
    session: ClientSession,
    transport: MailFathomTransport,
    outgoingEmailId: string,
): Promise<ClientResult<MailSendWithdrawal>> {
    const decided = await decide(session, transport, mailOutboxCancellationRoute, { outgoingEmail: outgoingEmailId });

    if (decided.outcome === 'failed') {
        return failed(decided.failure.reason, decided.failure.status);
    }

    const withdrawal = withdrawals[decided.value];

    return withdrawal === null ? failed('unreadable', 200) : read(withdrawal);
}

/**
 * Offers one send again, which is the decision this system will not take on its own.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param outgoingEmailId The send.
 * @param refusalRestated Whether the person saw the server's permanent refusal and asked anyway, which a refused send needs.
 * @returns What became of it, or an expected failure as a value.
 */
export function offerOutgoingMailAgain(
    session: ClientSession,
    transport: MailFathomTransport,
    outgoingEmailId: string,
    refusalRestated: boolean,
): Promise<ClientResult<MailOutboxDecision>> {
    return decide(session, transport, mailOutboxRequeueRoute, { outgoingEmail: outgoingEmailId, refusalRestated });
}

/**
 * Takes one refused send, or one whose outcome nobody will learn, out of its account's outbox.
 *
 * It is the person accepting the failure and changes nothing else about the send: a refusal may follow a partial
 * acceptance, so nothing here says the message was not sent.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param outgoingEmailId The send.
 * @returns What became of it, or an expected failure as a value.
 */
export function dismissOutgoingMail(
    session: ClientSession,
    transport: MailFathomTransport,
    outgoingEmailId: string,
): Promise<ClientResult<MailOutboxDecision>> {
    return decide(session, transport, mailOutboxDismissalRoute, { outgoingEmail: outgoingEmailId });
}

function decide(
    session: ClientSession,
    transport: MailFathomTransport,
    route: string,
    decision: Readonly<Record<string, unknown>>,
): Promise<ClientResult<MailOutboxDecision>> {
    return spanned(`POST ${route}`, async () =>
        answered(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, route),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(decision),
                longestAnswer: longestDecisionAnswer,
            }),
            parseDecision,
        ),
    );
}

function answered<TValue>(
    response: ClientResponse | null,
    parse: (value: unknown) => TValue | null,
): ClientResult<TValue> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    let body: unknown;

    try {
        body = JSON.parse(response.body);
    } catch {
        return failed('unreadable', response.status);
    }

    const parsed = parse(body);

    return parsed === null ? failed('unreadable', response.status) : read(parsed);
}

function parseDecision(value: unknown): MailOutboxDecision | null {
    const outcome = asRecord(value)?.['outcome'];

    return (typeof outcome === 'string' ? decisions[outcome] : undefined) ?? null;
}

function parsePage(value: unknown): MailOutboxPage | null {
    const record = asRecord(value);
    const nextCursor = record?.['nextCursor'] ?? null;

    if (record === null || (nextCursor !== null && typeof nextCursor !== 'string')) {
        return null;
    }

    const sends = parseEach(record['sends'], mailOutboxPageSize, parseRow);

    return sends === null ? null : { sends, continues: nextCursor !== null };
}

function parseRow(value: unknown): OutgoingMail | null {
    const record = asRecord(value);
    const standing = record === null ? null : parseStanding(record);

    if (record === null || standing === null || !isOneOf(standing.stage, pageStages)) {
        return null;
    }

    const recipients = parseEach(record['recipients'], mostRecipientsInRow, parseRecipient);
    const otherRecipientCount = record['otherRecipientCount'];

    return recipients === null || !isCount(otherRecipientCount)
        ? null
        : { ...standing, recipients, otherRecipientCount };
}

function parsePreview(value: unknown): OutgoingMailPreview | null {
    const record = asRecord(value);
    const standing = record === null ? null : parseStanding(record);

    if (record === null || standing === null) {
        return null;
    }

    const inOutbox = record['inOutbox'];
    const sender = parseSender(record['sender'] ?? null);
    const recipients = parseEach(record['recipients'], mostRecipientsInDraft, parseRecipientOutcome);
    const attachments = parseEach(record['attachments'], mostAttachmentsInDraft, parseAttachment);
    const text = parseText(record['body'] ?? null);

    if (typeof inOutbox !== 'boolean' || sender === undefined || recipients === null || attachments === null) {
        return null;
    }

    return text === undefined ? null : { ...standing, inOutbox, sender, recipients, text, attachments };
}

function parseStanding(record: Readonly<Record<string, unknown>>): OutgoingMailStanding | null {
    const outgoingEmailId = record['outgoingEmail'];
    const account = record['account'];
    const stage = record['stage'];
    const heldByAttempt = record['heldByAttempt'];
    const subject = record['subject'] ?? null;
    const recordedAt = record['recordedAt'];
    const dueAt = record['dueAt'] ?? null;
    const availableAt = record['availableAt'];
    const attemptCount = record['attemptCount'];
    const failure = record['failure'] ?? null;

    if (
        typeof outgoingEmailId !== 'string' ||
        outgoingEmailId === '' ||
        typeof account !== 'string' ||
        !isOneOf(stage, stages) ||
        typeof heldByAttempt !== 'boolean' ||
        !isTextOrNull(subject) ||
        typeof recordedAt !== 'string' ||
        !isTextOrNull(dueAt) ||
        typeof availableAt !== 'string' ||
        !isCount(attemptCount) ||
        !isTextOrNull(failure)
    ) {
        return null;
    }

    return {
        outgoingEmailId,
        account,
        stage,
        heldByAttempt,
        subject,
        recordedAt,
        dueAt,
        availableAt,
        attemptCount,
        failure,
    };
}

function parseRecipient(value: unknown): OutgoingMailRecipient | null {
    const record = asRecord(value);
    const address = record?.['address'];
    const role = record?.['role'];

    return typeof address === 'string' && isOneOf(role, roles) ? { address, role } : null;
}

function parseRecipientOutcome(value: unknown): OutgoingMailRecipientOutcome | null {
    const recipient = parseRecipient(value);
    const status = asRecord(value)?.['status'];

    return recipient !== null && isOneOf(status, statuses) ? { ...recipient, status } : null;
}

// `undefined` is a sender that arrived in a shape no sender has, which refuses the preview; `null` is none at all.
function parseSender(value: unknown): string | null | undefined {
    if (value === null) {
        return null;
    }

    const address = asRecord(value)?.['address'];

    return typeof address === 'string' ? address : undefined;
}

// The message as the words the service derived from it, where it could be read; the reduced document beside them is
// not what the design draws here and is left unread. `undefined` refuses the preview, `null` is a message that could
// not be read.
function parseText(value: unknown): string | null | undefined {
    if (value === null) {
        return null;
    }

    const record = asRecord(value);
    const availability = record?.['availability'];
    const text = asRecord(record?.['plainText'])?.['text'];

    if (typeof availability !== 'string' || typeof text !== 'string') {
        return undefined;
    }

    return availability === 'Readable' ? text : null;
}

function parseAttachment(value: unknown): OutgoingMailAttachment | null {
    const record = asRecord(value);
    const fileName = record?.['fileName'] ?? null;
    const mediaType = record?.['mediaType'];
    const sizeOctets = record?.['sizeOctets'];

    return isTextOrNull(fileName) && typeof mediaType === 'string' && isCount(sizeOctets)
        ? { fileName, mediaType, sizeOctets }
        : null;
}

function isOneOf<TValue extends string>(value: unknown, members: readonly TValue[]): value is TValue {
    return typeof value === 'string' && (members as readonly string[]).includes(value);
}

function isTextOrNull(value: unknown): value is string | null {
    return value === null || typeof value === 'string';
}

function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
