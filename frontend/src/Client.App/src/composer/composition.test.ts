// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailMessage, MailParticipant } from '@mailfathom/client-backend';
import {
    anythingWritten,
    answeredSubject,
    answerTo,
    draftContinued,
    draftHeld,
    looksLikeAnAddress,
    nothingWrittenYet,
    whatWouldBeMissing,
    wireComposition,
} from './composition';

function participant(role: MailParticipant['role'], address: string): MailParticipant {
    return { role, address, displayName: null };
}

function message(subject: string | null, participants: readonly MailParticipant[]): MailMessage {
    return {
        storedEmailId: 'e1',
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        sizeOctets: 1_024,
        headers: {
            subject,
            sentAt: '2026-09-01T08:00:00+00:00',
            receivedAt: '2026-09-01T08:00:01+00:00',
            participants,
            messageId: null,
            inReplyTo: null,
            references: [],
        },
        senderPhoto: null,
        body: { availability: 'Available', plainText: true, html: false },
        sender: {
            authorAuthentication: 'Authenticated',
            deploymentTrust: 'Unknown',
            authenticatedDomain: null,
            authenticationSource: 'ReceivingServer',
            dkimDomain: null,
            spfDomain: null,
            dmarc: 'NotReported',
            trustSource: null,
            trustOrganization: null,
            authorRoute: null,
            trustEvidence: null,
            trustContact: null,
            addToContacts: null,
        },
        signature: {
            outcome: 'NotSigned',
            format: null,
            secureMimeType: null,
            unverifiableReason: null,
            signedAt: null,
            steps: {
                content: 'NotChecked',
                authority: 'NotChecked',
                validity: 'NotChecked',
                revocation: 'NotChecked',
                signer: 'NotChecked',
            },
            trustAnchor: null,
            chainEnd: null,
            validityFailure: null,
            revocationMethod: null,
            revocationCheckedAt: null,
            revokedAt: null,
            signerMatchesAuthor: false,
            signer: null,
        },
        attachments: [],
        carried: null,
        unread: false,
        flagged: false,
        answered: false,
        spam: null,
    };
}

describe('answerTo', () => {
    // There is no default kept for an account, so every answer starts asking for nothing, as a new message does.
    it.each(['senderOnly', 'everyone', 'forward'] as const)(
        'opens a %s answer asking for neither request',
        (answers) => {
            expect(answerTo(message('Invoice', [participant('From', 'ada@example.invalid')]), answers)).toMatchObject({
                requestsDeliveryNotification: false,
                requestsReadReceipt: false,
            });
        },
    );

    it('addresses a reply to the author', () => {
        const composed = answerTo(message('Invoice', [participant('From', 'ada@example.invalid')]), 'senderOnly');

        expect(composed.to).toEqual(['ada@example.invalid']);
        expect(composed.cc).toEqual([]);
    });

    it('addresses a reply to the address the author asked to be answered at', () => {
        const composed = answerTo(
            message('Invoice', [
                participant('From', 'ada@example.invalid'),
                participant('ReplyTo', 'desk@example.invalid'),
            ]),
            'senderOnly',
        );

        expect(composed.to).toEqual(['desk@example.invalid']);
    });

    it('copies everybody else in when the reply is to everyone, and nobody twice', () => {
        const composed = answerTo(
            message('Invoice', [
                participant('From', 'ada@example.invalid'),
                participant('To', 'ada@example.invalid'),
                participant('To', 'bo@example.invalid'),
                participant('Cc', 'bo@example.invalid'),
                participant('Cc', 'cy@example.invalid'),
            ]),
            'everyone',
        );

        expect(composed.to).toEqual(['ada@example.invalid']);
        expect(composed.cc).toEqual(['bo@example.invalid', 'cy@example.invalid']);
    });

    it('addresses a forward to nobody, the conversation in it being somebody else’s', () => {
        const composed = answerTo(
            message('Invoice', [participant('From', 'ada@example.invalid'), participant('Cc', 'bo@example.invalid')]),
            'forward',
        );

        expect(composed.to).toEqual([]);
        expect(composed.cc).toEqual([]);
        expect(composed.subject).toBe('Fwd: Invoice');
    });

    it('carries the message it answers and the account it was read in, which the save is composed from', () => {
        const composed = answerTo(message('Invoice', []), 'everyone');

        expect(composed.answering).toEqual({ storedEmailId: 'e1', answers: 'everyone' });
        expect(composed.account).toBe('work');
        expect(composed.words).toEqual([]);
    });
});

