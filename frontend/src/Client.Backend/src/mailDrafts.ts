// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord, parseEach } from './json';
import { parseMailDocument, type MailDocument } from './mailBody';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientRequest, type ClientResponse, type MailFathomTransport } from './transport';

// Writing mail, which is the one thing on this surface that reaches somebody else. A draft here is the draft in the
// user's own drafts folder rather than anything the client keeps: saving one writes a row, a stored message, and a
// copy on their mail server in the same act, so the words a screen holds until somebody asks to save them are the
// client's alone and everything past that is the deployment's.
//
// Two grants rather than one, because writing a message and sending it are different powers. Everything below except
// the send is reached under `mailfathom.mail.drafts.write`, whose effect stops at the user's own mailbox; the send is
// `mailfathom.mail.send`, and it is the act that cannot be taken back.
//
// **A refused send is a value here rather than a failure.** Screening, the recipient policy, and the spending ceilings
// each refuse a message the author already wrote, so no retry and no rewriting of the request would change the answer —
// which is precisely what `ClientFailureReason` cannot say. The refusal therefore travels as an outcome of the send,
// named by the code the deployment answered beside it, and nothing here reads the sentence: an operator-facing message
// is written for an operator, and a screen says what would change the outcome in its own words.

/** The route the drafts one user is writing are reached at, relative to the client prefix. */
export const mailDraftsRoute = '/drafts';

/** The route one draft is reached at. */
export function mailDraftRoute(draftId: string): string {
    return `${mailDraftsRoute}/${encodeURIComponent(draftId)}`;
}

/** The route one draft's staged files are added at. */
export function mailDraftAttachmentsRoute(draftId: string): string {
    return `${mailDraftRoute(draftId)}/attachments`;
}

/** The route one staged file is taken back off at. */
export function mailDraftAttachmentRoute(draftId: string, attachmentId: string): string {
    return `${mailDraftAttachmentsRoute(draftId)}/${encodeURIComponent(attachmentId)}`;
}

/** The route the message a draft holds is queued at. */
export function mailDraftSendRoute(draftId: string): string {
    return `${mailDraftRoute(draftId)}/send`;
}

/** Which answer a draft is, for one written against a message this deployment holds. */
export type MailDraftAnswer = 'senderOnly' | 'everyone' | 'forward';

/** The header an address is written in. */
export type MailRecipientRole = 'To' | 'Cc' | 'Bcc';

/** One person a draft is addressed to. */
export interface MailDraftRecipient {
    readonly role: MailRecipientRole;
    readonly address: string;
    readonly displayName: string | null;
}

/** One file staged against a draft, described and carrying none of what it holds. */
export interface MailStagedAttachment {
    readonly attachmentId: string;
    readonly fileName: string;
    readonly mediaType: string;
    readonly sizeOctets: number;
}

/**
 * What a draft still owes its copy in the user's drafts folder on the mail server, by the stage's own name.
 *
 * `Filed` is a copy in step with what MailFathom holds; every other stage is a filing or a replacement still owed, and
 * `Discarded` a draft given up.
 */
export type MailDraftServerCopy =
    'Composed' | 'AppendIssued' | 'Filed' | 'ReplacementAppendPending' | 'ReplacementRemovalPending' | 'Discarded';

/**
 * Why a draft's copy on the mail server stopped being one MailFathom may touch.
 *
 * Each means the same thing to the person writing it — another mail app may show something other than what MailFathom
 * holds — and which one it was is what an operator reads.
 */
export type MailDraftDivergence =
    'AppendOutcomeUnknown' | 'PlacementUnreported' | 'DestinationChanged' | 'FolderRecreated';

/** One draft as the deployment holds it, which is what a save answers with. */
export interface MailDraft {
    readonly draftId: string;
    readonly account: string;
    readonly subject: string;
    readonly recipients: readonly MailDraftRecipient[];
    readonly attachments: readonly MailStagedAttachment[];
    readonly revision: number;
    readonly sizeOctets: number;
    readonly serverCopy: MailDraftServerCopy;

    /** Why its copy on the mail server is out of step, or `null` while it is not. */
    readonly divergence: MailDraftDivergence | null;

    /** The code of the failure the last attempt on the mailbox ended in, or `null` while none has failed. */
    readonly lastFailureCode: number | null;

    /** Whether a send of it asks to be told when each recipient's server delivers it or cannot. */
    readonly requestsDeliveryNotification: boolean;

    /** Whether it asks each recipient's mail client for a read receipt. */
    readonly requestsReadReceipt: boolean;

