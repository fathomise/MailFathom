// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { parseSpamVerdict, type MailSpamVerdict } from './mailSpam';
import {
    authorAuthentications,
    deploymentTrusts,
    isOneOf,
    signatureOutcomes,
    type MailAuthorAuthentication,
    type MailDeploymentTrust,
    type MailSignatureOutcome,
} from './senderFacts';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// Everything a reading pane draws around a message, which is the other half of the body route. What the service serves
// here is a description rather than any of the message's content beyond its headers: no octet of a file, and no picture
// the body embeds. This is the client's trust boundary for that description, so every field is checked before it becomes
// a value a screen may draw, and every collection carries a bound checked during the walk rather than after it.

/** The route one message is served at, relative to the client prefix. */
export function mailMessageRoute(storedEmailId: string): string {
    return `/messages/${encodeURIComponent(storedEmailId)}`;
}

/** The header one address appeared in, which is what decides where a screen draws it. */
export type MailParticipantRole = 'Sender' | 'From' | 'ReplyTo' | 'To' | 'Cc' | 'Bcc';

/**
 * Who reached the authentication verdict: the account's trusted receiving mail server, read back out of the header it
 * wrote, or MailFathom verifying the message's own DKIM signatures where no such statement was available.
 *
 * The first is also what a message carries when nothing was read at all, because the value names which reading
 * produced the verdict rather than asserting that a server established something.
 */
export type MailAuthenticationSource = 'ReceivingServer' | 'LocalVerification';

/** The DMARC result the verdict was reached with. */
export type MailDmarcOutcome =
    'NotReported' | 'Pass' | 'Fail' | 'NoPolicyPublished' | 'TemporaryError' | 'PermanentError';

/** What recognized a trusted author: a domain of the owner's own accounts, or the trusted senders. */
export type MailTrustSource = 'OwnAccountDomain' | 'ConfiguredTrustedSender' | 'StoredTrustedSender';

/** How a signed message arrived. */
export type MailSignatureFormat = 'SecureMimeDetached' | 'SecureMimeOpaque' | 'OpenPgpMime';

/** The `smime-type` an opaque S/MIME body declared, as declared. */
export type MailSecureMimeType =
    'SignedData' | 'EnvelopedData' | 'AuthEnvelopedData' | 'CompressedData' | 'CertsOnly' | 'NotDeclared';

/** Why a signature is not verifiable here, and which validity rule a signer broke. */
export type MailSignatureUnverifiableReason =
    | 'NoCertificate'
    | 'UntrustedRoot'
    | 'OutsideValidityPeriod'
    | 'WrongKeyUsage'
    | 'Revoked'
    | 'RevocationNotCheckable'
    | 'UnknownKey'
    | 'ExpiredKey'
    | 'RevokedKey'
    | 'UnsupportedAlgorithm'
    | 'MalformedSignature'
    | 'ContentNotOpened';

/** What one step of a signature verification found, or that it was not reached. */
export type MailSignatureStepResult = 'Passed' | 'Failed' | 'NotChecked' | 'Skipped';

/** What the signer is trusted through. */
export type MailSignatureTrustAnchor = 'SystemTrustStore' | 'ConfiguredAuthority' | 'ConfiguredKey';

/** How a signer's revocation status was established. */
export type MailRevocationMethod = 'Ocsp' | 'Crl' | 'KeyRevocationSignature';

/** One address a message wrote, and the header it wrote it in. */
export interface MailParticipant {
    readonly role: MailParticipantRole;
    readonly address: string;
    readonly displayName: string | null;
}

/** What a message displays above its body. */
export interface MailMessageHeaders {
    readonly subject: string | null;
    readonly sentAt: string | null;
    readonly receivedAt: string | null;
    readonly participants: readonly MailParticipant[];
    readonly messageId: string | null;
    readonly inReplyTo: string | null;
    readonly references: readonly string[];
}

/** Whether a message has a body to draw, and which forms of it the sender wrote. */
export interface MailMessageBodyForms {
    readonly availability: string;
    readonly plainText: boolean;
    readonly html: boolean;
}