describe('draftContinued', () => {
    // A draft is a message this deployment already holds, so nothing here is composed: what was filed is what comes
    // back, including the blind copies a reply would never have derived.
    it('opens the composer on what was filed, each header where its author put it', () => {
        const filed = message('Half written', [
            participant('From', 'me@example.invalid'),
            participant('To', 'ada@example.invalid'),
            participant('Cc', 'grace@example.invalid'),
            participant('Bcc', 'alan@example.invalid'),
        ]);

        const composed = draftContinued(filed, null);

        expect(composed.subject).toBe('Half written');
        expect(composed.account).toBe('work');
        expect(composed.to).toStrictEqual(['ada@example.invalid']);
        expect(composed.cc).toStrictEqual(['grace@example.invalid']);
        expect(composed.bcc).toStrictEqual(['alan@example.invalid']);
    });

    // It answers nothing — a draft is a message of its own — and says instead which stored message the words came
    // from, which is what tells one composition from another when neither answers anything.
    it('answers nothing and says which stored message it was carried on from', () => {
        const composed = draftContinued(message('Half written', []), null);

        expect(composed.answering).toBeNull();
        expect(composed.continuing).toBe('e1');
    });

    it('opens on an empty subject where the draft was filed without one, rather than on nothing at all', () => {
        expect(draftContinued(message(null, []), null).subject).toBe('');
    });

    it('opens on no words where the body could not be read, which is a draft to carry on rather than one to refuse', () => {
        expect(draftContinued(message('Half written', []), null).words).toStrictEqual([]);
    });
});

describe('draftHeld', () => {
    // A kept draft is carried on with as itself, so what opens is what its record holds, each address under the header
    // its author put it in, and the identity it goes on being saved as.
    it('opens on what the kept draft holds and goes on being that draft', () => {
        const composed = draftHeld({
            draft: {
                draftId: 'd-7',
                account: 'work',
                subject: 'Q4 supplier review',
                recipients: [
                    { role: 'To', address: 'marta@example.invalid', displayName: null },
                    { role: 'Cc', address: 'olek@example.invalid', displayName: null },
                ],
                attachments: [],
                revision: 3,
                sizeOctets: 400,
                serverCopy: 'AppendIssued',
                divergence: 'AppendOutcomeUnknown',
                lastFailureCode: null,
                requestsDeliveryNotification: false,
                requestsReadReceipt: true,
                revisedAt: '2026-09-04T11:55:00+00:00',
            },
            plainTextBody: 'Agenda\nfirst item',
            document: null,
        });

        expect(composed).toMatchObject({
            answering: null,
            continuing: 'd-7',
            account: 'work',
            subject: 'Q4 supplier review',
            to: ['marta@example.invalid'],
            cc: ['olek@example.invalid'],
            bcc: [],
            requestsDeliveryNotification: false,
            requestsReadReceipt: true,
        });
        expect(composed.words).toHaveLength(2);
    });
});

describe('answeredSubject', () => {
    it('reads a reply under the subject it answers', () => {
        expect(answeredSubject('Invoice', 'senderOnly')).toBe('Re: Invoice');
    });

    it('does not stack a prefix on a reply to a reply, whatever it was spelled as', () => {
        expect(answeredSubject('Re: Invoice', 'senderOnly')).toBe('Re: Invoice');
        expect(answeredSubject('RE: Invoice', 'everyone')).toBe('RE: Invoice');
        expect(answeredSubject('fwd: Invoice', 'forward')).toBe('fwd: Invoice');
    });

    it('reads a message with no subject under the prefix alone', () => {
        expect(answeredSubject(null, 'senderOnly')).toBe('Re: ');
    });
});

