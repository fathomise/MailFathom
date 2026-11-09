// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { asRecord } from './json';
import { parseMailCategories, type MailCategory } from './mailCategories';
import { parsePersonPhoto, type PersonPhotoReference } from './personPhotos';
import { parseListedSender, type MailListedSender } from './senderFacts';

// One conversation as a row, which is what the mail list answers with when it is grouped by conversation rather than
// read by message — `mailTimeline.ts` reads the page. A conversation gathers its messages from every folder the user
// may read, so the row names no folder at all.
//
// A row is not a mail list row with a count added. It quotes the conversation's newest message, names who wrote in it,
// and carries what a derivation called the whole of it — so it has a shape and a parser of its own, built from the same
// pieces a mail list row is built from where the two genuinely carry the same fact: the sender facts and the photo.

/** Somebody who has written in a conversation, and whether that is the user. */
export interface ListedThreadParticipant {
    readonly address: string;
    readonly displayName: string | null;

    /** Whether this is the user, under any of the sending identities of the accounts they are assigned. */
    readonly reader: boolean;
}

/** The conversation's newest message, quoted the way a list row quotes one. */
export interface ListedThreadNewest {
    readonly id: string;
    readonly subject: string | null;
    readonly receivedAt: string | null;
    readonly senderAddress: string | null;
    readonly senderDisplayName: string | null;

    /** The opening of the message's own text, or `null` for a message this deployment has stored but not extracted. */
    readonly preview: string | null;

    /** Whether the user wrote it. */
    readonly own: boolean;

    /** The photo its author is drawn by, or `null` where they are drawn by their initials. */
    readonly senderPhoto: PersonPhotoReference | null;

    /** Set where the mail server no longer holds the message and MailFathom alone keeps it; absent on every other. */
    readonly localOnly?: boolean;
}

/**
 * What a derivation called the whole conversation.
 *
 * Its two counts are the conversation it was derived from rather than the one that stands now, which is what tells a
 * screen the standing was written before a reply that has since arrived.
 */
export interface ListedThreadTitle {
    /** What the conversation is about, set early and kept as replies arrive. */
    readonly topic: string;

    /** Where it stands now, written again with each message, and empty where nothing is left open. */
    readonly standing: string;

    readonly derivedFromMessageCount: number;
    readonly derivedFromLatestArrival: string | null;
}

/** One conversation, as the list draws it. */
export interface ListedMailThread {
    readonly threadId: string;

    /** The account that holds its newest message, as the accounts route names it. */
    readonly account: string;

    /** Every message the user may read in it, the spam and the trashed ones included. */
    readonly messageCount: number;

    /** The received messages in it nobody has read; a message the user sent is never one. */
    readonly unreadCount: number;

    /** Whether any message in it, however far back, carries an attachment. */
    readonly hasAttachments: boolean;

    /** Who wrote in it, in the order they first wrote, at most eight of them. */
    readonly participants: readonly ListedThreadParticipant[];

    /** Whether it has authors {@link participants} does not name. */
    readonly moreParticipantsNotNamed: boolean;

    readonly newest: ListedThreadNewest;

    /**
     * The facts the sender mark of the newest message somebody else wrote is chosen from, or `null` where the user
     * wrote every message in it.
     */
    readonly newestReceivedSender: MailListedSender | null;

    /** What a derivation called the conversation, or `null` where none has. */
    readonly title: ListedThreadTitle | null;

    /** Every category any of its messages holds, each once, empty where it shows none. */
    readonly categories: readonly MailCategory[];
}

const longestIdentity = 256;
const longestText = 4_096;

// The deployment names at most eight authors per row. This is above that and bounds the walk rather than trusting the
// answer to stop.
const mostParticipants = 16;

/**
 * Reads one conversation row, or `null` for a row with anything wrong in it.
 *
 * Exported inside this package rather than published, because the row is what the mail list grouped by conversation
 * answers with, and the bounds a row is held to belong beside its shape rather than to the route that asked for it.
 *
 * @param value The row as the deployment sent it.
 * @returns The row, or `null` where it is not one.
 */
