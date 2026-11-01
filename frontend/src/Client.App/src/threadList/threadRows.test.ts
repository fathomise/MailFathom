// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { ListedMailThread, MailAccount } from '@mailfathom/client-backend';
import { newestAuthor, participantsNamed, rowVariantOf, threadListAccountIn, titleStateOf } from './threadRows';

const anna = { address: 'anna@example.invalid', displayName: 'Anna Nowak', reader: false };
const piotr = { address: 'piotr@example.invalid', displayName: 'Piotr Lis', reader: false };
const me = { address: 'me@example.invalid', displayName: 'Me Myself', reader: true };

const title = {
    topic: 'Quarterly figures for the audit',
    standing: 'Anna is waiting for the signed copy.',
    derivedFromMessageCount: 3,
    derivedFromLatestArrival: '2026-08-31T09:41:00+00:00',
};

function thread(overrides: Partial<ListedMailThread> = {}): ListedMailThread {
    return {
        threadId: 'a-conversation',
        account: 'work',
        messageCount: 3,
        unreadCount: 0,
        hasAttachments: false,
        participants: [anna, me, piotr],
        moreParticipantsNotNamed: false,
        newest: {
            id: 'three',
            subject: 'The quarterly figures',
            receivedAt: '2026-08-31T09:41:00+00:00',
            senderAddress: piotr.address,
            senderDisplayName: piotr.displayName,
            preview: 'The figures are attached.',
            own: false,
            senderPhoto: null,
        },
        newestReceivedSender: null,
        title,
        ...overrides,
    };
}

function account(id: string): MailAccount {
    return {
        id,
        displayName: id,
        synchronizationState: 'Synchronized',
        lastSynchronizedAt: null,
        behind: false,
        filesJunk: false,
        shared: false,
    };
}

describe('threadListAccountIn', () => {
    it('reads the account picked where there are several to pick between', () => {
        expect(threadListAccountIn([account('work'), account('home')], 'home')).toBe('home');
    });

    it('reads every account where only one is held, there being nothing to pick between', () => {
        expect(threadListAccountIn([account('work')], 'work')).toBeNull();
    });

    it('reads every account where the one picked is no longer held', () => {
        expect(threadListAccountIn([account('work'), account('home')], 'gone')).toBeNull();
    });
});

describe('participantsNamed', () => {
    it('names everybody by first name in the order they wrote, and the reader as themselves', () => {
        expect(participantsNamed(thread(), 'me')).toStrictEqual(['Anna', 'me', 'Piotr']);
    });

    it('names the one person who wrote by their whole name', () => {
        expect(participantsNamed(thread({ participants: [anna] }), 'me')).toStrictEqual(['Anna Nowak']);
    });

    it('names somebody the deployment holds no name for by their address', () => {
        expect(participantsNamed(thread({ participants: [{ ...anna, displayName: null }, me] }), 'me')).toStrictEqual([
            'anna@example.invalid',
            'me',
        ]);
    });
});

describe('newestAuthor', () => {
    it('names who wrote the newest message by first name where several took part', () => {
        expect(newestAuthor(thread())).toBe('Piotr');
    });

    it('names them in full where they are the only one who wrote', () => {
        expect(newestAuthor(thread({ participants: [piotr] }))).toBe('Piotr Lis');
    });

    it('names nobody where the reader wrote it, which the row says as themselves', () => {
        expect(newestAuthor(thread({ newest: { ...thread().newest, own: true } }))).toBeNull();
    });
});

describe('titleStateOf', () => {
    it('reads a conversation no derivation has named as untitled, and draws its subject', () => {
        const titled = titleStateOf(thread({ title: null }));

        expect(titled).toBe('untitled');
        expect(rowVariantOf(titled)).toBe('subject');
    });

    it('reads a title whose standing is empty as the topic alone', () => {
        const titled = titleStateOf(thread({ title: { ...title, standing: '' } }));

        expect(titled).toBe('topic');
        expect(rowVariantOf(titled)).toBe('topic');
    });

    it('reads a current title as the topic with where the conversation stands under it', () => {
        const titled = titleStateOf(thread());

        expect(titled).toBe('standing');
        expect(rowVariantOf(titled)).toBe('standing');
    });

    it('holds the standing back once a message joined since it was written', () => {
        const titled = titleStateOf(thread({ messageCount: 4 }));

        expect(titled).toBe('stale');
        expect(rowVariantOf(titled)).toBe('standing');
    });

    it('holds the standing back once a newer message arrived, even where the count came out the same', () => {
        expect(titleStateOf(thread({ newest: { ...thread().newest, receivedAt: '2026-08-31T10:00:00+00:00' } }))).toBe(
            'stale',
        );
    });

    it('lets the count decide alone where either instant cannot be read', () => {
        expect(titleStateOf(thread({ title: { ...title, derivedFromLatestArrival: null } }))).toBe('standing');
    });
});
