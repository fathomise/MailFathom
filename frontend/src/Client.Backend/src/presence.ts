// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { bodyOf } from './contacts';
import { failed, failureReasonForStatus, read, type ClientResult } from './failure';
import { asRecord } from './json';
import { headersFor, routeFor, type ClientSession } from './session';
import { spanned } from './telemetry';
import { send, type ClientResponse, type MailFathomTransport } from './transport';

// The signed-in member's own presence: the clients they keep open, and the state they chose.
//
// A running client renews a row of its own every minute while it is open and removes it as it closes, which is what
// keeps a member present rather than anything the signal hub holds. Whether a person is at the keyboard is measured by
// the client and stated on each renewal; what the member reads as — to themselves and to their colleagues — is worked
// out by the deployment when it is read, so nothing here decides a state.
//
// A caller who belongs to no organization is answered `404` on every route here, because nobody could see their
// presence and nothing is recorded about them. That is `missing` here, which is what tells a client to draw no presence
// and to stop renewing.

/** The route the caller's own presence is read at, relative to the client prefix. */
export const presenceRoute = '/presence';

/** The route the caller's chosen presence is written at, relative to the client prefix. */
export const presenceChoiceRoute = `${presenceRoute}/choice`;

/** The route one of the caller's open clients is renewed and closed at, relative to the client prefix. */
export function presenceClientRoute(client: string): string {
    return `${presenceRoute}/clients/${encodeURIComponent(client)}`;
}

/** The six states a member may be in, in the order the design lists them. */
export const presenceStates = ['Active', 'Away', 'Busy', 'DoNotDisturb', 'Offline', 'Hidden'] as const;

/** A member's presence as the member reads it, `Hidden` included. */
export type PresenceState = (typeof presenceStates)[number];

/** A colleague's presence as the other members read it: a member who chose to hide reads as `Offline`. */
export type ColleaguePresence = Exclude<PresenceState, 'Hidden'>;

/** Whether somebody is using a client, which the client measures from its own input. */
export type PresenceActivity = 'active' | 'idle';

/** How long a chosen state lasts, where it does not last until changed. */
export const presenceDurations = ['30m', '1h', '2h', 'today', 'week'] as const;

export type PresenceDuration = (typeof presenceDurations)[number];

/** The signed-in member's presence as they see it themselves. */
export interface OwnPresence {
    /** What they read as to themselves, `Hidden` where they chose to hide. */
    readonly state: PresenceState;

    /** The state they chose while that choice holds, or `null` where their clients decide it. */
    readonly chosenState: PresenceState | null;

    /** The while the choice was made for, or `null` where it lasts until changed or there is none. */
    readonly choiceDuration: PresenceDuration | null;

    /** When the choice runs out, or `null` where it lasts until changed or there is none. */
    readonly choiceEndsAt: string | null;
}

// The four fields compose to a few dozen bytes, and a renewal answers with none.
const longestPresenceAnswer = 1_024;
const longestInstant = 64;

/** Reads the signed-in member's own presence; a caller in no organization is `missing`. */
export function readOwnPresence(
    session: ClientSession,
    transport: MailFathomTransport,
): Promise<ClientResult<OwnPresence>> {
    return spanned(`GET ${presenceRoute}`, async () =>
        presenceOf(
            await send(transport, {
                method: 'GET',
                path: routeFor(session, presenceRoute),
                headers: headersFor(session),
                longestAnswer: longestPresenceAnswer,
            }),
        ),
    );
}

/**
 * Chooses a state, for a while or until changed. A state chosen with no duration ends any while chosen before, and
 * `Active` chosen until changed returns the member to the presence their clients produce.
 */
export function choosePresence(
    session: ClientSession,
    transport: MailFathomTransport,
    state: PresenceState,
    duration: PresenceDuration | null,
): Promise<ClientResult<OwnPresence>> {
    return spanned(`PUT ${presenceChoiceRoute}`, async () =>
        presenceOf(
            await send(transport, {
                method: 'PUT',
                path: routeFor(session, presenceChoiceRoute),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify({ state, duration }),
                longestAnswer: longestPresenceAnswer,
            }),
        ),
    );
}