export function parseListedThread(value: unknown): ListedMailThread | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const threadId = record['threadId'];
    const account = record['account'];
    const messageCount = record['messageCount'];
    const unreadCount = record['unreadCount'];
    const hasAttachments = record['hasAttachments'];
    const moreParticipantsNotNamed = record['moreParticipantsNotNamed'];

    if (!isIdentity(threadId) || !isIdentity(account) || !isCount(messageCount) || !isCount(unreadCount)) {
        return null;
    }

    if (typeof hasAttachments !== 'boolean' || typeof moreParticipantsNotNamed !== 'boolean') {
        return null;
    }

    const participants = parseParticipants(record['participants']);
    const newest = parseNewest(record['newest']);
    const sent = record['newestReceivedSender'] ?? null;
    const newestReceivedSender = sent === null ? null : parseListedSender(sent);
    const titled = record['title'] ?? null;
    const title = titled === null ? null : parseTitle(titled);
    const categories = parseMailCategories(record['categories']);

    if (participants === null || newest === null || categories === null) {
        return null;
    }

    if ((sent !== null && newestReceivedSender === null) || (titled !== null && title === null)) {
        return null;
    }

    return {
        threadId,
        account,
        messageCount,
        unreadCount,
        hasAttachments,
        participants,
        moreParticipantsNotNamed,
        newest,
        newestReceivedSender,
        title,
        categories,
    };
}

function parseParticipants(value: unknown): readonly ListedThreadParticipant[] | null {
    if (!Array.isArray(value) || value.length > mostParticipants) {
        return null;
    }

    const participants: ListedThreadParticipant[] = [];
    for (const entry of value) {
        const record = asRecord(entry);
        if (record === null) {
            return null;
        }

        const address = record['address'];
        const displayName = record['displayName'] ?? null;
        const reader = record['reader'];

        if (!isText(address) || !isOptionalText(displayName) || typeof reader !== 'boolean') {
            return null;
        }

        participants.push({ address, displayName, reader });
    }

    return participants;
}

function parseNewest(value: unknown): ListedThreadNewest | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const id = record['id'];
    const subject = record['subject'] ?? null;
    const receivedAt = record['receivedAt'] ?? null;
    const senderAddress = record['senderAddress'] ?? null;
    const senderDisplayName = record['senderDisplayName'] ?? null;
    const preview = record['preview'] ?? null;
    const own = record['own'];
    const drawnBy = parsePersonPhoto(record['senderPhoto']);

    if (!isIdentity(id) || typeof own !== 'boolean' || drawnBy === null) {
        return null;
    }

    if (
        !isOptionalText(subject) ||
        !isOptionalText(receivedAt) ||
        !isOptionalText(senderAddress) ||
        !isOptionalText(senderDisplayName) ||
        !isOptionalText(preview)
    ) {
        return null;
    }

    return {
        id,
        subject,
        receivedAt,
        senderAddress,
        senderDisplayName,
        preview,
        own,
        senderPhoto: drawnBy.photo,
        ...(record['localOnly'] === true && { localOnly: true }),
    };
}

function parseTitle(value: unknown): ListedThreadTitle | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const topic = record['topic'];
    const standing = record['standing'];
    const derivedFromMessageCount = record['derivedFromMessageCount'];
    const derivedFromLatestArrival = record['derivedFromLatestArrival'] ?? null;

    if (!isText(topic) || topic.length === 0 || typeof standing !== 'string' || standing.length > longestText) {
        return null;
    }

    if (!isCount(derivedFromMessageCount) || !isOptionalText(derivedFromLatestArrival)) {
        return null;
    }

    return { topic, standing, derivedFromMessageCount, derivedFromLatestArrival };
}

function isIdentity(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestIdentity;
}

function isText(value: unknown): value is string {
    return typeof value === 'string' && value.length <= longestText;
}

function isOptionalText(value: unknown): value is string | null {
    return value === null || isText(value);
}

function isCount(value: unknown): value is number {
    return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
}
