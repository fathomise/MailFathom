// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type {
    ClientMessageView,
    ClientSession,
    MailAttachment,
    MailBody,
    ClientResponse,
    MailCarried,
    MailFathomTransport,
    MailMessage,
    MailSpamVerdict,
} from '@mailfathom/client-backend';
import { AttachmentExchangeContext, type AttachmentExchange } from '../deployment/attachmentExchange';
import { LocalizationProvider } from '../localization/Localization';
import { MailboxActsContext, nothingActed, type MailboxActs } from '../mailboxActs/useMailboxActs';
import { MessageViewContext } from '../preferences/messageView';
import { ReadMarkingContext, nothingMarkedRead, type ReadMarking } from '../readMarking/useReadMarking';
import { LinkOpenerContext } from '../shellOperations/linkOpener';
import { ToastsProvider } from '../toasts/Toasts';
import { WorkspaceProvider } from '../workspace/Workspace';
import { OpenAttachmentContext } from '../workspace/openAttachment';
import type { MessageBodyRead } from '../messageBody/useMessageBody';
import { OpenedMessage } from './OpenedMessage';

// The drawing one message takes wherever it is drawn. What is asserted here is that it is one drawing rather than two:
// the surface reading a message on its own and the conversation reading it inside a correspondence hand this the same
// two values and get the same thing back, so a difference between the two screens cannot be introduced in one of them.

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const answersNothing: MailFathomTransport = () => new Promise<ClientResponse>(() => undefined);

const storedEmailId = '00000000-0000-4000-8000-000000000000';

const described: MailMessage = {
    storedEmailId,
    account: 'work',
    folder: 'INBOX',
    threadId: null,
    sizeOctets: 40_960,
    headers: {
        subject: 'Quarterly invoice',
        sentAt: '2026-08-31T09:41:00+00:00',
        receivedAt: '2026-08-31T09:41:10+00:00',
        participants: [{ role: 'From', address: 'billing@example.invalid', displayName: 'Billing' }],
        messageId: 'abc@example.invalid',
        inReplyTo: null,
        references: [],
    },
    senderPhoto: null,
    report: null,
    receipts: null,
    readReceiptRequest: null,
    body: { availability: 'Readable', plainText: true, html: true },
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
        trustMember: null,
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
    unread: true,
    flagged: false,
    answered: false,
    spam: null,
};

const said: MailBody = {
    storedEmailId,
    availability: 'Readable',
    plainText: { text: 'The invoice is attached.', originalCharacterCount: 24, truncation: 'None' },
    document: null,
    selfContainedHtml: null,
    remoteImagesRequested: false,
};

/** A read that has already answered, which is the state both surfaces hand this component in the ordinary case. */
const read: MessageBodyRead = {
    drawn: { outcome: 'read', value: said },
    reading: false,
    askingForPictures: false,
    askedForPictures: false,
    embeddedHtml: false,
    cleaned: null,
    cleaning: false,
    readAgain: () => undefined,
    showRemotePictures: () => undefined,
    showWithoutRemotePictures: () => undefined,
};

/** What a message carries once its parts were read and nothing about them is worth a sentence. */
const nothingCarried: MailCarried = {
    attachmentCount: 0,
    totalSizeOctets: 0,
    inlineResourceCount: 0,
    encrypted: false,
    unverifiedSignature: false,
    unexpandedTnefPart: false,
};

// The strip under the words is drawn wherever a message carries a file, and what it does when one is asked for is
// `Attachments.test.tsx`'s: this file only needs it drawable.
const deliversNothing: AttachmentExchange = {
    deliver: () => Promise.resolve('delivered'),
    read: () => Promise.resolve({ outcome: 'shown', content: '' }),
    take: () => Promise.resolve({ outcome: 'taken', octets: new Blob() }),
};

function attached(position: number, fileName: string): MailAttachment {
    return { position, fileName, wasFileNameNormalized: false, mediaType: 'application/pdf', sizeOctets: 20_480 };
}

