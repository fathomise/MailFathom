// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { ClientSession, MailFathomTransport, MailFolderRole } from '@mailfathom/client-backend';
import { listedSender } from '../../../../tests/fixtures/senders';
import type { ActedMessage } from '../mailboxActs/useMailboxActs';
import { messagesTaken, readActOver, readConversationMembers, type ConversationAct } from './conversationMessages';

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
        (member) => (member.folder in roles ? { known: true, role: roles[member.folder] ?? null } : { known: false }),
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

    it('takes no message kept in a folder this client has not read, which could be the reader’s own or the trash', () => {
        const unplaced = [message('elsewhere', { unread: true }), message('inbox')];

        for (const act of ['delete', 'archive', 'move', 'spam', 'markUnread'] as const) {
            expect(taken(act, unplaced)).not.toContain('elsewhere');
        }

        // Marking read asks nothing of where a message is kept.
        expect(taken('markRead', unplaced)).toStrictEqual(['elsewhere']);
    });
});

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

function listedMember(id: string): Readonly<Record<string, unknown>> {
    return {
        position: 0,
        answeredId: null,
        email: {
            id,
            account: 'work',
            folder: 'inbox',
            threadId: 'thread-1',
            subject: 'The figures',
            receivedAt: '2026-08-31T09:41:00+00:00',
            sentAt: '2026-08-31T09:40:00+00:00',
            senderAddress: 'anna@example.invalid',
            senderDisplayName: 'Anna Nowak',
            toAddresses: ['user@example.invalid'],
            unread: false,
            flagged: false,
            answered: false,
            hasAttachments: false,
            attachmentCount: 0,
            categories: [],
            sizeOctets: 1_024,
            preview: 'The figures are attached.',
            sender: listedSender('unrecognized'),
        },
    };
}

/** A deployment answering each read of a conversation with the next of those pages, the last one from then on. */
function deploymentPaging(
    pages: readonly (
        | { readonly ids: readonly string[]; readonly nextCursor: string | null; readonly notAssembled?: boolean }
        | 'failing'
    )[],
): { readonly transport: MailFathomTransport; readonly cursors: (string | null)[] } {
    const cursors: (string | null)[] = [];

    return {
        cursors,
        transport: (request) => {
            const page = pages[Math.min(cursors.length, pages.length - 1)] ?? 'failing';
            cursors.push(new URL(request.path).searchParams.get('cursor'));

            return Promise.resolve(
                page === 'failing'
                    ? { status: 503, headers: {}, body: '' }
                    : {
                          status: 200,
                          headers: {},
                          body: JSON.stringify({
                              threadId: 'thread-1',
                              messages: page.ids.map(listedMember),
                              participants: [],
                              messageCount: page.ids.length,
                              moreMessagesNotAssembled: page.notAssembled ?? false,
                              moreParticipantsNotNamed: false,
                              nextCursor: page.nextCursor,
                              pageSize: 100,
                          }),
                      },
            );
        },
    };
}

describe('readConversationMembers', () => {
    it('reads a conversation page after page from the cursor each answered with, and names the conversation of each message', async () => {
        const { transport, cursors } = deploymentPaging([
            { ids: ['one'], nextCursor: 'onwards' },
            { ids: ['two'], nextCursor: null },
        ]);

        const answered = await readConversationMembers(session, transport, ['thread-1']);

        expect(cursors).toStrictEqual([null, 'onwards']);
        expect(answered.outcome).toBe('read');
        expect(
            answered.outcome === 'read' ? answered.value.messages.map((member) => member.storedEmailId) : [],
        ).toStrictEqual(['one', 'two']);
        expect(answered.outcome === 'read' && answered.value.cut).toBe(false);
        expect(answered.outcome === 'read' ? answered.value.conversationOf.get('two') : undefined).toBe('thread-1');
    });

    it('says a conversation was cut where the service assembled only part of it', async () => {
        const { transport } = deploymentPaging([{ ids: ['one'], nextCursor: null, notAssembled: true }]);

        const answered = await readConversationMembers(session, transport, ['thread-1']);

        expect(answered.outcome === 'read' && answered.value.cut).toBe(true);
    });

    it('stops after eight pages of one conversation, and says it was cut', async () => {
        const { transport, cursors } = deploymentPaging([{ ids: ['one'], nextCursor: 'onwards' }]);

        const answered = await readConversationMembers(session, transport, ['thread-1']);

        expect(cursors).toHaveLength(8);
        expect(answered.outcome === 'read' && answered.value.cut).toBe(true);
    });

    it('answers with the failure of a later page rather than with the part read before it', async () => {
        const { transport } = deploymentPaging([{ ids: ['one'], nextCursor: 'onwards' }, 'failing']);

        const answered = await readConversationMembers(session, transport, ['thread-1']);

        expect(answered.outcome === 'failed' ? answered.failure.reason : null).toBe('unavailable');
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
