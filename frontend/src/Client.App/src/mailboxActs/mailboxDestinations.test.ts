// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailFolder, MailFolderDirectory, MailFolderRole } from '@mailfathom/client-backend';
import {
    deletesPermanently,
    destinationsFor,
    dropDestination,
    filesIntoTrash,
    filingFor,
    folderWithRole,
    refusalFor,
} from './mailboxDestinations';
import type { ActedMessage } from './useMailboxActs';

const everythingOffered = { flags: true, moves: true, deletes: true };

function folder(alias: string, role: MailFolderRole | null, path: readonly string[]): MailFolder {
    return {
        alias,
        role,
        path,
        storedEmailCount: 0,
        unreadEmailCount: 0,
        synchronizationState: 'Synchronized',
        lastSynchronizedAt: null,
        behind: false,
    };
}

function directoryOf(accounts: Record<string, readonly MailFolder[]>): MailFolderDirectory {
    return {
        synchronizationEnabled: true,
        accounts: Object.entries(accounts).map(([id, folders]) => ({
            account: {
                id,
                displayName: id,
                synchronizationState: 'Synchronized',
                lastSynchronizedAt: null,
                behind: false,
            },
            folders,
        })),
    };
}

const wholeMailbox = directoryOf({
    work: [
        folder('work-inbox', 'Inbox', ['INBOX']),
        folder('work-archive', 'Archive', ['Archive']),
        folder('work-trash', 'Trash', ['Trash']),
        folder('work-clients', null, ['Projects', 'Clients']),
    ],
    home: [folder('home-inbox', 'Inbox', ['INBOX'])],
});

function inWork(storedEmailId: string): ActedMessage {
    return { storedEmailId, account: 'work', folder: 'work-inbox', unread: false, flagged: false };
}

const atHome: ActedMessage = {
    storedEmailId: 'message-9',
    account: 'home',
    folder: 'home-inbox',
    unread: false,
    flagged: false,
};

function inWorkTrash(storedEmailId: string): ActedMessage {
    return { storedEmailId, account: 'work', folder: 'work-trash', unread: false, flagged: false };
}

describe('folderWithRole', () => {
    it('names the folder an account labels with the role, which is the only thing that says what archiving means', () => {
        expect(folderWithRole(wholeMailbox, 'work', 'Archive')).toBe('work-archive');
    });

    it('names none where the configuration labels none, rather than guessing one from what a folder is called', () => {
        expect(folderWithRole(wholeMailbox, 'home', 'Archive')).toBeNull();
    });

    it('names none for an account the folders never described, and for folders that were never read', () => {
        expect(folderWithRole(wholeMailbox, 'nobody', 'Trash')).toBeNull();
        expect(folderWithRole(null, 'work', 'Trash')).toBeNull();
    });
});

describe('destinationsFor', () => {
    it('offers the one account the messages are in, its folders named by their role and their place on the server', () => {
        expect(destinationsFor(wholeMailbox, [inWork('message-1')])).toStrictEqual([
            {
                accountId: 'work',
                accountName: 'work',
                ordinal: 1,
                destinations: [
                    { alias: 'work-inbox', name: 'INBOX', role: 'Inbox' },
                    { alias: 'work-archive', name: 'Archive', role: 'Archive' },
                    { alias: 'work-trash', name: 'Trash', role: 'Trash' },
                    { alias: 'work-clients', name: 'Projects / Clients', role: null },
                ],
            },
        ]);
    });

    it('counts the account the way the folder column counts its groups, so a mailbox keeps its colour', () => {
        const oneAccount = directoryOf({ work: [folder('work-inbox', 'Inbox', ['INBOX'])] });

        expect(destinationsFor(oneAccount, [inWork('message-1')])[0]?.ordinal).toBe(0);
    });

    it('offers nothing across two accounts, a folder belonging to the account it is in', () => {
        expect(destinationsFor(wholeMailbox, [inWork('message-1'), atHome])).toStrictEqual([]);
    });
});

