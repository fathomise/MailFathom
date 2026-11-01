// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { conversationId } from './mail';

// The signed-in person's address book, as the two books the client surface publishes it as: the people they wrote down
// themselves and the people their own mailboxes picked up from mail that arrived.
//
// `frontend/tests/AGENTS.md` § *The corpus* holds what the whole of it is and what may go in it. What this file adds
// beside the resting book is the states the People screen has to be looked at in and cannot be reached by scrolling: a
// book with nobody in it, a record collected rather than asserted, a write the book refused, and a person the mailbox
// has nothing to say about.
//
// Nobody here exists. Every name is invented and every host is a reserved one, exactly as it is for the mail.

/** How many people one page of a book holds, which is what a cursor moves by. */
export const contactsPerPage = 50;

function person(at: number, origin: string) {
    return {
        id: `contact-${String(at)}`,
        displayName: `Correspondent ${String(at)}`,
        addresses: [`correspondent-${String(at)}@nordwind.example`],
        preferredAddress: `correspondent-${String(at)}@nordwind.example`,
        note: null,
        phoneNumbers: [],
        origin,
        book: origin === 'Collected' ? 'Collected' : 'Own',
        recordedAt: '2026-08-31T09:41:00+00:00',
        amendedAt: '2026-08-31T09:41:00+00:00',
        photo: null,
        organization: null,
    };
}

/**
 * One person this user wrote down, named rather than numbered.
 *
 * The book's first row is the one every check reaching a contact by position lands on, so it is the record a screen is
 * most often looked at with — a person with two addresses, two phone numbers, and something written about them, which
 * is what the design draws a contact's own page with.
 */
export const assertedContact = {
    id: 'contact-anna',
    displayName: 'Anna Marlow',
    addresses: ['anna@nordwind.example', 'a.marlow@nordwind.example'],
    preferredAddress: 'anna@nordwind.example',
    note: 'Runs the warehouse in Gdańsk.',
    phoneNumbers: [
        { number: '+1 202 555 0143', label: 'Mobile' },
        { number: '+1 202 555 0178', label: 'Work' },
    ],
    origin: 'Asserted',
    book: 'Own',
    recordedAt: '2026-06-02T11:15:00+00:00',
    amendedAt: '2026-08-14T08:02:00+00:00',
    photo: null,
    organization: null,
};

/**
 * One person a mailbox picked up, which is the record nobody may amend.
 *
 * It is the second state a contact is drawn in and cannot be reached from the asserted one by any press, so the book
 * that holds it is a page of its own rather than an extra row in the resting one.
 */
export const collectedContact = {
    id: 'contact-bartosz',
    displayName: 'Bartosz Rowe',
    addresses: ['b.rowe@contoso.example'],
    preferredAddress: 'b.rowe@contoso.example',
    note: null,
    phoneNumbers: [],
    origin: 'Collected',
    book: 'Collected',
    recordedAt: '2026-08-29T16:40:00+00:00',
    amendedAt: '2026-08-29T16:40:00+00:00',
    photo: null,
    organization: null,
};

/**
 * One person the reader's organization keeps in its own book, which every member reads and none may change.
 *
 * It is listed among the reader's own, so it sits in the resting book rather than a page of its own, named to sort
 * straight after the first row so the mark that tells it apart is on the screen the book opens on.
 */
export const organizationContact = {
    id: 'contact-atlas',
    displayName: 'Atlas Travel desk',
    addresses: ['desk@atlastravel.example'],
    preferredAddress: 'desk@atlastravel.example',
    note: null,
    phoneNumbers: [],
    origin: 'Asserted',
    book: 'Organization',
    recordedAt: '2026-05-12T07:30:00+00:00',
    amendedAt: '2026-05-12T07:30:00+00:00',
    photo: null,
    organization: '0197f0c4-3b1a-7c2e-9d4f-5a6b7c8d9e0f',
};

/** The book of people this user wrote down, long enough that the list windows rather than drawing every row. */
export const assertedContactPage = {
    contacts: [
        assertedContact,
        organizationContact,
        ...Array.from({ length: contactsPerPage - 2 }, (_, at) => person(at + 1, 'Asserted')),
    ],
    nextCursor: null,
};

