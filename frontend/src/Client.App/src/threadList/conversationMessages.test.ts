// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailFolderRole } from '@mailfathom/client-backend';
import type { ActedMessage } from '../mailboxActs/useMailboxActs';
import { messagesTaken, readActOver, type ConversationAct } from './conversationMessages';

// One conversation with a message in every place the rules tell apart, each named for the folder it is kept in.
const roles: Readonly<Record<string, MailFolderRole | null>> = {
    inbox: 'Inbox',
    project: null,
    archive: 'Archive',
    sent: 'Sent',
    drafts: 'Drafts',
    junk: 'Junk',
    trash: 'Trash',
};

function message(folder: string, overrides: Partial<ActedMessage> = {}): ActedMessage {
    return { storedEmailId: folder, account: 'work', folder, unread: false, flagged: false, spam: null, ...overrides };
}

const conversation: readonly ActedMessage[] = Object.keys(roles).map((folder) => message(folder));

function taken(act: ConversationAct, members: readonly ActedMessage[] = conversation): readonly string[] {
    return messagesTaken(
        act,
        members,
        (member) => roles[member.folder] ?? null,
        (member) => member.spam !== null,
    ).map((member) => member.storedEmailId);
}

describe('messagesTaken', () => {
    it('deletes everything but what is already in the trash and the drafts, spam included', () => {
        expect(taken('delete')).toStrictEqual(['inbox', 'project', 'archive', 'sent', 'junk']);
    });

    it('archives the received mail outside the trash and spam that is not archived already', () => {
        expect(taken('archive')).toStrictEqual(['inbox', 'project']);
    });

    it('moves the received mail outside the trash and spam, and leaves what the reader sent and is writing', () => {
        expect(taken('move')).toStrictEqual(['inbox', 'project', 'archive']);
    });

    it('marks as spam the received mail outside the trash, wherever else it is kept', () => {
        expect(taken('spam')).toStrictEqual(['inbox', 'project', 'archive', 'junk']);
    });

    it('leaves mail already judged spam out of a spam mark, a move and an archive', () => {
        const judged = [
            message('inbox', {
                spam: {
                    verdict: 'Spam',
                    decidedBy: 'Deterministic',
                    spamSubcategory: null,
                    spamSubcategoryNamedBy: null,
                    phishingRequest: null,
                    impersonatedParty: null,
                },
            }),
            message('project'),
        ];

        expect(taken('spam', judged)).toStrictEqual(['project']);
        expect(taken('move', judged)).toStrictEqual(['project']);
        expect(taken('archive', judged)).toStrictEqual(['project']);
    });

    it('marks read every unread message, whichever folder holds it', () => {
        const unread = conversation.map((member) => ({ ...member, unread: true }));

        expect(taken('markRead', unread)).toStrictEqual(Object.keys(roles));
        expect(taken('markRead')).toStrictEqual([]);
    });

    it('marks unread the received mail outside the trash and spam alone', () => {
        expect(taken('markUnread')).toStrictEqual(['inbox', 'project', 'archive']);
    });
});

describe('readActOver', () => {
    it('marks the conversations read while any of them holds an unread message', () => {
        expect(readActOver([{ unreadCount: 0 }, { unreadCount: 2 }])).toBe('markRead');
    });

    it('marks them unread once none of them does', () => {
        expect(readActOver([{ unreadCount: 0 }, { unreadCount: 0 }])).toBe('markUnread');
    });
});