    /** When it last changed, as the instant the deployment stated, which is what a list sorts and dates by. */
    readonly revisedAt: string;
}

/** One draft opened to go on editing: the record and the words its stored message carries. */
export interface MailDraftReading {
    readonly draft: MailDraft;
    readonly plainTextBody: string;

    /** The words as the closed tree a composer reads formatting back from, or `null` where the message yields none. */
    readonly document: MailDocument | null;
}

/** The drafts one person is writing in one account, newest edit first. */
export interface MailDraftList {
    readonly drafts: readonly MailDraft[];

    /** How many a reading answers with at most, so a screen can say when it is showing all of them. */
    readonly maximumCount: number;
}

/**
 * What an author has written, as a save states it.
 *
 * The two shapes are one type because a revision has to stay whichever shape the draft already is: an answer re-derives
 * its account, its subject, and its threading identifiers from the message it answers, so an edit that arrived as a
 * message of its own would quietly detach the reply from its conversation. Naming one half of either pair without the
 * other names nothing, which the deployment refuses rather than guesses at.
 */
export type MailDraftComposition = {
    readonly plainTextBody: string;

    /**
     * The HTML alternative the author wrote, or <code>null</code> where they wrote none.
     *
     * It is stated beside the plain text rather than instead of it: a message carrying only markup is one a reader
     * whose client shows the text part reads nothing of, which is why the deployment refuses a body with no plain
     * text at all.
     */
    readonly htmlBody: string | null;
    readonly to: readonly string[];
    readonly cc: readonly string[];
    readonly bcc: readonly string[];

    /**
     * Whether the message asks to be told when each recipient's server delivers it or cannot, and whether it asks each
     * recipient's mail client for a read receipt.
     *
     * Both are stated on every save, because a revision states the whole draft again: one that left either out would
     * switch it off rather than keep what the revision before it asked for.
     */
    readonly requestsDeliveryNotification: boolean;
    readonly requestsReadReceipt: boolean;
} & (
    | { readonly account: string; readonly subject: string; readonly answeredEmailId?: undefined }
    | { readonly answeredEmailId: string; readonly answers: MailDraftAnswer; readonly account?: undefined }
);

/**
 * Which rule of the deployment refused a send.
 *
 * Seven rather than one because a screen says something different about each, and each names what would change the
 * outcome: turning sending on for the account, addressing somebody the policy admits, waiting for the window a ceiling
 * is counted over, rewriting what a scanner found, and — for three of the seven — trying again. Those three are
 * `screeningUnavailable`, whose scanner may answer in a moment, and `notFullyScanned` and `attachmentNotRead`, each of
 * which a loaded host can produce with nothing wrong with the message: a read that ran out of time is one of the
 * outcomes behind both. So a retry is the first thing offered for either, ahead of shortening the message or sending
 * without the file, and a screen built from this comment offers one.
 */
export type MailSendRefusal =
    | 'sendingNotEnabled'
    | 'recipientRefused'
    | 'ceilingReached'
    | 'contentRefused'
    | 'notFullyScanned'
    | 'attachmentNotRead'
    | 'screeningUnavailable'
    | 'dueTimePassed'
    | 'refusedForAnotherReason';

// The codes the deployment answers a refused send with, beside the failure each of them is. They are matched rather
// than the sentence beside them: the sentence is written for an operator to read and is not a contract, and a client
// that parsed one would break on the day somebody improves the wording.
const refusalsByCode: Readonly<Record<number, MailSendRefusal>> = {
    53_006: 'recipientRefused',
    53_009: 'recipientRefused',
    56_003: 'sendingNotEnabled',
    57_002: 'ceilingReached',
    59_001: 'contentRefused',
    59_002: 'notFullyScanned',
    59_003: 'attachmentNotRead',
    81_001: 'screeningUnavailable',
};

// The one refusal a send answers with `400` rather than as a rule of the deployment: the time it was asked to leave at
// had already gone when the deployment read it. Nothing was queued, and the draft stands as it was.
const dueTimePassedCode = 51_015;

/**
 * What became of a draft write: the draft the deployment now holds, or a rule of this deployment refusing it.
 *
 * The same shape a send answers with, and for the same reason: the draft book is screened exactly as the outbox is, so
 * a save and a revision are refused by the same rules with the same codes. Folding that into a transport failure would
 * tell somebody whose attached document could not be read that the deployment did not answer, and offer them a retry
 * instead of the remedy.
 */