/** The book of people the mailboxes picked up, which is the larger of the two against any real mailbox. */
export const collectedContactPage = {
    contacts: [
        collectedContact,
        ...Array.from({ length: contactsPerPage - 1 }, (_, at) => person(at + 100, 'Collected')),
    ],
    nextCursor: null,
};

/** The name the session gives the signed-in person's organization, which is the one its members' records carry. */
const organizationName = 'Nordwind';

function colleague(
    id: string,
    displayName: string,
    jobTitle: string | null,
    addresses: readonly string[],
    timeZone: string | null,
    workingHours: { readonly days: readonly string[]; readonly start: string; readonly end: string },
    phoneNumbers: readonly { readonly number: string; readonly label: string }[],
    presence: string,
    lastSeen: string | null = null,
    outOfOfficeUntil: string | null = null,
    usesPhoneApp = false,
) {
    return {
        id,
        book: 'Member',
        displayName,
        addresses,
        preferredAddress: addresses[0] ?? null,
        phoneNumbers,
        jobTitle,
        organizationName,
        timeZone,
        workingHours,
        photo: null,
        presence,
        lastSeen,
        outOfOfficeUntil,
        usesPhoneApp,
    };
}

const mondayToFriday = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'];

function hoursOn(days: readonly string[], start: string, end: string) {
    return { days, start, end };
}

/**
 * The members of that organization, as the directory derives them from their user records and orders them by name.
 *
 * They are the design's own sample members, so a parity capture of the directory draws the people the artboard does:
 * one who lives six hours behind, one in London, one who states no zone, and the rest beside the reader — each in the
 * presence the artboard draws them in. Igor Pawlak chose to hide, so he reads as offline with no time he was last seen,
 * and the two who are simply offline were last seen the evening before the corpus's own day. Piotr Zieliński's
 * automatic reply runs to the end of the day the artboard draws him out of office until. Marta Nowak, Oliver Grant,
 * and Piotr Zieliński use the phone app, as the artboard marks them — Piotr while offline, since the marker is not his
 * presence.
 */
export const colleaguePage = {
    contacts: [
        colleague(
            'member-adam',
            'Adam Lewandowski',
            'Finance Director',
            ['a.lewandowski@nordwind.example'],
            'Europe/Warsaw',
            hoursOn(mondayToFriday, '08:00', '16:00'),
            [{ number: '+48 22 555 01 10', label: 'Work' }],
            'Away',
        ),
        colleague(
            'member-hanna',
            'Hanna Mazur',
            'QA Lead',
            ['h.mazur@nordwind.example'],
            null,
            hoursOn(['Monday', 'Tuesday', 'Wednesday', 'Thursday'], '07:00', '15:00'),
            [],
            'DoNotDisturb',
        ),
        colleague(
            'member-igor',
            'Igor Pawlak',
            'Systems Engineer',
            ['i.pawlak@nordwind.example'],
            'Europe/Warsaw',
            hoursOn(mondayToFriday, '08:00', '16:00'),
            [],
            'Offline',
        ),
        colleague(
            'member-marta',
            'Marta Nowak',
            'Controller',
            ['m.nowak@nordwind.example', 'm.nowak@company.example'],
            'Europe/Warsaw',
            hoursOn(mondayToFriday, '08:00', '16:00'),
            [
                { number: '+48 22 555 01 14', label: 'Work' },
                { number: '+48 601 555 014', label: 'Mobile' },
            ],
            'Busy',
            null,
            null,
            true,
        ),
        colleague(
            'member-oliver',
            'Oliver Grant',
            'Delivery Engineer',
            ['o.grant@nordwind.example'],
            'Europe/London',
            hoursOn(mondayToFriday, '09:00', '17:30'),
            [{ number: '+44 20 7946 0321', label: 'Work' }],
            'Active',
            null,
            null,
            true,
        ),
        colleague(
            'member-piotr',
            'Piotr Zieliński',
            'Head of Delivery',
            ['p.zielinski@nordwind.example', 'p.zielinski@company.example'],
            'Europe/Warsaw',
            hoursOn(mondayToFriday, '09:00', '17:00'),
            [{ number: '+48 603 555 207', label: 'Mobile' }],
            'Offline',
            '2026-10-30T18:20:00+01:00',
            '2027-09-05T00:00:00+02:00',
            true,
        ),
        colleague(
            'member-zofia',
            'Zofia Krawczyk',
            'Managing Director',
            ['z.krawczyk@nordwind.example'],
            'America/New_York',
            hoursOn(mondayToFriday, '09:00', '17:00'),
            [{ number: '+1 212 555 0148', label: 'Work' }],
            'Offline',
            '2026-10-30T23:10:00+01:00',
        ),
    ],
    nextCursor: null,
};

