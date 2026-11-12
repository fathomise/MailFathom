// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { agentConversationRoute } from './agentConversations';
import { failed, failureReasonForStatus, read, type ClientFailureReason, type ClientResult } from './failure';
import { asRecord, parseEach } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { reported, spanned } from './telemetry';
import { send, type ClientRequest, type ClientResponse, type MailFathomTransport } from './transport';

// The files a person hands the Agent in a conversation, as `/api/client` takes and lists them. A file is posted as the
// request body itself, named by a `Content-Disposition` header, into the conversation the client names — which the
// service starts there where nothing stands under that identifier yet, so a file attached before the first question is
// held by the conversation that question then starts. A question then names the files it carries by their identifiers.

/** The most octets one file may hold, which is the service's own bound. */
export const largestAgentFile = 16 * 1024 * 1024;

/** The most files one question may carry, which is the service's own bound. */
export const mostFilesPerQuestion = 10;

/** The most files one conversation may hold, which is the service's own bound. */
export const mostFilesPerConversation = 20;

// One file's record, and the whole list of them, each with room to spare; a name is bounded where the service bounds
// it, as a mail attachment's name is.
const longestFileAnswer = 8 * 1024;
const longestFileListing = 64 * 1024;
const longestName = 255;
const longestIdentifier = 128;
const longestMediaType = 255;

/** One file a conversation holds. */
export interface AgentConversationFile {
    readonly fileId: string;

    /** The name the person's machine gave it, or `null` where it gave none the service could use. */
    readonly name: string | null;

    /** A picture the model is shown, or a document it reads. */
    readonly kind: 'document' | 'picture';

    /** What its octets proved it to be. */
    readonly mediaType: string;
    readonly byteLength: number;
}

/**
 * Why the service did not take a file, each a sentence of its own on the screen.
 *
 * `tooLarge` is over {@link largestAgentFile}, `notRead` is octets that are neither a document the Agent reads nor a
 * picture it can be shown — whatever the name says — `conversationFull` is a conversation already holding
 * {@link mostFilesPerConversation}, and `empty` is a file with nothing in it.
 */
export type AgentFileRefusal = 'tooLarge' | 'notRead' | 'conversationFull' | 'empty';

/** What taking one file into a conversation came to: the file it now holds, or the service's reason for refusing it. */
export type AgentFileIntake =
    | { readonly outcome: 'taken'; readonly file: AgentConversationFile }
    | { readonly outcome: 'refused'; readonly refusal: AgentFileRefusal };

/** Where a conversation's files are taken in and listed. */
export function agentConversationFilesRoute(conversationId: string): string {
    return `${agentConversationRoute(conversationId)}/files`;
}

/**
 * The request that takes one file into a conversation, whose body the caller supplies: the octets are a `Blob`, which
 * this package cannot name, so an adapter on the application's side puts them on the wire.
 *
 * @param fileName The person's own name for the file, which travels in the header as RFC 8187 encoded UTF-8 and decides
 * nothing at the service.
 */
export function agentConversationFileUploadRequest(
    session: ClientSession,
    conversationId: string,
    fileName: string,
): ClientRequest {
    return {
        method: 'POST',
        path: routeFor(session, agentConversationFilesRoute(conversationId)),
        headers: {
            ...headersFor(session),
            'Content-Type': 'application/octet-stream',
            'Content-Disposition': `attachment; filename*=UTF-8''${encodedHeaderValue(fileName)}`,
        },
        longestAnswer: longestFileAnswer,
    };
}

/**
 * Takes one file into a conversation, through an adapter, and reports the request the way every other one is.
 *
 * @param deliver Puts the composed request on the wire with the octets, answering `null` where nothing answered at
 * all — a connection refused, or an upload the person abandoned.
 * @returns The file, the service's refusal, or why nothing was decided — `missing` where the conversation is somebody
 * else's.
 */
export function takeAgentConversationFile(
    session: ClientSession,
    conversationId: string,
    fileName: string,
    deliver: (request: ClientRequest) => Promise<ClientResponse | null>,
): Promise<ClientResult<AgentFileIntake>> {
    return spanned('POST /agent/conversations/{conversationId}/files', async () =>
        intakeOf(await deliver(agentConversationFileUploadRequest(session, conversationId, fileName))),
    );
}

/**
 * Lists the files one conversation holds, in the order they were taken in — none for a conversation that holds none,
 * or that is not this person's.
 */
export function readAgentConversationFiles(
    session: ClientSession,
    transport: MailFathomTransport,
    conversationId: string,
): Promise<ClientResult<readonly AgentConversationFile[]>> {
    return spanned('GET /agent/conversations/{conversationId}/files', async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, agentConversationFilesRoute(conversationId)),
            headers: headersFor(session),
            longestAnswer: longestFileListing,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const files = parseEach(asRecord(parsedJson(response.body))?.['files'], mostFilesPerConversation, parseFile);

        return files === null ? failed('unreadable', response.status) : read(files);
    });
}