// The drafts folder and the sent folder beside the rest, each with a subfolder of its own, which is what makes a message
// a draft or a sent copy: nothing but the tree of the folder it sits in says so. *Drafts2* sits beside the drafts
// folder rather than beneath it, which is a name rather than a place.
const withDraftsAndSent = directoryOf({
    work: [
        folder('work-inbox', 'Inbox', ['INBOX']),
        folder('work-drafts', 'Drafts', ['Drafts']),
        folder('work-drafts-clients', null, ['Drafts', 'Clients']),
        folder('work-drafts2', null, ['Drafts2']),
        folder('work-sent', 'Sent', ['Sent']),
        folder('work-sent-2026', null, ['Sent', '2026']),
        folder('work-archive', 'Archive', ['Archive']),
        folder('work-trash', 'Trash', ['Trash']),
        folder('work-clients', null, ['Projects', 'Clients']),
    ],
});

function inWorkDrafts(storedEmailId: string): ActedMessage {
    return { storedEmailId, account: 'work', folder: 'work-drafts', unread: false, flagged: false };
}

function inWorkSent(storedEmailId: string): ActedMessage {
    return { storedEmailId, account: 'work', folder: 'work-sent', unread: false, flagged: false };
}

function offeredAliases(messages: readonly ActedMessage[]): readonly string[] {
    return destinationsFor(withDraftsAndSent, messages).flatMap((group) =>
        group.destinations.map(({ alias }) => alias),
    );
}

describe('destinationsFor, around the drafts and the sent tree', () => {
    it('offers ordinary mail every folder outside the drafts and the sent tree', () => {
        expect(offeredAliases([inWork('message-1')])).toStrictEqual([
            'work-inbox',
            'work-archive',
            'work-trash',
            'work-drafts2',
            'work-clients',
        ]);
    });

    it('offers a draft its own tree and the trash, wherever in the tree it sits', () => {
        const inDraftsClients: ActedMessage = { ...inWorkDrafts('message-2'), folder: 'work-drafts-clients' };

        expect(offeredAliases([inWorkDrafts('message-1')])).toStrictEqual([
            'work-drafts',
            'work-trash',
            'work-drafts-clients',
        ]);
        expect(offeredAliases([inDraftsClients])).toStrictEqual(offeredAliases([inWorkDrafts('message-1')]));
    });

    it('offers a sent message its own tree and the trash', () => {
        expect(offeredAliases([inWorkSent('message-1')])).toStrictEqual(['work-sent', 'work-trash', 'work-sent-2026']);
    });

    it('offers a selection reaching into two trees the trash and nothing else', () => {
        expect(offeredAliases([inWorkDrafts('message-1'), inWork('message-2')])).toStrictEqual(['work-trash']);
        expect(offeredAliases([inWorkDrafts('message-1'), inWorkSent('message-2')])).toStrictEqual(['work-trash']);
    });

    it('files a drop inside a tree, and nothing dropped across the edge of one', () => {
        const ordinary = [inWork('message-1')];
        const draft = [inWorkDrafts('message-2')];
        const sent = [inWorkSent('message-3')];
        const dropping = (messages: readonly ActedMessage[], alias: string) =>
            dropDestination(destinationsFor(withDraftsAndSent, messages), messages, 'work', alias);

        expect(dropping(ordinary, 'work-drafts')).toBeNull();
        expect(dropping(ordinary, 'work-sent-2026')).toBeNull();
        expect(dropping(draft, 'work-clients')).toBeNull();
        expect(dropping(draft, 'work-drafts2')).toBeNull();
        expect(dropping(sent, 'work-archive')).toBeNull();
        expect(dropping(draft, 'work-drafts-clients')?.alias).toBe('work-drafts-clients');
        expect(dropping(sent, 'work-sent-2026')?.alias).toBe('work-sent-2026');
        expect(dropping(sent, 'work-trash')?.alias).toBe('work-trash');
    });
});

describe('filesIntoTrash', () => {
    it('reads the trash as the one destination whose filing is deleting', () => {
        expect(filesIntoTrash({ alias: 'work-trash', name: 'Trash', role: 'Trash' })).toBe(true);
        expect(filesIntoTrash({ alias: 'work-archive', name: 'Archive', role: 'Archive' })).toBe(false);
        expect(filesIntoTrash({ alias: 'work-clients', name: 'Projects / Clients', role: null })).toBe(false);
    });
});