export type MailDraftWriteOutcome =
    | { readonly written: true; readonly draft: MailDraft }
    | { readonly written: false; readonly refusal: MailSendRefusal };

/**
 * When a message is to leave: the instant, and the IANA zone it was named in, which the Outbox says the time back in.
 */
export interface MailSendDueTime {
    readonly instant: string;
    readonly timeZone: string;
}

/** What became of a send: a message queued, or a rule of this deployment refusing the one the author wrote. */
export type MailSendOutcome =
    | { readonly queued: true; readonly outgoingEmailId: string }
    | { readonly queued: false; readonly refusal: MailSendRefusal };

/**
 * The most recipients and staged files a draft answer may carry before it is refused unread.
 *
 * Both are this parser's bound rather than the deployment's rule: how many addresses a message may carry and how many
 * files a draft may hold are the operator's configured numbers, which no answer states, so what these say is only that
 * an answer past them was never a draft. Applied during the walk rather than after it, because a walk that completed
 * has already paid for what it read.
 */
export const mostRecipientsInDraft = 256;

/**
 * The most files a draft or a send may carry, which is the highest number an operator may configure a message to attach:
 * a bound below it would refuse a send the service composed legitimately.
 *
 * @see mostRecipientsInDraft
 */
export const mostAttachmentsInDraft = 100;

// A draft record is a handful of short fields plus the two bounded collections above, and it carries no body at any
// size. Generous against that arithmetic and far below anything worth buffering.
const longestDraftAnswer = 262_144;

// An opened draft carries its words and their reduced document as well, so it is bounded as a message body is.
const longestDraftReadingAnswer = 8 * 1024 * 1024;

// A listing carries no body at any size. The deployment states how many it answers with at most, and this bounds that
// number before trusting it, so a listing is never walked past what this client would draw.
const mostDraftsListed = 500;
const longestDraftListAnswer = 4 * 1024 * 1024;

const serverCopies: readonly MailDraftServerCopy[] = [
    'Composed',
    'AppendIssued',
    'Filed',
    'ReplacementAppendPending',
    'ReplacementRemovalPending',
    'Discarded',
];

const divergences: readonly MailDraftDivergence[] = [
    'AppendOutcomeUnknown',
    'PlacementUnreported',
    'DestinationChanged',
    'FolderRecreated',
];

/**
 * Reads the drafts the signed-in person is writing in one account, without their words.
 *
 * @param account The account, as the deployment names it.
 */
export function readMailDrafts(
    session: ClientSession,
    transport: MailFathomTransport,
    account: string,
): Promise<ClientResult<MailDraftList>> {
    return spanned(`GET ${mailDraftsRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, mailDraftsRoute)}?account=${encodeURIComponent(account)}`,
            headers: headersFor(session),
            longestAnswer: longestDraftListAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const listed = parseList(bodyIn(response));

        return listed === null ? failed('unreadable', response.status) : read(listed);
    });
}

/**
 * Opens one draft with the words its stored message carries, which is what a composer goes on editing from.
 *
 * @returns The draft and its words, `missing` where this person holds no such draft, or another expected failure.
 */
export function readMailDraft(
    session: ClientSession,
    transport: MailFathomTransport,
    draftId: string,
): Promise<ClientResult<MailDraftReading>> {
    return spanned('GET /drafts/{draftId}', async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailDraftRoute(draftId)),
            headers: headersFor(session),
            longestAnswer: longestDraftReadingAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        // The route names one draft, so its absence is that draft's: one given up, sent, or never this person's.
        if (response.status === 404) {
            return failed('missing', 404);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const reading = parseReading(bodyIn(response));

        return reading === null ? failed('unreadable', response.status) : read(reading);
    });
}

/**
 * Writes one new draft, as a message of its own or as an answer to stored mail.
 *
 * @param session Who is asking and where.
 * @param transport How a request reaches the deployment.
 * @param composition What the author has written.
 * @returns The draft the deployment now holds, or an expected failure as a value.
 */
export function writeMailDraft(
    session: ClientSession,
    transport: MailFathomTransport,
    composition: MailDraftComposition,
): Promise<ClientResult<MailDraftWriteOutcome>> {
    return spanned(`POST ${mailDraftsRoute}`, async () =>
        draftIn(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, mailDraftsRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(stated(composition)),
                longestAnswer: longestDraftAnswer,
            }),
        ),
    );
}

/**
 * Replaces one draft with what the author has written since.
 *
 * @param draftId The draft a save already wrote down.
 * @returns The draft as it now stands, or an expected failure as a value.
 */