/** The route one file of a conversation is downloaded from, relative to the client prefix. */
export function agentConversationFileRoute(conversationId: string, fileId: string): string {
    return `${agentConversationFilesRoute(conversationId)}/${encodeURIComponent(fileId)}`;
}

// A file is downloaded as a message's attachment is, and for the same reason: what it answers with is octets and a
// `MailFathomTransport` answers with text, so this module composes the request and an adapter puts it on the wire. A
// `404` is the deployment's single answer for a conversation deleted since, a file it no longer holds, and either
// belonging to somebody else, which `attachmentRefusalForStatus` already reads as `missing`.

/**
 * Composes the request that downloads one file of a conversation — one the person handed over, or a report the Agent
 * wrote into it.
 *
 * @param describedSizeOctets How many octets the answer said the file holds, which becomes the bound the answer is
 * read under, so a deployment cannot hand a reader more than the screen told them they were asking for.
 */
export function agentConversationFileRequest(
    session: ClientSession,
    conversationId: string,
    fileId: string,
    describedSizeOctets: number,
): ClientRequest {
    return {
        method: 'GET',
        path: routeFor(session, agentConversationFileRoute(conversationId, fileId)),
        headers: { ...headersFor(session), Accept: 'application/octet-stream' },
        longestAnswer: describedSizeOctets,
    };
}

/**
 * Downloads one file of a conversation, through an adapter, and reports the request the way every other one is.
 *
 * The composition sits inside the span for the reason `readMailAttachment` gives: the trace header is written from
 * whatever span is active when the request is composed.
 *
 * @param deliver Puts the composed request on the wire and answers whatever the application makes of it.
 * @param failureOf Which failure that answer amounts to, or `null` where the client got an answer it acts on.
 */
export function readAgentConversationFile<TOutcome>(
    session: ClientSession,
    conversationId: string,
    fileId: string,
    describedSizeOctets: number,
    deliver: (request: ClientRequest) => Promise<TOutcome>,
    failureOf: (outcome: TOutcome) => ClientFailureReason | null,
): Promise<TOutcome> {
    return reported(
        'GET /agent/conversations/{conversationId}/files/{fileId}',
        () => deliver(agentConversationFileRequest(session, conversationId, fileId, describedSizeOctets)),
        failureOf,
    );
}

// Each status the intake route answers with a reason of its own.
const refusals: Readonly<Partial<Record<number, AgentFileRefusal>>> = {
    400: 'empty',
    409: 'conversationFull',
    413: 'tooLarge',
    415: 'notRead',
};

function intakeOf(response: ClientResponse | null): ClientResult<AgentFileIntake> {
    if (response === null) {
        return failed('unavailable', null);
    }

    const refusal = refusals[response.status];

    if (refusal !== undefined) {
        return read({ outcome: 'refused', refusal });
    }

    if (response.status === 404) {
        return failed('missing', response.status);
    }

    if (response.status !== 201) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const file = parseFile(parsedJson(response.body));

    return file === null ? failed('unreadable', response.status) : read({ outcome: 'taken', file });
}

const kinds: Readonly<Record<string, AgentConversationFile['kind'] | undefined>> = {
    document: 'document',
    picture: 'picture',
};

function parseFile(value: unknown): AgentConversationFile | null {
    const record = asRecord(value);
    const fileId = record?.['fileId'];
    const name = record?.['name'];
    const kind =
        typeof record?.['kind'] === 'string' && Object.hasOwn(kinds, record['kind'])
            ? kinds[record['kind']]
            : undefined;
    const mediaType = record?.['mediaType'];
    const byteLength = record?.['byteLength'];

    return typeof fileId === 'string' &&
        fileId.length > 0 &&
        fileId.length <= longestIdentifier &&
        (name === null || (typeof name === 'string' && name.length <= longestName)) &&
        kind !== undefined &&
        typeof mediaType === 'string' &&
        mediaType.length <= longestMediaType &&
        typeof byteLength === 'number' &&
        Number.isSafeInteger(byteLength) &&
        byteLength >= 0
        ? { fileId, name, kind, mediaType, byteLength }
        : null;
}

function parsedJson(body: string): unknown {
    try {
        return JSON.parse(body) as unknown;
    } catch {
        return null;
    }
}

// RFC 8187's `attr-char` leaves out what `encodeURIComponent` keeps, so those are escaped as well.
function encodedHeaderValue(value: string): string {
    return encodeURIComponent(value).replace(
        /['()*!]/gu,
        (character) => `%${character.charCodeAt(0).toString(16).toUpperCase()}`,
    );
}
