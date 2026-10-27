// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailSpamVerdict, MailTimelineEntry } from '@mailfathom/client-backend';
import {
    actLeaving,
    actPending,
    allDrawnSpam,
    drawnFlagged,
    drawnSpam,
    flagPending,
    nothingActed,
    opensAsDraft,
    type ActedMessage,
    type MailboxAct,
    type MailboxActs,
} from './useMailboxActs';

const email: MailTimelineEntry = {
    id: 'message-1',
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    subject: 'The quarter is closed',
    receivedAt: '2026-08-31T09:41:00+00:00',
    sentAt: null,
    senderAddress: 'writer@nordwind.example',
    senderDisplayName: 'Writer',
    toAddresses: ['user@example.invalid'],
    unread: false,
    flagged: false,
    answered: false,
    hasAttachments: false,
    attachmentCount: 0,
    sizeOctets: 1_024,
    preview: 'The opening of the message.',
    enrichment: null,
    threadMessageCount: null,
    spam: null,
    sender: {
        authorAuthentication: 'Authenticated',
        deploymentTrust: 'Unknown',
        signatureOutcome: 'NotSigned',
        signerMatchesAuthor: false,
    },
};

function asking(
    act: MailboxAct,
    storedEmailId = email.id,
    from = email.folder,
    leaves = act === 'archive' || act === 'move' || act === 'delete',
    destroys = act === 'delete' && !leaves,
): MailboxActs {
    return { ...nothingActed, asked: new Map([[storedEmailId, { act, from, leaves, destroys }]]) };
}

// What retires a pending act, which is the whole of why this client polls nothing: an act writes a record and the
// account's own pass issues the mail-server command later, so what says it has landed is the deployment reporting the
// row differently — never a timer and never a second read this client asked for.
describe('actPending', () => {
    it('says nothing about a message nothing was asked of', () => {
        expect(actPending(nothingActed, email)).toBeNull();
    });

    it('says nothing about a message another one’s act was asked of', () => {
        expect(actPending(asking('archive', 'another-message'), email)).toBeNull();
    });

    it.each(['archive', 'delete', 'move'] as const)(
        'keeps saying a message is being %sd until the folder it is leaving stops listing it',
        (act) => {
            expect(actPending(asking(act), email)?.act).toBe(act);
        },
    );

    it('stops saying a message is being flagged once the deployment reports it flagged', () => {
        expect(actPending(asking('flag'), email)?.act).toBe('flag');
        expect(actPending(asking('flag'), { ...email, flagged: true })).toBeNull();
    });

    it('stops saying a flag is being taken off once the deployment reports the message unflagged', () => {
        expect(actPending(asking('unflag'), { ...email, flagged: true })?.act).toBe('unflag');
        expect(actPending(asking('unflag'), email)).toBeNull();
    });

    it('stops saying a message is being marked unread once the deployment reports it unread', () => {
        expect(actPending(asking('markUnread'), email)?.act).toBe('markUnread');
        expect(actPending(asking('markUnread'), { ...email, unread: true })).toBeNull();
    });

    it('stops saying a message is being marked read once the deployment reports it read, the same rule turned round', () => {
        expect(actPending(asking('markRead'), { ...email, unread: true })?.act).toBe('markRead');
        expect(actPending(asking('markRead'), email)).toBeNull();
    });

    // An act is about a message in a place, so the sentence belongs to the place it was asked from: the same message
    // drawn in the folder it was filed into has arrived rather than being on its way.
    it('says nothing about the same message drawn in the folder the act filed it into', () => {
        expect(actPending(asking('move'), { ...email, folder: 'Archive' })).toBeNull();
    });
});

