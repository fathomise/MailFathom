// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord, parseEach } from './json';
import { isOneOf } from './senderFacts';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type MailFathomTransport } from './transport';

// Which photo a person is drawn by, wherever a response names somebody by address. The deployment resolves it in the
// order a person is shown in everywhere else — the reader's own book, then their colleagues — and answers a reference
// rather than a picture: which route reads it, whose it is, and a name that changes whenever the photo does. A screen
// reads the picture itself through the contact or colleague photo route, exactly as it does on the People screen.
//
// A mail list, a search, a conversation, and a message carry the reference on the row they describe, so no list asks
// for photos one row at a time. What does not carry it is an AI answer, whose blocks are composed once and kept: the
// people one block names are resolved here in one request when it is drawn.

/** Which route a photo is read at: a contact of the reader's own book, or a colleague. */
export type PersonPhotoBook = 'Own' | 'Member';

const personPhotoBooks: readonly PersonPhotoBook[] = ['Own', 'Member'];

/** The photo one person is drawn by, named rather than carried. */
export interface PersonPhotoReference {
    readonly book: PersonPhotoBook;

    /** The contact or the colleague the photo belongs to, which is what its route names. */
    readonly id: string;

    /** The stored file the photo is, which changes whenever the photo does. */
    readonly photo: string;
}

/** The route the photos of a set of addresses are resolved at, relative to the client prefix. */
export const personPhotosRoute = '/contacts/photos';

/** The most addresses one request resolves, which is the deployment's bound rather than a preference. */
export const mostPersonPhotos = 200;

const longestIdentity = 256;
const longestAddress = 320;
const longestPhotosAnswer = 128 * 1024;

/**
 * Reads the photo reference a response carries beside a person, which is `null` for one drawn by their initials.
 *
 * @param value The field as the deployment sent it, absent included.
 * @returns The reference or `null` inside the answer, or `null` itself where what arrived is no reference at all.
 */
export function parsePersonPhoto(value: unknown): { readonly photo: PersonPhotoReference | null } | null {
    if (value === undefined || value === null) {
        return { photo: null };
    }

    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const book = record['book'];
    const id = record['id'];
    const photo = record['photo'];

    if (!isOneOf(book, personPhotoBooks) || !isIdentity(id) || !isIdentity(photo)) {
        return null;
    }

    return { photo: { book, id, photo } };
}

/**
 * Resolves the photo each of a set of addresses is drawn by, answering an expected failure as a value.
 *
 * @param session The address to reach and the finished header value to present.
 * @param transport How the request goes out.
 * @param addresses The addresses to resolve, at most {@link mostPersonPhotos} of them.
 * @returns Each address drawn by a photo, under the spelling it was asked by; one drawn by initials is absent.
 */
export function readPersonPhotos(
    session: ClientSession,
    transport: MailFathomTransport,
    addresses: readonly string[],
): Promise<ClientResult<ReadonlyMap<string, PersonPhotoReference>>> {
    return spanned(`POST ${personPhotosRoute}`, async () => {
        if (addresses.length > mostPersonPhotos) {
            // Refused here rather than sent, because a request the route answers `400` to is one this client composed
            // wrongly rather than a deployment that would not answer.
            return failed('unreadable', null);
        }

        if (addresses.length === 0) {
            return read(new Map<string, PersonPhotoReference>());
        }

        const response = await send(transport, {
            method: 'POST',
            changesNothing: true,
            path: routeFor(session, personPhotosRoute),
            headers: { ...headersFor(session), 'Content-Type': 'application/json' },
            body: JSON.stringify({ addresses }),
            longestAnswer: longestPhotosAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const photos = parseAnswer(response.body);

        return photos === null ? failed('unreadable', response.status) : read(photos);
    });
}

function parseAnswer(body: string): ReadonlyMap<string, PersonPhotoReference> | null {
    let parsed: unknown;

    try {
        parsed = JSON.parse(body);
    } catch {
        return null;
    }

    const entries = parseEach(asRecord(parsed)?.['photos'], mostPersonPhotos, parseEntry);

    return entries === null ? null : new Map(entries);
}

function parseEntry(value: unknown): readonly [string, PersonPhotoReference] | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const address = record['address'];
    const photo = parsePersonPhoto(record['photo'])?.photo ?? null;

    if (typeof address !== 'string' || address.length === 0 || address.length > longestAddress) {
        return null;
    }

    if (photo === null) {
        return null;
    }

    return [address, photo];
}

function isIdentity(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentity;
}