// A zone pinned for one test is put back for the reason a fake clock is released: it is the worker's, not the file's.
// Assigning `undefined` to an environment variable writes the string "undefined", which is a zone of its own.
const machineZone = process.env['TZ'];

afterEach(() => {
    vi.useRealTimers();

    if (machineZone === undefined) {
        Reflect.deleteProperty(process.env, 'TZ');
    } else {
        process.env['TZ'] = machineZone;
    }
});

function drawing(
    message: MailMessage = described,
    body: MessageBodyRead = read,
    marking: ReadMarking = nothingMarkedRead,
    view: ClientMessageView = 'reduced',
    onShowFullHtml: () => void = () => undefined,
    acts: MailboxActs = nothingActed,
    shown: { readonly receiptsShown?: boolean; readonly answersReadReceipt?: boolean } = {},
): void {
    render(
        <LocalizationProvider>
            <ToastsProvider>
                <WorkspaceProvider>
                    <LinkOpenerContext value={() => Promise.resolve()}>
                        <MessageViewContext value={view}>
                            <ReadMarkingContext value={marking}>
                                <MailboxActsContext value={acts}>
                                    <AttachmentExchangeContext value={deliversNothing}>
                                        <OpenAttachmentContext value={() => undefined}>
                                            <OpenedMessage
                                                session={session}
                                                transport={answersNothing}
                                                message={message}
                                                body={body}
                                                onShowFullHtml={onShowFullHtml}
                                                {...shown}
                                            />
                                        </OpenAttachmentContext>
                                    </AttachmentExchangeContext>
                                </MailboxActsContext>
                            </ReadMarkingContext>
                        </MessageViewContext>
                    </LinkOpenerContext>
                </WorkspaceProvider>
            </ToastsProvider>
        </LocalizationProvider>,
    );
}