describe('nothingWrittenYet', () => {
    it('opens addressed to nobody where the act that opened it named nobody', () => {
        expect(nothingWrittenYet('work').to).toStrictEqual([]);
    });

    // Writing to a person from their own page, and to everybody picked out of the address book, are an empty message
    // with the recipients already in it rather than a second kind of composition.
    it('opens addressed to whoever the act that opened it named', () => {
        const written = nothingWrittenYet('work', ['anna@example.invalid', 'bartek@example.invalid']);

        expect(written.to).toStrictEqual(['anna@example.invalid', 'bartek@example.invalid']);
        expect(written.answering).toBeNull();
        expect(written.subject).toBe('');
    });

    it('opens asking for neither a delivery notification nor a read receipt', () => {
        expect(nothingWrittenYet('work')).toMatchObject({
            requestsDeliveryNotification: false,
            requestsReadReceipt: false,
        });
    });
});

describe('whatWouldBeMissing', () => {
    it('names nothing where a message is addressed, titled, and written', () => {
        expect(
            whatWouldBeMissing({
                ...nothingWrittenYet('work'),
                to: ['ada@example.invalid'],
                subject: 'Invoice',
                words: [{ text: 'Here it is.' }],
            }),
        ).toEqual([]);
    });

    it('names each of the three a send would go out without, in the order they are read', () => {
        expect(whatWouldBeMissing(nothingWrittenYet('work'))).toEqual(['noRecipient', 'noSubject', 'noWords']);
    });

    it('reads a blind copy as somebody being addressed', () => {
        expect(whatWouldBeMissing({ ...nothingWrittenYet('work'), bcc: ['ada@example.invalid'] })).toEqual([
            'noSubject',
            'noWords',
        ]);
    });

    it('reads whitespace as nothing written', () => {
        expect(whatWouldBeMissing({ ...nothingWrittenYet('work'), subject: '   ', words: [{ text: '\n' }] })).toContain(
            'noSubject',
        );
    });
});

describe('anythingWritten', () => {
    it('is nothing for a message nobody has touched', () => {
        expect(anythingWritten(nothingWrittenYet('work'))).toBe(false);
    });

    it('is something once anybody is addressed or anything is written', () => {
        expect(anythingWritten({ ...nothingWrittenYet('work'), words: [{ text: 'Hello' }] })).toBe(true);
        expect(anythingWritten({ ...nothingWrittenYet('work'), cc: ['ada@example.invalid'] })).toBe(true);
    });

    it('is nothing for a subject alone, an answer opening with one it did not write', () => {
        expect(anythingWritten({ ...nothingWrittenYet('work'), subject: 'Re: Invoice' })).toBe(false);
    });
});

describe('wireComposition', () => {
    it('states the account and the subject for a message of its own', () => {
        expect(
            wireComposition({ ...nothingWrittenYet('work'), subject: 'Invoice', to: ['ada@example.invalid'] }),
        ).toEqual({
            account: 'work',
            subject: 'Invoice',
            plainTextBody: '',
            htmlBody: null,
            to: ['ada@example.invalid'],
            cc: [],
            bcc: [],
            requestsDeliveryNotification: false,
            requestsReadReceipt: false,
        });
    });

    it('states the two requests the message makes, each as the author left it', () => {
        expect(wireComposition({ ...nothingWrittenYet('work'), requestsDeliveryNotification: true })).toMatchObject({
            requestsDeliveryNotification: true,
            requestsReadReceipt: false,
        });
    });

    it('states the message it answers instead, both of those being the deployment’s to derive', () => {
        const wire = wireComposition({
            ...nothingWrittenYet('work'),
            answering: { storedEmailId: 'e1', answers: 'senderOnly' },
            subject: 'Re: Invoice',
            words: [{ text: 'Thank you.' }],
        });

        expect(wire).toEqual({
            answeredEmailId: 'e1',
            answers: 'senderOnly',
            plainTextBody: 'Thank you.',
            htmlBody: 'Thank you.',
            to: [],
            cc: [],
            bcc: [],
            requestsDeliveryNotification: false,
            requestsReadReceipt: false,
        });
    });
});

describe('looksLikeAnAddress', () => {
    it.each([
        ['ada@example.invalid', true],
        ['ada', false],
        ['@example.invalid', false],
        ['ada@', false],
        ['ada@one@two', false],
        ['ada bo@example.invalid', false],
    ])('reads %s as an address: %s', (text, shaped) => {
        expect(looksLikeAnAddress(text)).toBe(shaped);
    });
});
