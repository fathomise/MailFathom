// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ListedMailThread, ListedThreadParticipant, MailAccount } from '@mailfathom/client-backend';

// What a row of the conversation list says, decided apart from how it is drawn: who took part, who wrote the newest
// message, and which of the title's states the row is in. Each is a reading of what the deployment answered rather than
// a value it sent, and each is what a test pins down without drawing a row.

/**
 * The account the conversation list is read under, or `null` for every account.
 *
 * A pick naming an account the list no longer holds — one taken away since it was made — reads as every account, which
 * is the list that pick reads anyway; and with one account there is nothing to pick between, so the list reads them all.
 */
export function threadListAccountIn(accounts: readonly MailAccount[], picked: string | null): string | null {
    return accounts.length > 1 && accounts.some((account) => account.id === picked) ? picked : null;
}

/** How a person is named on a row: by their name where the deployment holds one, and by their address where it does not. */
function nameOf(participant: ListedThreadParticipant): string {
    return participant.displayName ?? participant.address;
}

function firstNameOf(participant: ListedThreadParticipant): string {
    return participant.displayName?.split(/\s+/)[0] ?? participant.address;
}

/**
 * Who took part, as the row's first line names them: by first name, the user as `me`, and one whole name where only one
 * person wrote at all — a row naming a single *Anna* says less than one naming the person.
 *
 * @param me What the user is called on a row, in the reader's language.
 * @returns The names in the order they first wrote, for the caller to join as its language joins a list.
 */
export function participantsNamed(thread: ListedMailThread, me: string): readonly string[] {
    const { participants } = thread;

    if (participants.length === 1 && participants[0] !== undefined) {
        return [participants[0].reader ? me : nameOf(participants[0])];
    }

    return participants.map((participant) => (participant.reader ? me : firstNameOf(participant)));
}

/**
 * Who wrote the newest message, which the row's quotation of it opens with: `null` where the user did, which the row
 * says as *You*, the whole name where only one person wrote in the conversation, and the first name where the first line
 * names several.
 */
export function newestAuthor(thread: ListedMailThread): string | null {
    const { newest } = thread;

    if (newest.own) {
        return null;
    }

    const named = thread.participants.find((participant) => participant.address === newest.senderAddress);
    const author: ListedThreadParticipant = named ?? {
        address: newest.senderAddress ?? '',
        displayName: newest.senderDisplayName,
        reader: false,
    };

    return thread.participants.length > 1 ? firstNameOf(author) : nameOf(author);
}

/**
 * Which of its four states a row's title is in.
 *
 * - `untitled` — no derivation has named the conversation, and the row stands under its subject with no frame;
 * - `topic` — titled, with nothing left open, so no standing stands under the topic;
 * - `standing` — titled, and where it stands now is the line under the topic;
 * - `stale` — titled, but a message joined or left the conversation since the standing was written, so the topic stays
 *   and the standing is held back rather than shown as if it were current.
 */
export type ThreadTitleState = 'untitled' | 'topic' | 'standing' | 'stale';

export function titleStateOf(thread: ListedMailThread): ThreadTitleState {
    const { title } = thread;

    if (title === null) {
        return 'untitled';
    }

    if (title.derivedFromMessageCount !== thread.messageCount || arrivedSince(title.derivedFromLatestArrival, thread)) {
        return 'stale';
    }

    return title.standing.length === 0 ? 'topic' : 'standing';
}

/**
 * Which of the three heights a row is drawn at, which is what a drawn row is measured under: a subject with no frame, a
 * framed topic alone, or a framed topic with a line under it — the standing, or the line holding it back.
 */
export type ThreadRowVariant = 'subject' | 'topic' | 'standing';

export function rowVariantOf(titled: ThreadTitleState): ThreadRowVariant {
    return titled === 'untitled' ? 'subject' : titled === 'topic' ? 'topic' : 'standing';
}

// A count alone misses a message that left as another arrived, so the newest arrival is held against the one the title
// was derived from too. An instant either side cannot be read is no evidence of anything, and the count decides alone.
function arrivedSince(derivedFrom: string | null, thread: ListedMailThread): boolean {
    const derived = derivedFrom === null ? Number.NaN : Date.parse(derivedFrom);
    const newest = thread.newest.receivedAt === null ? Number.NaN : Date.parse(thread.newest.receivedAt);

    return !Number.isNaN(derived) && !Number.isNaN(newest) && newest > derived;
}