/**
 * Renews one open client, stating whether somebody is using it.
 *
 * It is put on the wire once: the caller renews on a schedule of its own, and a retry underneath would be a renewal
 * the next one already makes.
 *
 * @returns Nothing read where it was renewed, and `missing` where the caller belongs to no organization — which is the
 * one answer that stops the renewing.
 */
export function renewPresenceClient(
    session: ClientSession,
    transport: MailFathomTransport,
    client: string,
    activity: PresenceActivity,
): Promise<ClientResult<null>> {
    return spanned(`PUT ${presenceRoute}/clients/{client}`, async () =>
        emptyAnswerOf(
            await send(transport, {
                method: 'PUT',
                path: routeFor(session, presenceClientRoute(client)),
                headers: { ...headersFor(session), 'Content-Type': 'application/json' },
                body: JSON.stringify({ activity }),
                longestAnswer: longestPresenceAnswer,
                retriedByCaller: true,
            }),
        ),
    );
}

/**
 * Says that one client is closing, as the page goes away or its person signs out.
 *
 * Nothing waits on the answer and nothing may rest on it having arrived: a row nobody removed expires on its own.
 */
export function closePresenceClient(
    session: ClientSession,
    transport: MailFathomTransport,
    client: string,
): Promise<ClientResult<null>> {
    return spanned(`DELETE ${presenceRoute}/clients/{client}`, async () =>
        emptyAnswerOf(
            await send(transport, {
                method: 'DELETE',
                path: routeFor(session, presenceClientRoute(client)),
                headers: headersFor(session),
                longestAnswer: longestPresenceAnswer,
                outlivesThePage: true,
            }),
        ),
    );
}

function emptyAnswerOf(response: ClientResponse | null): ClientResult<null> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 404) {
        return failed('missing', response.status);
    }

    return response.status === 204 ? read(null) : failed(failureReasonForStatus(response.status), response.status);
}

function presenceOf(response: ClientResponse | null): ClientResult<OwnPresence> {
    if (response === null) {
        return failed('unavailable', null);
    }

    if (response.status === 404) {
        return failed('missing', response.status);
    }

    if (response.status !== 200) {
        return failed(failureReasonForStatus(response.status), response.status);
    }

    const presence = parseOwnPresence(bodyOf(response));

    return presence === null ? failed('unreadable', response.status) : read(presence);
}

function parseOwnPresence(value: unknown): OwnPresence | null {
    const record = asRecord(value);
    const state = record?.['state'];
    const chosenState = record?.['chosenState'] ?? null;
    const choiceDuration = record?.['choiceDuration'] ?? null;
    const choiceEndsAt = record?.['choiceEndsAt'] ?? null;

    if (!isPresenceState(state) || (chosenState !== null && !isPresenceState(chosenState))) {
        return null;
    }

    // A while is the while of a choice, named and ended together, so an end with nothing chosen, an end with no
    // while, or a while with no end is not an answer the service gives.
    if (choiceEndsAt !== null && (chosenState === null || !isInstant(choiceEndsAt))) {
        return null;
    }

    if (choiceDuration !== null && !isPresenceDuration(choiceDuration)) {
        return null;
    }

    if ((choiceDuration === null) !== (choiceEndsAt === null)) {
        return null;
    }

    return { state, chosenState, choiceDuration, choiceEndsAt };
}

function isPresenceDuration(value: unknown): value is PresenceDuration {
    return (presenceDurations as readonly unknown[]).includes(value);
}

/** Whether a value names one of the six states, spelt as the client surface spells them. */
export function isPresenceState(value: unknown): value is PresenceState {
    return (presenceStates as readonly unknown[]).includes(value);
}

/** Whether a value is an instant the service wrote: readable as one, and of a length one is written in. */
export function isInstant(value: unknown): value is string {
    return (
        typeof value === 'string' &&
        value.length > 0 &&
        value.length <= longestInstant &&
        !Number.isNaN(Date.parse(value))
    );
}