describe('OpenedMessage', () => {
    it('draws who wrote the message and what it says', () => {
        drawing();

        expect(screen.getByText('Billing')).toBeDefined();
        expect(screen.getByText('The invoice is attached.')).toBeDefined();
    });

    it('names an author the message wrote nobody for, rather than drawing an empty line', () => {
        drawing({ ...described, headers: { ...described.headers, participants: [] } });

        expect(screen.getByText('This message names nobody as its author.')).toBeDefined();
    });

    // ADR 0026: the body being drawn is what opening a message means, and it is one rule wherever a message is drawn.
    it('marks the message read, and says where in the mailbox it stands', () => {
        const opened: { storedEmailId: string; account: string; folder: string; unread: boolean }[] = [];

        drawing(described, read, {
            marked: new Map(),
            markRead: (message) => {
                opened.push(message);
            },
            recordMarked: () => undefined,
            forget: () => undefined,
        });

        expect(opened).toStrictEqual([{ storedEmailId, account: 'work', folder: 'INBOX', unread: true }]);
    });

    it('offers the sender own markup, which is the ask a reader makes about one message', () => {
        const onShowFullHtml = vi.fn();

        drawing(described, read, nothingMarkedRead, 'reduced', onShowFullHtml);
        fireEvent.click(screen.getByRole('button', { name: 'Show the original message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Show the original' }));

        expect(onShowFullHtml).toHaveBeenCalled();
    });

    // With the embedded view chosen the markup is already under this line, so a control offering to open it would open
    // a second copy of what is being read.
    it('offers no way to the markup where the reader is already reading it', () => {
        drawing(described, read, nothingMarkedRead, 'embeddedHtml');

        expect(screen.queryByRole('button', { name: 'Show the original message' })).toBeNull();
    });

    // The cleaned rendering is the reduced tree with blocks dropped, so the sender's own version is still somewhere the
    // reader has not been: the control turns on the embedded view alone rather than on "not the reduced one".
    it('offers the way to the markup to a reader on the cleaned rendering', () => {
        drawing(described, read, nothingMarkedRead, 'cleaned');

        expect(screen.getByRole('button', { name: 'Show the original message' })).toBeDefined();
    });

    it('counts the files the message carries on the line naming its author', () => {
        drawing({ ...described, attachments: [attached(1, 'invoice.pdf'), attached(2, 'terms.pdf')] });

        expect(screen.getByText(new Intl.NumberFormat('en').format(2))).toBeDefined();
    });

    it('counts no files on a message that carries none', () => {
        drawing();

        expect(screen.getByText('Billing')).toBeDefined();
        expect(screen.queryByText(new Intl.NumberFormat('en').format(0))).toBeNull();
    });

    // The same instant read against two readers' days, so a card that stopped honouring the reader's own zone fails
    // rather than agreeing with itself. It is five in the morning in Los Angeles and two in the afternoon in Warsaw.
    it.each([
        ['Europe/Warsaw', '1:12 AM'],
        ['America/Los_Angeles', 'yesterday'],
    ])('words when this deployment recorded the message against the day a reader in %s is on', (zone, said) => {
        process.env['TZ'] = zone;
        vi.useFakeTimers();
        vi.setSystemTime(new Date('2026-09-01T12:00:00Z'));

        drawing({ ...described, headers: { ...described.headers, receivedAt: '2026-08-31T23:12:10+00:00' } });

        expect(screen.getByText(said).getAttribute('datetime')).toBe('2026-08-31T23:12:10+00:00');
    });

    it('says what everything the message has attached comes to', () => {
        drawing({ ...described, carried: { ...nothingCarried, attachmentCount: 2, totalSizeOctets: 40_960 } });

        const size = new Intl.NumberFormat('en', { style: 'unit', unit: 'kilobyte', unitDisplay: 'short' }).format(41);

        expect(screen.getByText(`Everything attached comes to ${size}.`)).toBeDefined();
    });

    it.each([
        ['encrypted', 'This message carries encrypted content somewhere.'],
        ['unverifiedSignature', 'This message carries a signature, and nothing here has verified it.'],
        [
            'unexpandedTnefPart',
            'This message carries a winmail.dat part, which was recorded without being opened, so whatever it holds is not listed above.',
        ],
    ] as const)('says the message is %s, and nothing about a total where nothing is attached', (fact, said) => {
        drawing({ ...described, carried: { ...nothingCarried, [fact]: true } });

        expect(screen.getByText(said)).toBeDefined();
        expect(screen.queryByText(/^Everything attached comes to /u)).toBeNull();
    });

    it.each([
        ['nothing has read its parts', null],
        ['nothing it carries is worth a sentence', nothingCarried],
    ])('says nothing about what the message carries where %s', (_, carried) => {
        drawing({ ...described, carried });

        expect(screen.getByText('The invoice is attached.')).toBeDefined();
        expect(screen.queryByText(/^Everything attached comes to /u)).toBeNull();
        expect(screen.queryByText(/^This message carries /u)).toBeNull();
    });

    // The screen's own head is the caller's: a conversation says its subject once above every message in it, and a
    // message read on its own says it above that one.
    it('draws no subject of its own, that being the surface head each caller draws', () => {
        drawing();

        expect(screen.queryByRole('heading', { name: 'Quarterly invoice' })).toBeNull();
    });
});

