// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import {
    bodyOf,
    contactsPerPage,
    contactsRoute,
    longestContactSearch,
    mostContactsPerPage,
    pageArguments,
    parsePhoneNumbers,
    writeOf,
    type ContactPageRequest,
    type ContactPhoneNumber,
    type ContactWrite,
} from './contacts';
import { failed, failureReasonForStatus, read, type ClientFailureReason, type ClientResult } from './failure';
import { asRecord, parseEach } from './json';
import { largestPortraitOctets, portraitImageTypes } from './ownPortrait';
import { headersFor, routeFor, type ClientSession } from './session';
import { reported, spanned } from './telemetry';
import { send, type ClientRequest, type MailFathomTransport } from './transport';

// The members of the reader's organization, which the service serves as a directory beside the books rather than as a
// third one: a member is derived from their user record — their name, their accounts' addresses, their portrait, the
// title an administrator gave them, and the zone they live in — and nobody but them and that administrator writes any
// of it. So there is no write route here at all, and a member is read-only by construction; what a reader may do is
// take a copy into their own book, which the promotion does.
//
// A reader who belongs to no organization is answered an empty directory rather than a refusal, so the listing reads
// every status as any other route does. One member read alone is one person, so a `404` there is `missing`, as it is
// for a contact.

/** The route the members of the reader's organization are served at, relative to the client prefix. */
export const colleaguesRoute = `${contactsRoute}/colleagues`;

/** The route one member of the reader's organization is read at, relative to the client prefix. */
export function colleagueRoute(userId: string): string {
    return `${colleaguesRoute}/${encodeURIComponent(userId)}`;
}

/** The route a member's portrait is read at, relative to the client prefix. */
export function colleaguePhotoRoute(userId: string): string {
    return `${colleagueRoute(userId)}/photo`;
}

/** The route a member is copied into the reader's own book at, relative to the client prefix. */
export function colleaguePromotionRoute(userId: string): string {
    return `${colleagueRoute(userId)}/promotion`;
}

/** One member of the reader's organization, as the directory derives them from their user record. */
export interface Colleague {
    /** The member's user identity, which is how the directory is asked about them and never something to show. */
    readonly id: string;

    /** Which book the member is listed in, which is the directory and tells them apart from any contact. */
    readonly book: 'Member';

    readonly displayName: string;

    /** Their mail accounts' addresses, never a shared mailbox's; none for a member who holds no account. */
    readonly addresses: readonly string[];

    /** The address the member chose to be written to, or `null` exactly where they hold no address at all. */
    readonly preferredAddress: string | null;

    readonly phoneNumbers: readonly ContactPhoneNumber[];

    /** The title the administrator gave them, or `null` where none is stated. */
    readonly jobTitle: string | null;

    /** The current name of the organization they belong to. */
    readonly organizationName: string;

    /** The IANA zone the member lives in, or `null` where they state none — in which case no time of theirs is known. */
    readonly timeZone: string | null;

    /** When the member works, on their own wall clock and so read in {@link timeZone}, or `null` where none is stated. */
    readonly workingHours: ColleagueWorkingHours | null;

    /** Which portrait they are drawn by, changing whenever it does, or `null` where they have none. */
    readonly photo: string | null;
}

/** A day of the week as the service names one. */
export type Weekday = (typeof weekdays)[number];

/** Every day of the week, Monday first, which is the order the service lists working days in. */
export const weekdays = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const;

/** When a member works: the days, and one range of hours those days share, as minutes after midnight on their clock. */
export interface ColleagueWorkingHours {
    /** The days worked, each once, Monday first. */
    readonly days: readonly Weekday[];

    readonly start: number;

    /** Always after {@link start}: a range across midnight is not one the service states. */
    readonly end: number;
}

/** One bounded page of the directory, and where the walk continues. */
export interface ColleaguePage {
    readonly colleagues: readonly Colleague[];

    /** The cursor the following page is asked with, or `null` at the end of the directory. */
    readonly nextCursor: string | null;
}

// The service's own bound on a title, and a zone longer than any IANA identifier by a wide margin.
const longestJobTitle = 256;
const longestTimeZone = 64;
const longestName = 1024;
const mostAddressesPerColleague = 64;

// A member carries what a contact carries less the note, so the arithmetic is the book's.
const longestColleaguePage = 512 * 1024;
const longestColleagueAnswer = 32 * 1024;