export function reviseMailDraft(
    session: ClientSession,
    transport: MailFathomTransport,
    draftId: string,
    composition: MailDraftComposition,
): Promise<ClientResult<MailDraftWriteOutcome>> {
    // A template rather than the composed route: an identifier in a span name is one name per draft.
    return spanned('PUT /drafts/{draftId}', async () =>
        draftIn(
            await send(transport, {
                method: 'PUT',
                path: routeFor(session, mailDraftRoute(draftId)),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify(stated(composition)),
                longestAnswer: longestDraftAnswer,
            }),
        ),
    );
}

/**
 * Gives one draft up, which takes its copies back out of the user's drafts folder.
 *
 * The draft is given up whatever the mailbox answered, so what comes back says what became of the copies rather than
 * whether the act worked — which is why this answers nothing rather than an outcome no screen acts on.
 */
export function discardMailDraft(
    session: ClientSession,
    transport: MailFathomTransport,
    draftId: string,
): Promise<ClientResult<void>> {
    return spanned('DELETE /drafts/{draftId}', async () =>
        acknowledged(
            await send(transport, {
                method: 'DELETE',
                path: routeFor(session, mailDraftRoute(draftId)),
                headers: headersFor(session),
                longestAnswer: longestDraftAnswer,
            }),
        ),
    );
}

/** Takes one staged file back off a draft. */
export function unstageMailDraftAttachment(
    session: ClientSession,
    transport: MailFathomTransport,
    draftId: string,
    attachmentId: string,
): Promise<ClientResult<void>> {
    return spanned('DELETE /drafts/{draftId}/attachments/{attachmentId}', async () =>
        acknowledged(
            await send(transport, {
                method: 'DELETE',
                path: routeFor(session, mailDraftAttachmentRoute(draftId, attachmentId)),
                headers: headersFor(session),
                longestAnswer: longestDraftAnswer,
            }),
        ),
    );
}

/**
 * Queues the message one draft holds, which is the one act here that reaches anybody else.
 *
 * Nothing has been transmitted when this answers: the message is queued, and cancelling it while it is still queued is
 * the closest thing to unsending that is honest. A message given a time to leave at is queued the same way and held
 * until then; a send at once states no body at all.
 *
 * @param dueAt When the message is to leave, or `null` for as soon as it can.
 * @returns The queued send, the rule that refused it, or an expected failure as a value — a refusal being an outcome
 * of the send rather than a failure of the request, for the reason {@link MailSendRefusal} gives.
 */
export function sendMailDraft(
    session: ClientSession,
    transport: MailFathomTransport,
    draftId: string,
    dueAt: MailSendDueTime | null = null,
): Promise<ClientResult<MailSendOutcome>> {
    return spanned('POST /drafts/{draftId}/send', async () =>
        outcomeOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, mailDraftSendRoute(draftId)),
                ...(dueAt === null
                    ? { headers: headersFor(session) }
                    : {
                          headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                          body: JSON.stringify({ dueAt: dueAt.instant, dueTimeZone: dueAt.timeZone }),
                      }),
                longestAnswer: longestDraftAnswer,
            }),
        ),
    );
}

/**
 * Composes the request that stages one file against a draft.
 *
 * The octets are the request body and nothing else, and a body is not something this package speaks: it composes the
 * route, the credential, and what the file declares itself to be, and the application puts the file on the wire — the
 * same division `mailAttachmentRequest` makes for a download, and for the same reason.
 *
 * @param fileName What the file is called, which is the author's own text and travels as a query value rather than as
 * anything a path is read from.
 * @param mediaType What the file declares itself to be, which is what the request states as its own content type.
 */
export function mailDraftAttachmentUploadRequest(
    session: ClientSession,
    draftId: string,
    fileName: string,
    mediaType: string,
): ClientRequest {
    const route = `${mailDraftAttachmentsRoute(draftId)}?fileName=${encodeURIComponent(fileName)}`;

    return {
        method: 'POST',
        path: routeFor(session, route),
        headers: { ...headersFor(session), 'Content-Type': mediaType },
        longestAnswer: longestDraftAnswer,
    };
}

/**
 * Stages one file against a draft, through an adapter, and reports the request the way every other one is.
 *
 * The adapter puts the octets on the wire and hands back what came off it, and nothing more: what an answer means is
 * this package's, which is why the parsing is here rather than in the module that owns the `File` and the signal.
 *
 * @param deliver Puts the composed request on the wire with the octets, answering `null` where nothing answered at
 * all — a connection refused, or an upload the author abandoned.
 */