describe('dropDestination', () => {
    const carried = [inWork('message-1'), inWork('message-2')];
    const offered = destinationsFor(wholeMailbox, carried);

    it('files a drop on a folder of the account the messages are in into that folder', () => {
        expect(dropDestination(offered, carried, 'work', 'work-clients')).toStrictEqual({
            alias: 'work-clients',
            name: 'Projects / Clients',
            role: null,
        });
    });

    it('files nothing dropped on a folder of another account, which a message cannot move into', () => {
        expect(dropDestination(offered, carried, 'home', 'home-inbox')).toBeNull();
    });

    it('files nothing dropped on the folder every one of the messages is already in', () => {
        expect(dropDestination(offered, carried, 'work', 'work-inbox')).toBeNull();
    });

    it('files a drop on the folder only some of them are in, since it takes the rest somewhere they are not', () => {
        const spread = [inWork('message-1'), inWorkTrash('message-2')];

        expect(dropDestination(destinationsFor(wholeMailbox, spread), spread, 'work', 'work-trash')?.alias).toBe(
            'work-trash',
        );
    });

    it('files nothing where nothing is being dropped', () => {
        expect(dropDestination(offered, [], 'work', 'work-clients')).toBeNull();
    });
});

describe('refusalFor', () => {
    it('refuses an act about nothing before it asks what the credential may do', () => {
        expect(refusalFor('flag', [], null, { flags: false, moves: false, deletes: false })).toBe('nothingToActOn');
    });

    it.each([
        ['flag', { flags: false, moves: true, deletes: true }],
        ['unflag', { flags: false, moves: true, deletes: true }],
        ['markUnread', { flags: false, moves: true, deletes: true }],
        ['archive', { flags: true, moves: false, deletes: true }],
        ['delete', { flags: true, moves: false, deletes: true }],
        ['move', { flags: true, moves: false, deletes: true }],
    ] as const)(
        'says a credential without the grant may not %s, rather than letting the act be refused',
        (act, offered) => {
            expect(refusalFor(act, [inWork('message-1')], wholeMailbox, offered)).toBe('notOffered');
        },
    );

    it('permits the three acts that change a flag without asking anything of the folders', () => {
        expect(refusalFor('flag', [inWork('message-1')], null, everythingOffered)).toBeNull();
        expect(refusalFor('unflag', [inWork('message-1')], null, everythingOffered)).toBeNull();
        expect(refusalFor('markUnread', [inWork('message-1')], null, everythingOffered)).toBeNull();
    });

    it.each([
        ['archive', 'noArchiveFolder'],
        ['delete', 'noTrashFolder'],
    ] as const)('says an account labelling no folder for %s has nowhere to put it', (act, refusal) => {
        expect(refusalFor(act, [atHome], wholeMailbox, everythingOffered)).toBe(refusal);
        expect(refusalFor(act, [inWork('message-1')], wholeMailbox, everythingOffered)).toBeNull();
    });

    it.each([
        ['a draft', inWorkDrafts('message-1')],
        ['a sent message', inWorkSent('message-1')],
    ])('refuses to archive %s, and still lets it be moved within its tree or deleted', (_, message) => {
        const selection = [inWork('message-2'), message];

        expect(refusalFor('archive', selection, withDraftsAndSent, everythingOffered)).toBe('draftsAndSentStay');
        expect(refusalFor('move', [message], withDraftsAndSent, everythingOffered)).toBeNull();
        expect(refusalFor('delete', selection, withDraftsAndSent, everythingOffered)).toBeNull();
    });

    it('refuses a move across two accounts, and one with nowhere to file into', () => {
        expect(refusalFor('move', [inWork('message-1'), atHome], wholeMailbox, everythingOffered)).toBe(
            'severalAccounts',
        );
        expect(refusalFor('move', [inWork('message-1')], directoryOf({ work: [] }), everythingOffered)).toBe(
            'noOtherFolder',
        );
    });

    it('refuses a move for an account whose only folder is the one the messages are already in', () => {
        const oneFolder = directoryOf({ work: [folder('work-inbox', 'Inbox', ['INBOX'])] });

        expect(refusalFor('move', [inWork('message-1')], oneFolder, everythingOffered)).toBe('noOtherFolder');
    });

    it('offers a move where a folder would take at least one of the messages somewhere it is not', () => {
        const twoFolders = directoryOf({
            work: [folder('work-inbox', 'Inbox', ['INBOX']), folder('work-clients', null, ['Clients'])],
        });
        const filed: ActedMessage = {
            storedEmailId: 'message-2',
            account: 'work',
            folder: 'work-clients',
            unread: false,
            flagged: false,
        };

        expect(refusalFor('move', [inWork('message-1'), filed], twoFolders, everythingOffered)).toBeNull();
    });

    it.each(['archive', 'delete', 'move'] as const)(
        'says the folders are unread rather than blaming the account for %s, which are different things',
        (act) => {
            expect(refusalFor(act, [inWork('message-1')], null, everythingOffered)).toBe('foldersUnknown');
        },
    );

    it('says a credential holding neither grant may not act, rather than offering a folder read nobody would make', () => {
        expect(refusalFor('delete', [inWork('message-1')], null, { flags: true, moves: false, deletes: false })).toBe(
            'notOffered',
        );
    });

    it('reaches a delete in the trash under the deleting grant rather than the moving one', () => {
        expect(
            refusalFor('delete', [inWorkTrash('message-1')], wholeMailbox, {
                flags: false,
                moves: false,
                deletes: true,
            }),
        ).toBeNull();
    });

    it('refuses a delete in the trash for a credential that may file mail but not destroy it', () => {
        expect(
            refusalFor('delete', [inWorkTrash('message-1')], wholeMailbox, {
                flags: false,
                moves: true,
                deletes: false,
            }),
        ).toBe('notOffered');
    });
});