/**
 * What this deployment established about the author a message displays.
 *
 * The two outcomes are held side by side and never collapsed into one value, for the reason the service publishes them
 * that way: one is a fact a receiving server established about the message, the other is this deployment's own
 * classification of the author it established, and a client that combined them would be inventing the rule that does so.
 *
 * `authenticatedDomain` is who actually sent the message, which is what a screen names instead of the `From` value the
 * message displays. It is stated and never judged: comparing it against the displayed domain would be evaluating a
 * policy the service deliberately does not, so what a reader acts on stays `authorAuthentication`.
 *
 * The evidence after it is what a details surface lists step by step — who checked, the DKIM and SPF domains that
 * passed, the DMARC result, and what recognized a trusted author — and none of it changes what a reader acts on.
 */
export interface MailSenderVerdict {
    readonly authorAuthentication: MailAuthorAuthentication;
    readonly deploymentTrust: MailDeploymentTrust;
    readonly authenticatedDomain: string | null;
    readonly authenticationSource: MailAuthenticationSource;
    readonly dkimDomain: string | null;
    readonly spfDomain: string | null;
    readonly dmarc: MailDmarcOutcome;

    /** What recognized the author, or `null` where nothing did. */
    readonly trustSource: MailTrustSource | null;
}

/** The result of each step a signature verification takes, in the order a reader is shown them. */
export interface MailSignatureSteps {
    readonly content: MailSignatureStepResult;
    readonly authority: MailSignatureStepResult;
    readonly validity: MailSignatureStepResult;
    readonly revocation: MailSignatureStepResult;
    readonly signer: MailSignatureStepResult;
}

/** Who signed, as their certificate or OpenPGP key names them. Every value is text the signer's material chose. */
export interface MailSigner {
    readonly name: string | null;
    readonly address: string | null;
    readonly issuer: string | null;
    readonly validFrom: string | null;
    readonly validUntil: string | null;
    readonly serialNumber: string | null;
    readonly fingerprint: string | null;
    readonly keyId: string | null;
    readonly publicKeyAlgorithm: string | null;
    readonly digestAlgorithm: string | null;
}

/**
 * What verifying a message's own S/MIME or OpenPGP signature found, step by step, beside the sender verdict and never
 * folded into it. Neither says a message is safe.
 */
export interface MailSignature {
    readonly outcome: MailSignatureOutcome;
    readonly format: MailSignatureFormat | null;
    readonly secureMimeType: MailSecureMimeType | null;
    readonly unverifiableReason: MailSignatureUnverifiableReason | null;
    readonly signedAt: string | null;
    readonly steps: MailSignatureSteps;
    readonly trustAnchor: MailSignatureTrustAnchor | null;
    readonly chainEnd: string | null;
    readonly validityFailure: MailSignatureUnverifiableReason | null;
    readonly revocationMethod: MailRevocationMethod | null;
    readonly revocationCheckedAt: string | null;
    readonly revokedAt: string | null;
    readonly signerMatchesAuthor: boolean;
    readonly signer: MailSigner | null;
}

/**
 * One file a message carries, described and carrying none of what it holds.
 *
 * The position is the identity because it is the only stable one a message's parts have, and it is what the download
 * route is asked with. The file name is text a sender chose: it arrives normalized to a bare name, and
 * `wasFileNameNormalized` says whether that rewrote anything — which is the case worth drawing carefully rather than a
 * detail to hide.
 */
export interface MailAttachment {
    readonly position: number;
    readonly fileName: string | null;
    readonly wasFileNameNormalized: boolean;
    readonly mediaType: string;
    readonly sizeOctets: number;
}

/** The counts for everything a message carries besides its body, or `null` where nothing has read its parts. */
export interface MailCarried {
    readonly attachmentCount: number;
    readonly totalSizeOctets: number;
    readonly inlineResourceCount: number;
    readonly encrypted: boolean;
    readonly unverifiedSignature: boolean;
    readonly unexpandedTnefPart: boolean;
}

/** One message as the reading pane draws it, without its body and without any file it carries. */
export interface MailMessage {
    readonly storedEmailId: string;
    readonly account: string;
    readonly folder: string;
    readonly threadId: string | null;
    readonly sizeOctets: number;
    readonly headers: MailMessageHeaders;
    readonly body: MailMessageBodyForms;
    readonly sender: MailSenderVerdict;
    readonly signature: MailSignature;
    readonly attachments: readonly MailAttachment[];
    readonly carried: MailCarried | null;
    readonly unread: boolean;
    readonly flagged: boolean;
    readonly answered: boolean;