// The mark a row draws rather than a sentence about it, which is the whole of what either flag act reports: it appears
// at the press and goes at the press, because a mutation is durable the moment it is written down and the design draws
// the outcome instead of the mechanism.
describe('drawnFlagged', () => {
    it('draws what the deployment reported where nothing was asked of the message', () => {
        expect(drawnFlagged(nothingActed, email)).toBe(false);
        expect(drawnFlagged(nothingActed, { ...email, flagged: true })).toBe(true);
    });

    it('draws the flag from the press, ahead of the deployment reporting it', () => {
        expect(drawnFlagged(asking('flag'), email)).toBe(true);
    });

    it('takes the flag off at the press, which is the same rule in the other direction', () => {
        expect(drawnFlagged(asking('unflag'), { ...email, flagged: true })).toBe(false);
    });

    it('draws what the deployment reported once the act about the message has landed', () => {
        expect(drawnFlagged(asking('flag'), { ...email, flagged: true })).toBe(true);
    });

    it('leaves an act about another message out of it', () => {
        expect(drawnFlagged(asking('flag', 'another-message'), email)).toBe(false);
    });

    it('draws the flag asked first while the read flag asked after it is on its way beside it', () => {
        const markedAfterFlagging: MailboxActs = {
            ...nothingActed,
            asked: new Map([
                [
                    email.id,
                    {
                        act: 'markUnread',
                        from: email.folder,
                        leaves: false,
                        destroys: false,
                        beside: { act: 'flag', from: email.folder, leaves: false, destroys: false },
                    },
                ],
            ]),
        };

        expect(drawnFlagged(markedAfterFlagging, email)).toBe(true);
    });
});

// A row draws both marks, and a message can be waiting on both flags at once, so each mark is read from the act that
// writes that flag rather than from whichever act was asked last.
describe('flagPending', () => {
    const flaggedAfterMarking: MailboxActs = {
        ...nothingActed,
        asked: new Map([
            [
                email.id,
                {
                    act: 'flag',
                    from: email.folder,
                    leaves: false,
                    destroys: false,
                    beside: { act: 'markUnread', from: email.folder, leaves: false, destroys: false },
                },
            ],
        ]),
    };

    it('answers each flag with the act that writes it', () => {
        expect(flagPending(flaggedAfterMarking, email, 'seen')?.act).toBe('markUnread');
        expect(flagPending(flaggedAfterMarking, email, 'flagged')?.act).toBe('flag');
    });

    it('stops answering a flag once the deployment reports what its act asked for', () => {
        expect(flagPending(flaggedAfterMarking, { ...email, unread: true }, 'seen')).toBeNull();
        expect(flagPending(flaggedAfterMarking, { ...email, unread: true }, 'flagged')?.act).toBe('flag');
    });

    it('answers nothing for a flag no act was asked of', () => {
        expect(flagPending(asking('flag'), email, 'seen')).toBeNull();
    });
});

// What the list acts on rather than what a row says: an act a person performs is theirs, so the message goes from the
// folder it is leaving at the press instead of when a mailbox is next seen to agree.
describe('actLeaving', () => {
    it('says nothing about a message nothing was asked of', () => {
        expect(actLeaving(nothingActed, email)).toBe(false);
    });

    it('says a message asked to be filed elsewhere is leaving the folder it was asked in', () => {
        expect(actLeaving(asking('archive'), email)).toBe(true);
    });

    it('says nothing about that message once it is drawn in the folder it went to', () => {
        expect(actLeaving(asking('archive'), { ...email, folder: 'Archive' })).toBe(false);
    });

    it('says a delete that destroys the mail leaves no list, there being nowhere for the message to go', () => {
        expect(actLeaving(asking('delete', email.id, email.folder, false), email)).toBe(false);
    });

    it('says a flag change leaves no list either, the message staying exactly where it is', () => {
        expect(actLeaving(asking('flag'), email)).toBe(false);
    });

    it('says a message marked spam is leaving the folder it was marked in where the mark moves it', () => {
        expect(actLeaving(marking('Spam', email.id, email.folder, true), email)).toBe(true);
    });

    it('says nothing about a message marked spam where the mark moves nothing', () => {
        expect(actLeaving(marking('Spam', email.id, email.folder, false), email)).toBe(false);
    });

    it('says nothing about a marked message once it is drawn in the folder the mark moved it to', () => {
        expect(actLeaving(marking('Spam', email.id, email.folder, true), { ...email, folder: 'Junk' })).toBe(false);
    });
});