describe('OpenedMessage, marked spam', () => {
    const phishing: MailSpamVerdict = {
        verdict: 'Spam',
        decidedBy: 'Model',
        spamSubcategory: 'Phishing',
        spamSubcategoryNamedBy: 'Model',
        phishingRequest: 'Credentials',
        impersonatedParty: 'Bank',
    };

    const spam: MailMessage = { ...described, spam: phishing };

    it('warns over the words that the message is spam and what it asks for', () => {
        drawing(spam);

        expect(screen.getByText('Phishing. This message is spam and a security risk.')).toBeDefined();
        expect(
            screen.getByText('It asks for a password, and it is written to look as if it came from a bank.'),
        ).toBeDefined();
    });

    it('carries no warning for a message that is not spam', () => {
        drawing();

        expect(screen.queryByText(/This message is spam/u)).toBeNull();
    });

    it('carries no warning for a message judged not spam', () => {
        drawing({
            ...described,
            spam: { ...phishing, verdict: 'NotSpam', spamSubcategory: null, spamSubcategoryNamedBy: null },
        });

        expect(screen.queryByText(/This message is spam/u)).toBeNull();
    });

    it('opens no original, saying why, rather than asking the question that would', async () => {
        const onShowFullHtml = vi.fn();

        drawing(spam, read, nothingMarkedRead, 'reduced', onShowFullHtml);
        fireEvent.click(screen.getByRole('button', { name: 'Show the original message' }));

        expect(await screen.findByText('Its original does not open. Mark it not spam first.')).toBeDefined();
        expect(onShowFullHtml).not.toHaveBeenCalled();
    });

    // The embedded view is not drawn for spam, so the control that says why its original stays shut stays drawn.
    it('keeps the way to the original where the reader chose the sender’s own markup', () => {
        drawing(spam, read, nothingMarkedRead, 'embeddedHtml');

        expect(screen.getByRole('button', { name: 'Show the original message' })).toBeDefined();
    });

    it('opens none of its files, saying why', async () => {
        drawing({ ...spam, attachments: [attached(1, 'invoice.pdf')] });
        fireEvent.click(screen.getByRole('button', { name: 'Open invoice.pdf' }));

        expect(await screen.findByText('Its attachments do not open. Mark it not spam first.')).toBeDefined();
    });
});

describe('OpenedMessage, the mark on who sent it', () => {
    it('draws the mark beside the sender, named for what was established about them', () => {
        drawing({ ...described, sender: { ...described.sender, deploymentTrust: 'Trusted' } });

        expect(screen.getByRole('button', { name: 'Trusted sender' }).textContent).toBe('Trusted');
    });

    it('opens the details of every step from the mark', () => {
        drawing();

        fireEvent.click(screen.getByRole('button', { name: 'Sender not recognized' }));

        expect(screen.getByRole('dialog', { name: 'Sender not recognized' })).toBeDefined();
        expect(screen.getByRole('region', { name: 'Sender authentication' })).toBeDefined();
    });

    it('marks no sender on a message in Sent, whose writer is the reader', () => {
        drawing(described, read, nothingMarkedRead, 'reduced', () => undefined, {
            ...nothingActed,
            folderRoleOf: () => 'Sent',
        });

        expect(screen.queryByRole('button', { name: 'Sender not recognized' })).toBeNull();
        expect(screen.getByText('Billing')).toBeDefined();
    });
});

describe('OpenedMessage, what came back for it', () => {
    const sent: MailMessage = {
        ...described,
        folder: 'Sent',
        unread: false,
        headers: {
            ...described.headers,
            participants: [
                { role: 'From', address: 'me@example.invalid', displayName: 'Me' },
                { role: 'To', address: 'anna@example.invalid', displayName: 'Anna Kowalska' },
                { role: 'To', address: 'jacek@example.invalid', displayName: 'Jacek Wrona' },
            ],
        },
        receipts: {
            deliveryRequested: true,
            readRequested: true,
            recipients: [
                {
                    address: 'anna@example.invalid',
                    delivery: null,
                    read: {
                        outcome: 'Displayed',
                        reportedAt: '2026-08-31T10:12:00+00:00',
                        source: 'Report',
                        status: null,
                        reportingServer: null,
                    },
                },
                { address: 'jacek@example.invalid', delivery: null, read: null },
            ],
        },
    };

    it('marks the head of a sent message with what came back, naming each recipient’s own outcome', () => {
        drawing(sent);

        expect(
            screen.getByRole('button', {
                name: `Read by 1 of 2 - ${new Intl.ListFormat('en', { type: 'unit', style: 'short' }).format([
                    'Anna Kowalska read',
                    'Jacek Wrona nothing back yet',
                ])}`,
            }).textContent,
        ).toBe('Read by 1 of 2');
    });

    it('opens every recipient’s own receipts from the mark', () => {
        drawing(sent);
        fireEvent.click(screen.getByRole('button', { name: /^Read by 1 of 2/u }));

        const recipients = screen.getByRole('region', { name: 'Recipients' });

        expect(recipients.textContent).toContain('Anna Kowalska');
        expect(recipients.textContent).toContain('Jacek Wrona');
    });

    it('draws no mark where the surface draws the message without one', () => {
        drawing(sent, read, nothingMarkedRead, 'reduced', () => undefined, nothingActed, { receiptsShown: false });

        expect(screen.queryByRole('button', { name: /^Read by/u })).toBeNull();
    });

    it('draws no mark on a message that asked for nothing and got nothing', () => {
        drawing();

        expect(screen.queryByRole('button', { name: /^(Read|Delivered|Not delivered|Delayed|Receipts)/u })).toBeNull();
    });
});