    /** What spam classification concluded, read from the same summary the message's list row is, so the two agree. */
    readonly spam: MailSpamVerdict | null;
}

// A description composes to a stated size, so the backstop written for a stranger is far looser than this answer ever
// needs. It is generous against the bounds below rather than tight against them: the point of failing here is to stop a
// body being buffered whole, and the point of failing there is to refuse a description this client would not draw.
export const longestMessageAnswer = 4 * 1024 * 1024;

// What the service will compose at most, mirrored so a description larger than that is refused rather than drawn. The
// part count is the ceiling `MaxMimePartCount` defaults to, rounded up, because every part of a message can be an
// attachment; the address count is far above any real header set and exists for the case the answer is not one. Each is
// checked during the walk, because a bound applied after a walk that completed has already paid for what it read.
const bounds = {
    maximumAttachments: 1024,
    maximumParticipants: 2048,
    maximumReferences: 512,
    maximumTextLength: 4096,
    maximumFileNameLength: 1024,
    maximumMediaTypeLength: 256,
} as const;

const participantRoles: readonly MailParticipantRole[] = ['Sender', 'From', 'ReplyTo', 'To', 'Cc', 'Bcc'];

const authenticationSources: readonly MailAuthenticationSource[] = ['ReceivingServer', 'LocalVerification'];

const dmarcOutcomes: readonly MailDmarcOutcome[] = [
    'NotReported',
    'Pass',
    'Fail',
    'NoPolicyPublished',
    'TemporaryError',
    'PermanentError',
];

const trustSources: readonly MailTrustSource[] = ['OwnAccountDomain', 'ConfiguredTrustedSender', 'StoredTrustedSender'];

const signatureFormats: readonly MailSignatureFormat[] = ['SecureMimeDetached', 'SecureMimeOpaque', 'OpenPgpMime'];

const secureMimeTypes: readonly MailSecureMimeType[] = [
    'SignedData',
    'EnvelopedData',
    'AuthEnvelopedData',
    'CompressedData',
    'CertsOnly',
    'NotDeclared',
];

const unverifiableReasons: readonly MailSignatureUnverifiableReason[] = [
    'NoCertificate',
    'UntrustedRoot',
    'OutsideValidityPeriod',
    'WrongKeyUsage',
    'Revoked',
    'RevocationNotCheckable',
    'UnknownKey',
    'ExpiredKey',
    'RevokedKey',
    'UnsupportedAlgorithm',
    'MalformedSignature',
    'ContentNotOpened',
];

const stepResults: readonly MailSignatureStepResult[] = ['Passed', 'Failed', 'NotChecked', 'Skipped'];

const trustAnchors: readonly MailSignatureTrustAnchor[] = ['SystemTrustStore', 'ConfiguredAuthority', 'ConfiguredKey'];

const revocationMethods: readonly MailRevocationMethod[] = ['Ocsp', 'Crl', 'KeyRevocationSignature'];

/** Reads one message's description, answering an expected failure as a value rather than by throwing. */
export function readMailMessage(
    session: ClientSession,
    transport: MailFathomTransport,
    storedEmailId: string,
): Promise<ClientResult<MailMessage>> {
    return spanned('GET /messages/{storedEmailId}', async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, mailMessageRoute(storedEmailId)),
            headers: headersFor(session),
            longestAnswer: longestMessageAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            // The one route where a `404` is its own answer rather than one more way of not reaching the deployment:
            // it names a single message, so the deployment saying it does not hold that message is the deployment
            // answering. A reader whose open message was deleted or moved elsewhere is told it is gone and let go of
            // it, rather than being offered a retry that cannot succeed.
            const reason = response.status === 404 ? 'missing' : failureReasonForStatus(response.status);

            return failed(reason, response.status);
        }

        const message = parseMessage(response.body);

        return message === null ? failed('unreadable', response.status) : read(message);
    });
}

function parseMessage(body: string): MailMessage | null {
    try {
        return parseMailMessage(JSON.parse(body));
    } catch {
        return null;
    }
}