/** Reads one page of the members of the reader's organization, which is an empty one for a reader in none. */
export function readColleagues(
    session: ClientSession,
    transport: MailFathomTransport,
    page: ContactPageRequest = {},
): Promise<ClientResult<ColleaguePage>> {
    const pageSize = Math.min(page.pageSize ?? contactsPerPage, mostContactsPerPage);

    return spanned(`GET ${colleaguesRoute}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: `${routeFor(session, colleaguesRoute)}?${pageArguments(pageSize, page.cursor ?? null, page.search ?? null)}`,
            headers: headersFor(session),
            longestAnswer: longestColleaguePage,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const directory = parseColleaguePage(bodyOf(response), pageSize);

        return directory === null ? failed('unreadable', response.status) : read(directory);
    });
}

/** Reads one member of the reader's organization; one who left it, or was erased, is `missing`. */
export function readColleague(
    session: ClientSession,
    transport: MailFathomTransport,
    userId: string,
): Promise<ClientResult<Colleague>> {
    return spanned(`GET ${colleaguesRoute}/{userId}`, async () => {
        const response = await send(transport, {
            method: 'GET',
            path: routeFor(session, colleagueRoute(userId)),
            headers: headersFor(session),
            longestAnswer: longestColleagueAnswer,
        });

        if (response === null) {
            return failed('unavailable', null);
        }

        if (response.status === 404) {
            return failed('missing', response.status);
        }

        if (response.status !== 200) {
            return failed(failureReasonForStatus(response.status), response.status);
        }

        const colleague = parseColleague(bodyOf(response));

        return colleague === null ? failed('unreadable', response.status) : read(colleague);
    });
}

/** Writes a copy of a member — their name, addresses, and numbers — into the reader's own book. */
export function promoteColleague(
    session: ClientSession,
    transport: MailFathomTransport,
    userId: string,
): Promise<ClientResult<ContactWrite>> {
    return spanned(`POST ${colleaguesRoute}/{userId}/promotion`, async () =>
        writeOf(
            await send(transport, {
                method: 'POST',
                path: routeFor(session, colleaguePromotionRoute(userId)),
                headers: headersFor(session),
                longestAnswer: longestColleagueAnswer,
            }),
        ),
    );
}

/** Composes the request that reads a member's portrait, asking for the two kinds the surface serves. */
export function readColleaguePhotoRequest(session: ClientSession, userId: string): ClientRequest {
    return {
        method: 'GET',
        path: routeFor(session, colleaguePhotoRoute(userId)),
        headers: { ...headersFor(session), Accept: portraitImageTypes.join(', ') },
        longestAnswer: largestPortraitOctets,
    };
}

/**
 * Reads a member's portrait through an adapter, which is what carries the octets, and reports the request.
 *
 * @param failureOf Which failure the answer amounts to, or `null` where it is one the screen draws.
 */
export function readColleaguePhoto<TOutcome>(
    session: ClientSession,
    userId: string,
    deliver: (request: ClientRequest) => Promise<TOutcome>,
    failureOf: (outcome: TOutcome) => ClientFailureReason | null,
): Promise<TOutcome> {
    return reported(
        `GET ${colleaguesRoute}/{userId}/photo`,
        () => deliver(readColleaguePhotoRequest(session, userId)),
        failureOf,
    );
}

function parseColleaguePage(value: unknown, asked: number): ColleaguePage | null {
    const record = asRecord(value);
    const nextCursor = record?.['nextCursor'] ?? null;

    if (record === null || (nextCursor !== null && typeof nextCursor !== 'string')) {
        return null;
    }

    const colleagues = parseEach(record['contacts'], asked, parseColleague);

    return colleagues === null ? null : { colleagues, nextCursor };
}

/** Reads one member off a response body, or answers `null` where any field of it is missing or of the wrong shape. */
export function parseColleague(value: unknown): Colleague | null {
    const record = asRecord(value);
    if (record?.['book'] !== 'Member') {
        return null;
    }

    const id = record['id'];
    const displayName = record['displayName'];
    const organizationName = record['organizationName'];
    const preferredAddress = record['preferredAddress'] ?? null;
    const jobTitle = record['jobTitle'] ?? null;
    const timeZone = record['timeZone'] ?? null;
    const photo = record['photo'] ?? null;

    if (typeof id !== 'string' || !isText(displayName, longestName) || !isText(organizationName, longestName)) {
        return null;
    }

    if (!isOptionalText(jobTitle, longestJobTitle) || !isOptionalText(timeZone, longestTimeZone)) {
        return null;
    }

    if (!isOptionalText(photo, longestName) || !isOptionalText(preferredAddress, longestContactSearch)) {
        return null;
    }

    const addresses = parseEach(record['addresses'], mostAddressesPerColleague, (address) =>
        isText(address, longestContactSearch) ? address : null,
    );

    // The preferred address is one of the member's addresses, and it is absent exactly where they hold none: a member
    // with an address and no preference, or a preference naming an address they do not hold, is not one the service
    // described.
    if (addresses === null || (preferredAddress === null) !== (addresses.length === 0)) {
        return null;
    }

    if (preferredAddress !== null && !addresses.includes(preferredAddress)) {
        return null;
    }

    const phoneNumbers = parsePhoneNumbers(record['phoneNumbers']);
    const workingHours = record['workingHours'] ?? null;
    const hours = workingHours === null ? null : parseWorkingHours(workingHours);

    if (phoneNumbers === null || (workingHours !== null && hours === null)) {
        return null;
    }

    return {
        id,
        book: 'Member',
        displayName,
        addresses,
        preferredAddress,
        phoneNumbers,
        jobTitle,
        organizationName,
        timeZone,
        workingHours: hours,
        photo,
    };
}

function parseWorkingHours(value: unknown): ColleagueWorkingHours | null {
    const record = asRecord(value);
    const days = parseEach(
        record?.['days'],
        weekdays.length,
        (day) => weekdays.find((weekday) => weekday === day) ?? null,
    );
    const start = minutesOf(record?.['start']);
    const end = minutesOf(record?.['end']);

    if (days === null || days.length === 0 || start === null || end === null || end <= start) {
        return null;
    }

    return { days: weekdays.filter((weekday) => days.includes(weekday)), start, end };
}

// A time of day written `HH:mm`, as minutes after midnight.
function minutesOf(value: unknown): number | null {
    const time = typeof value === 'string' ? /^(\d{2}):(\d{2})$/.exec(value) : null;

    if (time === null) {
        return null;
    }

    const hours = Number(time[1]);
    const minutes = Number(time[2]);

    return hours < 24 && minutes < 60 ? hours * 60 + minutes : null;
}

function isText(value: unknown, longest: number): value is string {
    return typeof value === 'string' && value.length <= longest;
}

function isOptionalText(value: unknown, longest: number): value is string | null {
    return value === null || isText(value, longest);
}