function verdictOf(verdict: MailSpamVerdict['verdict'], decidedBy: MailSpamVerdict['decidedBy']): MailSpamVerdict {
    return {
        verdict,
        decidedBy,
        spamSubcategory: null,
        spamSubcategoryNamedBy: null,
        phishingRequest: null,
        impersonatedParty: null,
    };
}

function marking(
    verdict: MailSpamVerdict['verdict'],
    storedEmailId = email.id,
    from = email.folder,
    leaves = false,
): MailboxActs {
    return {
        ...nothingActed,
        spamMarked: new Map([[storedEmailId, { verdict: verdictOf(verdict, 'Person'), from, leaves }]]),
    };
}

function acted(storedEmailId: string, spam: MailSpamVerdict | null): ActedMessage {
    return { storedEmailId, account: 'work', folder: 'INBOX', unread: false, flagged: false, spam };
}

// Every surface draws one verdict about a message, so the person's own mark from this session stands over whatever the
// deployment last reported, in either direction.
describe('drawnSpam', () => {
    const judged = verdictOf('Spam', 'Model');

    it('draws what the deployment reported about a message nobody has marked', () => {
        expect(drawnSpam(nothingActed, { id: email.id, spam: judged })).toBe(judged);
    });

    it('draws the person’s not-spam mark over the deployment’s spam verdict', () => {
        expect(drawnSpam(marking('NotSpam'), { id: email.id, spam: judged })?.verdict).toBe('NotSpam');
    });

    it('draws the person’s spam mark over a message the deployment judged nothing about', () => {
        expect(drawnSpam(marking('Spam'), { id: email.id, spam: null })?.verdict).toBe('Spam');
    });

    it('draws nothing of another message’s mark', () => {
        expect(drawnSpam(marking('Spam', 'another-message'), { id: email.id, spam: null })).toBeNull();
    });
});

// What turns the one spam control to *Not spam*: every message under it drawn spam, and at least one message there.
describe('allDrawnSpam', () => {
    it('says an empty selection is not all spam, since there is nothing to mark not spam', () => {
        expect(allDrawnSpam(nothingActed, [])).toBe(false);
    });

    it('says a selection whose every message is drawn spam is all spam', () => {
        const messages = [acted('message-1', verdictOf('Spam', 'Model')), acted('message-2', null)];

        expect(allDrawnSpam(marking('Spam', 'message-2'), messages)).toBe(true);
    });

    it('says a selection one of whose messages the person marked not spam is not all spam', () => {
        const messages = [
            acted('message-1', verdictOf('Spam', 'Model')),
            acted('message-2', verdictOf('Spam', 'Model')),
        ];

        expect(allDrawnSpam(marking('NotSpam', 'message-2'), messages)).toBe(false);
    });
});

// Where a message opens is decided by what its own account calls the folder it is in rather than by what the folder
// is named: a deployment whose drafts folder is `Entwürfe` still opens one in the composer.
describe('opensAsDraft', () => {
    it('says a message in the folder its account labels for drafts is one to carry on writing', () => {
        expect(opensAsDraft({ ...nothingActed, folderRoleOf: () => 'Drafts' }, email)).toBe(true);
    });

    it('says a message in any other folder is one to read', () => {
        expect(opensAsDraft({ ...nothingActed, folderRoleOf: () => 'Inbox' }, email)).toBe(false);
    });

    // A session that may not file mail reads no folders at all, so nothing says which folder is for drafts — and a
    // message opened as mail is the answer that costs a reader nothing.
    it('says a message is one to read where no folder was read at all', () => {
        expect(opensAsDraft(nothingActed, email)).toBe(false);
    });
});