/**
 * Reads one message out of an answer that already carries it as a value, which is how a conversation carries its own.
 *
 * The conversation route answers with this route's message field for field rather than with a shape of its own, so the
 * parse is this one rather than a second one written to the same contract — two parsers of one message are how a client
 * comes to draw the same message differently on two screens.
 *
 * @param value The message as it arrived, in whatever shape it arrived in.
 * @returns The message, or `null` where what arrived is not one.
 */
export function parseMailMessage(value: unknown): MailMessage | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const storedEmailId = record['storedEmailId'];
    const account = record['account'];
    const folder = record['folder'];
    const threadId = record['threadId'] ?? null;
    const sizeOctets = record['sizeOctets'];

    if (!isText(storedEmailId) || !isText(account) || !isText(folder)) {
        return null;
    }

    if (threadId !== null && !isText(threadId)) {
        return null;
    }

    if (!isCount(sizeOctets)) {
        return null;
    }

    const headers = parseHeaders(record['headers']);
    const forms = parseBodyForms(record['body']);
    const sender = parseSenderVerdict(record['sender']);
    const signature = parseSignature(record['signature']);
    const attachments = parseAttachments(record['attachments']);
    const carried = parseCarried(record['carried'] ?? null);
    const unread = record['unread'];
    const flagged = record['flagged'];
    const answered = record['answered'];

    if (headers === null || forms === null || sender === null || attachments === null || carried === undefined) {
        return null;
    }

    if (signature === null) {
        return null;
    }

    if (typeof unread !== 'boolean' || typeof flagged !== 'boolean' || typeof answered !== 'boolean') {
        return null;
    }

    const judged = parseSpamVerdict(record['spam']);
    if (judged === null) {
        return null;
    }

    return {
        storedEmailId,
        account,
        folder,
        threadId,
        sizeOctets,
        headers,
        body: forms,
        sender,
        signature,
        attachments,
        carried,
        unread,
        flagged,
        answered,
        spam: judged.spam,
    };
}

function parseHeaders(value: unknown): MailMessageHeaders | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const subject = record['subject'] ?? null;
    const sentAt = record['sentAt'] ?? null;
    const receivedAt = record['receivedAt'] ?? null;
    const messageId = record['messageId'] ?? null;
    const inReplyTo = record['inReplyTo'] ?? null;

    if (!isOptionalText(subject) || !isOptionalText(sentAt) || !isOptionalText(receivedAt)) {
        return null;
    }

    if (!isOptionalText(messageId) || !isOptionalText(inReplyTo)) {
        return null;
    }

    const participants = parseParticipants(record['participants']);
    const references = parseReferences(record['references']);

    if (participants === null || references === null) {
        return null;
    }

    return { subject, sentAt, receivedAt, participants, messageId, inReplyTo, references };
}

function parseParticipants(value: unknown): readonly MailParticipant[] | null {
    if (!Array.isArray(value) || value.length > bounds.maximumParticipants) {
        return null;
    }

    const participants: MailParticipant[] = [];
    for (const entry of value) {
        const participant = parseParticipant(entry);
        if (participant === null) {
            return null;
        }

        participants.push(participant);
    }

    return participants;
}

function parseParticipant(value: unknown): MailParticipant | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const role = record['role'];
    const address = record['address'];
    const displayName = record['displayName'] ?? null;

    if (!isOneOf(role, participantRoles) || !isText(address) || !isOptionalText(displayName)) {
        return null;
    }

    return { role, address, displayName };
}

function parseReferences(value: unknown): readonly string[] | null {
    if (!Array.isArray(value) || value.length > bounds.maximumReferences) {
        return null;
    }

    const references: string[] = [];
    for (const entry of value) {
        if (!isText(entry)) {
            return null;
        }

        references.push(entry);
    }

    return references;
}

function parseBodyForms(value: unknown): MailMessageBodyForms | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const availability = record['availability'];
    const plainText = record['plainText'];
    const html = record['html'];

    if (!isText(availability) || typeof plainText !== 'boolean' || typeof html !== 'boolean') {
        return null;
    }

    return { availability, plainText, html };
}

