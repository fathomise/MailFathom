// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { contactRoute, contactsRoute } from './contacts';
import type { ClientFailureReason } from './failure';
import { largestPortraitOctets, portraitImageTypes, type PortraitImageType } from './ownPortrait';
import { headersFor, routeFor, type ClientSession } from './session';
import { reported } from './telemetry';
import type { ClientRequest } from './transport';

// The photo a person the signed-in user wrote down in their own book is drawn by. It is stored, bounded, and judged
// exactly as the user's own portrait is, so the kinds and the bound are that module's rather than restated here; what
// differs is only the route, which names the contact. As there, the adapter carries the octets and this module
// composes the request and keeps the record of it.

/** The route a contact's photo is read, replaced, and removed at, relative to the client prefix. */
export function contactPhotoRoute(contactId: string): string {
    return `${contactRoute(contactId)}/photo`;
}

const contactPhotoTraceRoute = `${contactsRoute}/{contactId}/photo`;

/** Composes the request that reads a contact's photo, asking for the two kinds the surface serves. */
export function readContactPhotoRequest(session: ClientSession, contactId: string): ClientRequest {
    return {
        method: 'GET',
        path: routeFor(session, contactPhotoRoute(contactId)),
        headers: { ...headersFor(session), Accept: portraitImageTypes.join(', ') },
        longestAnswer: largestPortraitOctets,
    };
}

/** Composes the request that replaces a contact's photo, under the kind the chosen file was found to be. */
export function replaceContactPhotoRequest(
    session: ClientSession,
    contactId: string,
    type: PortraitImageType,
): ClientRequest {
    return {
        method: 'POST',
        path: routeFor(session, contactPhotoRoute(contactId)),
        headers: { ...headersFor(session), 'Content-Type': type },
    };
}

/** Composes the request that removes a contact's photo, leaving the rest of the contact as it was. */
export function removeContactPhotoRequest(session: ClientSession, contactId: string): ClientRequest {
    return {
        method: 'DELETE',
        path: routeFor(session, contactPhotoRoute(contactId)),
        headers: headersFor(session),
    };
}

/**
 * Reads a contact's photo through an adapter, and reports the request.
 *
 * @param failureOf Which failure the answer amounts to, or `null` where it is one the screen draws — a contact with no
 * photo among them.
 */
export function readContactPhoto<TOutcome>(
    session: ClientSession,
    contactId: string,
    deliver: (request: ClientRequest) => Promise<TOutcome>,
    failureOf: (outcome: TOutcome) => ClientFailureReason | null,
): Promise<TOutcome> {
    return reported(
        `GET ${contactPhotoTraceRoute}`,
        () => deliver(readContactPhotoRequest(session, contactId)),
        failureOf,
    );
}

/** Replaces a contact's photo through an adapter, which is what carries the octets, and reports the request. */
export function replaceContactPhoto<TOutcome>(
    session: ClientSession,
    contactId: string,
    type: PortraitImageType,
    deliver: (request: ClientRequest) => Promise<TOutcome>,
    failureOf: (outcome: TOutcome) => ClientFailureReason | null,
): Promise<TOutcome> {
    return reported(
        `POST ${contactPhotoTraceRoute}`,
        () => deliver(replaceContactPhotoRequest(session, contactId, type)),
        failureOf,
    );
}

/** Removes a contact's photo through an adapter, and reports the request. */
export function removeContactPhoto<TOutcome>(
    session: ClientSession,
    contactId: string,
    deliver: (request: ClientRequest) => Promise<TOutcome>,
    failureOf: (outcome: TOutcome) => ClientFailureReason | null,
): Promise<TOutcome> {
    return reported(
        `DELETE ${contactPhotoTraceRoute}`,
        () => deliver(removeContactPhotoRequest(session, contactId)),
        failureOf,
    );
}