describe('deletesPermanently', () => {
    it('reads a message already in its own account trash as one the act would destroy', () => {
        expect(deletesPermanently(wholeMailbox, [inWorkTrash('message-1')])).toBe(true);
    });

    it('reads a message anywhere else as one the act would file, which is what makes it reversible', () => {
        expect(deletesPermanently(wholeMailbox, [inWork('message-1')])).toBe(false);
    });

    it('takes the reversible reading of a selection reaching outside the trash, rather than two acts in one press', () => {
        expect(deletesPermanently(wholeMailbox, [inWorkTrash('message-1'), inWork('message-2')])).toBe(false);
    });

    it('reads a trash folder as its own account, so an alias another account uses decides nothing', () => {
        const elsewhere: ActedMessage = {
            storedEmailId: 'message-3',
            account: 'home',
            folder: 'work-trash',
            unread: false,
            flagged: false,
        };

        expect(deletesPermanently(wholeMailbox, [elsewhere])).toBe(false);
    });

    it('destroys nothing where the folders were never read, and nothing where there is nothing to act on', () => {
        expect(deletesPermanently(null, [inWorkTrash('message-1')])).toBe(false);
        expect(deletesPermanently(wholeMailbox, [])).toBe(false);
    });
});

describe('filingFor', () => {
    it('files each message in its own account’s folder for the role, rather than in one folder for the batch', () => {
        const twoAccounts = directoryOf({
            work: [folder('work-archive', 'Archive', ['Archive'])],
            home: [folder('home-archive', 'Archive', ['Arkiv'])],
        });

        expect(filingFor('archive', [inWork('message-1'), atHome], twoAccounts, null)).toStrictEqual([
            { storedEmailId: 'message-1', destinationFolder: 'work-archive' },
            { storedEmailId: 'message-9', destinationFolder: 'home-archive' },
        ]);
    });

    it('files every message where the move was told to, which is the one folder somebody chose', () => {
        expect(
            filingFor('move', [inWork('message-1'), inWork('message-2')], wholeMailbox, 'work-clients'),
        ).toStrictEqual([
            { storedEmailId: 'message-1', destinationFolder: 'work-clients' },
            { storedEmailId: 'message-2', destinationFolder: 'work-clients' },
        ]);
    });

    it('files nothing for an act that changes a flag, and nothing where the account labels no such folder', () => {
        expect(filingFor('flag', [inWork('message-1')], wholeMailbox, null)).toStrictEqual([]);
        expect(filingFor('archive', [atHome], wholeMailbox, null)).toStrictEqual([]);
    });
});