export function stageMailDraftAttachment(
    session: ClientSession,
    draftId: string,
    fileName: string,
    mediaType: string,
    deliver: (request: ClientRequest) => Promise<ClientResponse | null>,
): Promise<ClientResult<MailStagedAttachment>> {
    return spanned('POST /drafts/{draftId}/attachments', async () =>
        stagedIn(await deliver(mailDraftAttachmentUploadRequest(session, draftId, fileName, mediaType))),
    );
}

// What a save states on the wire. The composition already carries whichever of the two shapes it is, so this only
// names the fields the surface reads — an absent half of either pair is absent rather than sent as nothing, which is
// the difference between a message of its own and an answer that lost its conversation.
function stated(composition: MailDraftComposition): Readonly<Record<string, unknown>> {
    const written = {
        plainTextBody: composition.plainTextBody,
        htmlBody: composition.htmlBody,
        to: composition.to,
        cc: composition.cc,
        bcc: composition.bcc,
        requestsDeliveryNotification: composition.requestsDeliveryNotification,
        requestsReadReceipt: composition.requestsReadReceipt,
    };

    return composition.answeredEmailId === undefined
        ? { ...written, account: composition.account, subject: composition.subject }
        : { ...written, answeredEmailId: composition.answeredEmailId, answers: composition.answers };
}

function draftIn(response: ClientResponse | null): ClientResult<MailDraftWriteOutcome> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 200) {
        const draft = parseDraft(bodyIn(response));

        return draft === null ? failed('unreadable', response.status) : read({ written: true, draft });
    }

    // The same two statuses a send reads as outcomes rather than as failures, for the same reason: the draft book is
    // screened by the same rules and answers the same codes, so a `409` here is a rule of this deployment about what
    // was written and a `503` is its one temporary refusal.
    if (response.status !== 409 && response.status !== 503) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    // An answer this client cannot read as a refusal is read as the failure its status names, which is what a `503`
    // carrying no body at all is: a proxy in front of the deployment rather than a screen that could not answer.
    const refused = refusalIn(response);

    return refused === null
        ? failed(failureReasonForStatus(response.status), response.status)
        : read({ written: false, refusal: refused });
}

function stagedIn(response: ClientResponse | null): ClientResult<MailStagedAttachment> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const staged = parseAttachment(bodyIn(response));

    return staged === null ? failed('unreadable', response.status) : read(staged);
}

// A give-up and an unstaging each answer with something the client does not act on — what became of the copies in the
// folder, or nothing at all — so what is read off them is that the deployment did it.
function acknowledged(response: ClientResponse | null): ClientResult<void> {
    if (response === null) {
        return failed('unavailable', null);
    }

    return response.status === 200 || response.status === 204
        ? read(undefined)
        : failed(failureReasonForStatus(response.status), response.status);
}

function outcomeOf(response: ClientResponse | null): ClientResult<MailSendOutcome> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 200) {
        const queued = bodyIn(response)?.['outgoingEmail'];

        return typeof queued === 'string'
            ? read({ queued: true, outgoingEmailId: queued })
            : failed('unreadable', response.status);
    }

    // A `409` is a rule of this deployment about the message that was written and a `503` is the one temporary refusal
    // here, so both are outcomes of the send. Every other status is a failure of the request, which the four reasons
    // already say everything about.
    if (response.status === 400 && bodyIn(response)?.['errorCode'] === dueTimePassedCode) {
        return read({ queued: false, refusal: 'dueTimePassed' });
    }

    if (response.status !== 409 && response.status !== 503) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const refused = refusalIn(response);

    return refused === null ? failed('unreadable', response.status) : read({ queued: false, refusal: refused });
}

// Which rule refused it, or null for an answer this client could not read as one. A refusal whose body is not a record
// is a body this client could not read, exactly as at `200`: reading it as a rule of the deployment would put a
// sentence about the message on the screen for what is a page from something in the way.
function refusalIn(response: ClientResponse): MailSendRefusal | null {
    const body = bodyIn(response);

    if (body === null) {
        return null;
    }

    const code = body['errorCode'];
    const refusal = typeof code === 'number' ? refusalsByCode[code] : undefined;

    return refusal ?? 'refusedForAnotherReason';
}

function bodyIn(response: ClientResponse): Readonly<Record<string, unknown>> | null {
    try {
        return asRecord(JSON.parse(response.body));
    } catch {
        return null;
    }
}

