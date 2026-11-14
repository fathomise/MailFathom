// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import {
    readMailThread,
    type ClientResult,
    type ClientSession,
    type ListedMailThread,
    type MailFathomTransport,
    type MailFolderRole,
} from '@mailfathom/client-backend';
import type { ActRefusal } from '../mailboxActs/mailboxDestinations';
import type { ActedMessage, MailboxAct, MailboxActs } from '../mailboxActs/useMailboxActs';
import { marksSender } from '../senderMark/senderMarks';

// Which of a conversation's messages an act asked of the whole conversation takes. A conversation gathers its messages
// from every folder, so *the conversation* is not one answer: deleting it is not destroying what is already in the
// trash, archiving it is not emptying the reader's own Sent folder, and marking it spam is not judging what the reader
// wrote. The design states each rule — `design/state-inventory.md`, *Acts on a thread* — and this is the one place the
// client holds them, read by the row's menu and by the bar over a selection alike.

/** What a conversation's row and a selection of conversations offer: the mailbox acts less the flag, and the spam mark. */
export type ConversationAct = Exclude<MailboxAct, 'flag' | 'unflag'> | 'spam';

/**
 * Which way the read control goes over those conversations: any unread message among them and the whole of each is
 * marked read, none and each is marked unread.
 */
export function readActOver(threads: readonly Pick<ListedMailThread, 'unreadCount'>[]): 'markRead' | 'markUnread' {
    return threads.some((thread) => thread.unreadCount > 0) ? 'markRead' : 'markUnread';
}

/**
 * Where a message is kept, as far as this client knows: the role its folder plays, which may be none, or that the
 * folder is not one this client has read — which is a different answer from a folder playing no role, since nothing
 * about such a message says whether the reader received it.
 */
export type FolderPlace = { readonly known: true; readonly role: MailFolderRole | null } | { readonly known: false };

/** Whether an act reads where each message is kept, which every one but marking read does. */
export function readsFolders(act: ConversationAct): boolean {
    return act !== 'markRead';
}

/**
 * The messages of the conversations read that one act takes.
 *
 * A message kept somewhere this client has not read is taken by no act that reads where a message is kept: it could
 * be the reader's own, or already in the trash.
 *
 * @param act What was asked of the conversations.
 * @param members Every message of them, wherever each is kept.
 * @param placeOf Where a message is kept, which is what tells received mail from the reader's own.
 * @param spam Whether a message is drawn as spam, the person's own verdict from this session included.
 */
export function messagesTaken(
    act: ConversationAct,
    members: readonly ActedMessage[],
    placeOf: (message: ActedMessage) => FolderPlace,
    spam: (message: ActedMessage) => boolean,
): readonly ActedMessage[] {
    if (act === 'markRead') {
        return members.filter((message) => message.unread);
    }

    return members.filter((message) => {
        const place = placeOf(message);

        if (!place.known) {
            return false;
        }

        const { role } = place;

        // Received mail outside the trash, which is what every act but deleting starts from.
        const received = marksSender(role) && role !== 'Trash';
        const kept = received && role !== 'Junk' && !spam(message);

        switch (act) {
            case 'delete':
                // Never what is already in the trash, so a conversation's row destroys nothing, and never a draft,
                // which is something the reader is still writing.
                return role !== 'Trash' && role !== 'Drafts';
            case 'markUnread':
                return kept && !message.unread;
            case 'archive':
                return kept && role !== 'Archive';
            case 'move':
                return kept;
            case 'spam':
                return received && !spam(message);
        }
    });
}

/**
 * Why an act cannot be asked of those conversations at all, read before any of their messages are: the credential, the
 * connection, and what each conversation's account has a folder for.
 *
 * Each conversation is stood for by a message of its account in no folder, because that is all a row knows of where
 * its messages are. What depends on the messages themselves is answered once they are read.
 */
export function offeredOver(
    acts: MailboxActs,
    act: ConversationAct,
    threads: readonly Pick<ListedMailThread, 'account' | 'newest'>[],
): ActRefusal | null {
    if (act === 'spam') {
        return acts.spamOffered ? null : 'notOffered';
    }

    const refusal = acts.refusalOf(
        act,
        threads.map((thread) => ({
            storedEmailId: thread.newest.id,
            account: thread.account,
            folder: '',
            unread: false,
            flagged: false,
            spam: null,
        })),
    );

    // Unread folders are not a refusal here: the act reads them again before it files anything.
    return refusal === 'foldersUnknown' ? null : refusal;
}

/** Every message of some conversations, and whether one of them runs past what was read. */
export interface ConversationMembers {
    readonly messages: readonly ActedMessage[];

    /** Whether a conversation holds more messages than the read reached, so an act over these reaches part of it. */
    readonly cut: boolean;

    /** The conversation each message was read from, by the message's stored identity. */
    readonly conversationOf: ReadonlyMap<string, string>;
}

// The most pages of one conversation read for an act. The service assembles five pages' worth of a conversation at
// most, so this is above what it answers and bounds the walk rather than trusting the cursor to end.
const mostPagesPerConversation = 8;

/**
 * Reads every message of those conversations, which is what an act on a conversation has to name: its row holds the
 * newest message alone.
 *
 * @returns The messages, or the first failure met — an act over part of what was picked out would be a different act.
 */
export async function readConversationMembers(
    session: ClientSession,
    transport: MailFathomTransport,
    threadIds: readonly string[],
): Promise<ClientResult<ConversationMembers>> {
    const messages: ActedMessage[] = [];
    const conversationOf = new Map<string, string>();
    let cut = false;

    for (const threadId of threadIds) {
        let cursor: string | null = null;

        for (let page = 0; page < mostPagesPerConversation; page += 1) {
            const answered = await readMailThread(session, transport, threadId, cursor);

            if (answered.outcome === 'failed') {
                return answered;
            }

            for (const { email } of answered.value.messages) {
                conversationOf.set(email.id, threadId);
                messages.push({
                    storedEmailId: email.id,
                    account: email.account,
                    folder: email.folder,
                    unread: email.unread,
                    flagged: email.flagged,
                    spam: email.spam,
                });
            }

            cut ||= answered.value.moreMessagesNotAssembled;
            cursor = answered.value.nextCursor;

            if (cursor === null) {
                break;
            }
        }

        // A cursor still standing after the last page read is a conversation longer than this walk.
        cut ||= cursor !== null;
    }

    return { outcome: 'read', value: { messages, cut, conversationOf } };
}