function parseSenderVerdict(value: unknown): MailSenderVerdict | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const authorAuthentication = record['authorAuthentication'];
    const deploymentTrust = record['deploymentTrust'];
    const authenticatedDomain = record['authenticatedDomain'] ?? null;
    const authenticationSource = record['authenticationSource'];
    const dkimDomain = record['dkimDomain'] ?? null;
    const spfDomain = record['spfDomain'] ?? null;
    const dmarc = record['dmarc'];
    const trustSource = record['trustSource'] ?? null;

    if (!isOneOf(authorAuthentication, authorAuthentications) || !isOneOf(deploymentTrust, deploymentTrusts)) {
        return null;
    }

    if (!isOptionalText(authenticatedDomain) || !isOptionalText(dkimDomain) || !isOptionalText(spfDomain)) {
        return null;
    }

    if (!isOneOf(authenticationSource, authenticationSources) || !isOneOf(dmarc, dmarcOutcomes)) {
        return null;
    }

    if (trustSource !== null && !isOneOf(trustSource, trustSources)) {
        return null;
    }

    return {
        authorAuthentication,
        deploymentTrust,
        authenticatedDomain,
        authenticationSource,
        dkimDomain,
        spfDomain,
        dmarc,
        trustSource,
    };
}

function parseSignature(value: unknown): MailSignature | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const outcome = record['outcome'];
    const format = record['format'] ?? null;
    const secureMimeType = record['secureMimeType'] ?? null;
    const unverifiableReason = record['unverifiableReason'] ?? null;
    const signedAt = record['signedAt'] ?? null;
    const trustAnchor = record['trustAnchor'] ?? null;
    const chainEnd = record['chainEnd'] ?? null;
    const validityFailure = record['validityFailure'] ?? null;
    const revocationMethod = record['revocationMethod'] ?? null;
    const revocationCheckedAt = record['revocationCheckedAt'] ?? null;
    const revokedAt = record['revokedAt'] ?? null;
    const signerMatchesAuthor = record['signerMatchesAuthor'];

    if (!isOneOf(outcome, signatureOutcomes) || typeof signerMatchesAuthor !== 'boolean') {
        return null;
    }

    if (!isOptionalOneOf(format, signatureFormats) || !isOptionalOneOf(secureMimeType, secureMimeTypes)) {
        return null;
    }

    if (
        !isOptionalOneOf(unverifiableReason, unverifiableReasons) ||
        !isOptionalOneOf(validityFailure, unverifiableReasons)
    ) {
        return null;
    }

    if (!isOptionalOneOf(trustAnchor, trustAnchors) || !isOptionalOneOf(revocationMethod, revocationMethods)) {
        return null;
    }

    if (!isOptionalText(signedAt) || !isOptionalText(chainEnd) || !isOptionalText(revocationCheckedAt)) {
        return null;
    }

    if (!isOptionalText(revokedAt)) {
        return null;
    }

    const steps = parseSignatureSteps(record['steps']);
    const signerValue = record['signer'] ?? null;
    const signer = signerValue === null ? null : parseSigner(signerValue);

    if (steps === null || (signerValue !== null && signer === null)) {
        return null;
    }

    return {
        outcome,
        format,
        secureMimeType,
        unverifiableReason,
        signedAt,
        steps,
        trustAnchor,
        chainEnd,
        validityFailure,
        revocationMethod,
        revocationCheckedAt,
        revokedAt,
        signerMatchesAuthor,
        signer,
    };
}

function parseSignatureSteps(value: unknown): MailSignatureSteps | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const content = record['content'];
    const authority = record['authority'];
    const validity = record['validity'];
    const revocation = record['revocation'];
    const signer = record['signer'];

    if (!isOneOf(content, stepResults) || !isOneOf(authority, stepResults) || !isOneOf(validity, stepResults)) {
        return null;
    }

    if (!isOneOf(revocation, stepResults) || !isOneOf(signer, stepResults)) {
        return null;
    }

    return { content, authority, validity, revocation, signer };
}