describe('OpenedMessage, a report that came back as mail', () => {
    const readReceipt: MailMessage = {
        ...described,
        headers: {
            ...described.headers,
            subject: 'Read: CPI calculation 2027',
            participants: [{ role: 'From', address: 'marta@example.invalid', displayName: 'Marta Nowak' }],
        },
        report: {
            kind: 'Read',
            recipients: [{ address: 'marta@example.invalid', outcome: 'Displayed' }],
            reportedAt: null,
            namesOwnMessage: true,
            ownMessageSubject: 'CPI calculation 2027',
        },
    };

    it('folds a report to the one line naming what it reports, and marks it read for that line', () => {
        const markRead = vi.fn();

        drawing(readReceipt, read, { ...nothingMarkedRead, markRead });

        const line = screen.getByRole('button', { name: 'Marta Nowak read your message' });

        expect(line.getAttribute('aria-expanded')).toBe('false');
        expect(screen.queryByText('The invoice is attached.')).toBeNull();
        expect(markRead).toHaveBeenCalledWith(expect.objectContaining({ storedEmailId }));
    });

    it('unfolds a report on a press and folds it again on the next', () => {
        drawing(readReceipt);

        const line = screen.getByRole('button', { name: 'Marta Nowak read your message' });

        fireEvent.click(line);

        expect(line.getAttribute('aria-expanded')).toBe('true');
        expect(screen.getByText('The invoice is attached.')).toBeDefined();

        fireEvent.click(line);

        expect(line.getAttribute('aria-expanded')).toBe('false');
        expect(screen.queryByText('The invoice is attached.')).toBeNull();
    });

    it('draws a message that is no report unfolded, with no line over it', () => {
        drawing();

        expect(screen.queryByRole('button', { expanded: false })).toBeNull();
        expect(screen.getByText('The invoice is attached.')).toBeDefined();
    });
});

describe('OpenedMessage, a read receipt it asks for', () => {
    const asking: MailMessage = {
        ...described,
        readReceiptRequest: { address: 'billing@example.invalid', namesAnotherAddress: false, sentAt: null },
    };

    it('offers to send the receipt the message asks for', () => {
        drawing(asking);

        expect(screen.getByRole('button', { name: 'Send read receipt' })).toBeDefined();
    });

    it('offers nothing on a message marked spam, whatever it asks', () => {
        drawing({
            ...asking,
            spam: {
                verdict: 'Spam',
                decidedBy: 'Model',
                spamSubcategory: 'Phishing',
                spamSubcategoryNamedBy: 'Model',
                phishingRequest: 'Credentials',
                impersonatedParty: 'Bank',
            },
        });

        expect(screen.queryByRole('button', { name: 'Send read receipt' })).toBeNull();
    });

    it('offers nothing where the surface answers the request on another message', () => {
        drawing(asking, read, nothingMarkedRead, 'reduced', () => undefined, nothingActed, {
            answersReadReceipt: false,
        });

        expect(screen.queryByRole('button', { name: 'Send read receipt' })).toBeNull();
    });
});