function parseDraft(body: Readonly<Record<string, unknown>> | null): MailDraft | null {
    // A save answers with the record itself, and the surface is documented as free to wrap it under `draft`, so the
    // record is taken from wherever this answer put it rather than from two parsers that would disagree about one
    // shape.
    const record = body === null ? null : (asRecord(body['draft']) ?? body);

    if (record === null) {
        return null;
    }

    const draftId = record['draftId'];
    const account = record['account'];
    const subject = record['subject'];
    const revision = record['revision'];
    const sizeOctets = record['sizeOctets'];
    const serverCopy = record['serverCopy'];
    const divergence = record['divergence'] ?? null;
    const lastFailureCode = record['lastFailureCode'] ?? null;
    const requestsDeliveryNotification = record['requestsDeliveryNotification'];
    const requestsReadReceipt = record['requestsReadReceipt'];
    const revisedAt = record['revisedAt'];

    if (
        typeof draftId !== 'string' ||
        typeof account !== 'string' ||
        typeof subject !== 'string' ||
        typeof revision !== 'number' ||
        typeof sizeOctets !== 'number' ||
        typeof revisedAt !== 'string' ||
        typeof requestsDeliveryNotification !== 'boolean' ||
        typeof requestsReadReceipt !== 'boolean' ||
        !isOneOf(serverCopy, serverCopies) ||
        (divergence !== null && !isOneOf(divergence, divergences)) ||
        (lastFailureCode !== null && !isWholeNumber(lastFailureCode))
    ) {
        return null;
    }

    const recipients = parseEach(record['recipients'], mostRecipientsInDraft, parseRecipient);
    const attachments = parseEach(record['attachments'], mostAttachmentsInDraft, parseAttachment);

    return recipients === null || attachments === null
        ? null
        : {
              draftId,
              account,
              subject,
              recipients,
              attachments,
              revision,
              sizeOctets,
              serverCopy,
              divergence,
              lastFailureCode,
              requestsDeliveryNotification,
              requestsReadReceipt,
              revisedAt,
          };
}

function parseList(body: Readonly<Record<string, unknown>> | null): MailDraftList | null {
    const maximumCount = body?.['maximumCount'];

    if (body === null || !isWholeNumber(maximumCount) || maximumCount < 0) {
        return null;
    }

    const drafts = parseEach(body['drafts'], Math.min(maximumCount, mostDraftsListed), (entry) =>
        parseDraft(asRecord(entry)),
    );

    return drafts === null ? null : { drafts, maximumCount };
}

function parseReading(body: Readonly<Record<string, unknown>> | null): MailDraftReading | null {
    const draft = body === null ? null : parseDraft(asRecord(body['draft']));
    const plainTextBody = body?.['plainTextBody'];
    const carried = body?.['document'] ?? null;
    const document = carried === null ? null : parseMailDocument(carried);

    // A document that arrived and could not be read is refused rather than dropped: the plain text would still open,
    // and the author would then save over their own formatting without knowing any was there.
    if (draft === null || typeof plainTextBody !== 'string' || (carried !== null && document === null)) {
        return null;
    }

    return { draft, plainTextBody, document };
}

function parseRecipient(entry: unknown): MailDraftRecipient | null {
    const record = asRecord(entry);
    const role = record?.['role'];
    const address = record?.['address'];
    const displayName = record?.['displayName'];

    if (!isRecipientRole(role) || typeof address !== 'string') {
        return null;
    }

    // Where the address came from is on the record as well, and is deliberately not read: it is what a send's own
    // governance asks about, and a field parsed for nobody is a field to keep true.
    return { role, address, displayName: typeof displayName === 'string' ? displayName : null };
}

function parseAttachment(entry: unknown): MailStagedAttachment | null {
    const record = asRecord(entry);
    const attachmentId = record?.['attachmentId'];
    const fileName = record?.['fileName'];
    const mediaType = record?.['mediaType'];
    const sizeOctets = record?.['sizeOctets'];

    return typeof attachmentId === 'string' &&
        typeof fileName === 'string' &&
        typeof mediaType === 'string' &&
        typeof sizeOctets === 'number'
        ? { attachmentId, fileName, mediaType, sizeOctets }
        : null;
}

function isOneOf<TValue extends string>(value: unknown, values: readonly TValue[]): value is TValue {
    return typeof value === 'string' && values.includes(value as TValue);
}

function isWholeNumber(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value);
}

function isRecipientRole(value: unknown): value is MailRecipientRole {
    return value === 'To' || value === 'Cc' || value === 'Bcc';
}