function parseSigner(value: unknown): MailSigner | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const name = record['name'] ?? null;
    const address = record['address'] ?? null;
    const issuer = record['issuer'] ?? null;
    const validFrom = record['validFrom'] ?? null;
    const validUntil = record['validUntil'] ?? null;
    const serialNumber = record['serialNumber'] ?? null;
    const fingerprint = record['fingerprint'] ?? null;
    const keyId = record['keyId'] ?? null;
    const publicKeyAlgorithm = record['publicKeyAlgorithm'] ?? null;
    const digestAlgorithm = record['digestAlgorithm'] ?? null;

    if (!isOptionalText(name) || !isOptionalText(address) || !isOptionalText(issuer)) {
        return null;
    }

    if (!isOptionalText(validFrom) || !isOptionalText(validUntil) || !isOptionalText(serialNumber)) {
        return null;
    }

    if (!isOptionalText(fingerprint) || !isOptionalText(keyId)) {
        return null;
    }

    if (!isOptionalText(publicKeyAlgorithm) || !isOptionalText(digestAlgorithm)) {
        return null;
    }

    return {
        name,
        address,
        issuer,
        validFrom,
        validUntil,
        serialNumber,
        fingerprint,
        keyId,
        publicKeyAlgorithm,
        digestAlgorithm,
    };
}

function parseAttachments(value: unknown): readonly MailAttachment[] | null {
    if (!Array.isArray(value) || value.length > bounds.maximumAttachments) {
        return null;
    }

    const attachments: MailAttachment[] = [];
    for (const entry of value) {
        const attachment = parseAttachment(entry);
        if (attachment === null) {
            return null;
        }

        attachments.push(attachment);
    }

    return attachments;
}

/**
 * One file a message carries, or `null` where what arrived is not a description this client can draw.
 *
 * Exported for `citedPassages.ts`, which follows a citation to a file and is answered with these same members — the
 * service composes a cited file out of the message route's own description for exactly that reason, so a second
 * parser here would be a second reading of one contract.
 */
export function parseAttachment(value: unknown): MailAttachment | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const position = record['position'];
    const fileName = record['fileName'] ?? null;
    const wasFileNameNormalized = record['wasFileNameNormalized'];
    const mediaType = record['mediaType'];
    const sizeOctets = record['sizeOctets'];

    if (!isCount(position) || typeof wasFileNameNormalized !== 'boolean' || !isCount(sizeOctets)) {
        return null;
    }

    if (fileName !== null && (!isText(fileName) || fileName.length > bounds.maximumFileNameLength)) {
        return null;
    }

    if (!isText(mediaType) || mediaType.length > bounds.maximumMediaTypeLength) {
        return null;
    }

    return { position, fileName, wasFileNameNormalized, mediaType, sizeOctets };
}

// Answers `undefined` for a shape it refuses, because `null` is one of the two answers the service legitimately gives:
// a message whose parts nothing has ever read carries no counts at all.
function parseCarried(value: unknown): MailCarried | null | undefined {
    if (value === null) {
        return null;
    }

    const record = asRecord(value);
    if (record === null) {
        return undefined;
    }

    const attachmentCount = record['attachmentCount'];
    const totalSizeOctets = record['totalSizeOctets'];
    const inlineResourceCount = record['inlineResourceCount'];
    const encrypted = record['encrypted'];
    const unverifiedSignature = record['unverifiedSignature'];
    const unexpandedTnefPart = record['unexpandedTnefPart'];

    if (!isCount(attachmentCount) || !isCount(totalSizeOctets) || !isCount(inlineResourceCount)) {
        return undefined;
    }

    if (
        typeof encrypted !== 'boolean' ||
        typeof unverifiedSignature !== 'boolean' ||
        typeof unexpandedTnefPart !== 'boolean'
    ) {
        return undefined;
    }

    return {
        attachmentCount,
        totalSizeOctets,
        inlineResourceCount,
        encrypted,
        unverifiedSignature,
        unexpandedTnefPart,
    };
}

function asRecord(value: unknown): Readonly<Record<string, unknown>> | null {
    return typeof value === 'object' && value !== null && !Array.isArray(value)
        ? (value as Record<string, unknown>)
        : null;
}

function isText(value: unknown): value is string {
    return typeof value === 'string' && value.length <= bounds.maximumTextLength;
}

function isOptionalText(value: unknown): value is string | null {
    return value === null || isText(value);
}

function isOptionalOneOf<TValue extends string>(value: unknown, members: readonly TValue[]): value is TValue | null {
    return value === null || isOneOf(value, members);
}

function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