/** Whom each of those members reports to, as an administrator named it — the design's own sample structure. */
const reportsTo = new Map([
    ['member-adam', 'member-zofia'],
    ['member-marta', 'member-adam'],
    ['member-piotr', 'member-zofia'],
    ['member-oliver', 'member-piotr'],
    ['member-hanna', 'member-piotr'],
    ['member-igor', 'member-piotr'],
]);

/**
 * Where one member sits in the organization: the managers above them from the top down, and their direct reports by
 * name, as the directory derives both from the one manager each member names. The reader is somebody none of the
 * members reports to, so the structure names them without any row being theirs.
 */
export function colleagueStructureOf(userId: string) {
    const members = colleaguePage.contacts;
    const above = [];

    for (let manager = reportsTo.get(userId); manager !== undefined; manager = reportsTo.get(manager)) {
        const found = members.find(({ id }) => id === manager);

        if (found !== undefined) {
            above.unshift(found);
        }
    }

    return {
        managers: above,
        directReports: members.filter(({ id }) => reportsTo.get(id) === userId),
        readerId: 'member-reader',
    };
}

/** A book with nobody in it, which is a state neither of the two above can be scrolled into. */
export const emptyContactPage = { contacts: [], nextCursor: null };

/**
 * Who of the people an AI answer names is drawn by a photo, which is nobody: none of the corpus's people has one, so
 * every person an answer names is drawn by their initials.
 */
export const noPersonPhotos = { photos: [] };

/** What writing somebody down answers with, which is the record the caller itself stated. */
export const contactWritten = { outcome: 'Written', contact: assertedContact, addressHolder: null };

/**
 * What a write claiming an address somebody else already holds answers with.
 *
 * The refusal names who holds it and nothing else about them — a client that could read a name out of a refusal would
 * be reading a record its caller was not given.
 */
export const contactAddressHeld = {
    outcome: 'AddressHeldByAnotherContact',
    contact: null,
    addressHolder: assertedContact.id,
};

/** What taking a collected record on answers with, which is that person as one this user now asserts. */
export const contactPromoted = {
    outcome: 'Written',
    contact: { ...collectedContact, origin: 'Asserted', book: 'Own' },
    addressHolder: null,
};

/** What erasing one person answers with, which carries no name and no address by design. */
export const contactErased = { contact: assertedContact.id, wasHeld: true, addressesErased: 1 };

/**
 * What the mailbox has to say about one person, which is computed over the mail index rather than stored.
 *
 * The conversation is the one the mail corpus already holds, so opening a row from a person's page lands on a
 * correspondence rather than on a message the deployment would not answer for.
 */
export const contactCorrespondence = {
    contactId: assertedContact.id,
    threads: [
        {
            threadId: conversationId,
            latestMessageId: 'message-3',
            subject: 'Racking quote — 12 bays',
            lastCorrespondedAt: '2026-08-31T09:41:00+00:00',
        },
        {
            threadId: 'thread-annex',
            latestMessageId: 'message-11',
            subject: null,
            lastCorrespondedAt: '2026-07-18T13:04:00+00:00',
        },
    ],
    documents: [
        {
            messageId: 'message-3',
            position: 1,
            fileName: 'racking-quote.pdf',
            mediaType: 'application/pdf',
            receivedAt: '2026-08-31T09:41:00+00:00',
        },
        {
            messageId: 'message-11',
            position: 2,
            fileName: null,
            mediaType: 'image/png',
            receivedAt: '2026-07-18T13:04:00+00:00',
        },
    ],
};

/** A person the mail index found nothing about, which is the empty state both columns of their page have. */
export const noContactCorrespondence = { contactId: collectedContact.id, threads: [], documents: [] };
