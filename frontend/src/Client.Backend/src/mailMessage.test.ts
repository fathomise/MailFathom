// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import { mailMessageRoute, readMailMessage, type MailMessage } from './mailMessage';
import type { ClientSession } from './session';
import type { ClientRequest, ClientResponse, MailFathomTransport } from './transport';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const verdict = {
    authorAuthentication: 'Authenticated',
    deploymentTrust: 'Trusted',
    authenticatedDomain: 'mail.example.invalid',
    authenticationSource: 'ReceivingServer',
    dkimDomain: 'MAIL.EXAMPLE.INVALID',
    spfDomain: 'BOUNCES.EXAMPLE.INVALID',
    dmarc: 'Pass',
    trustSource: 'PersonalContact',
    trustOrganization: null,
};

const unsigned = {
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
};

const signer = {
    name: 'Billing',
    address: 'billing@example.invalid',
    issuer: 'CN=Example Issuing Authority',
    validFrom: '2026-01-01T00:00:00+00:00',
    validUntil: '2027-01-01T00:00:00+00:00',
    serialNumber: '0A1B2C3D',
    fingerprint: '3F4E5D6C7B8A99887766554433221100FFEEDDCC',
    keyId: null,
    publicKeyAlgorithm: 'RSA',
    digestAlgorithm: 'SHA256',
};

const verified = {
    outcome: 'Verified',
    format: 'SecureMimeDetached',
    secureMimeType: null,
    unverifiableReason: null,
    signedAt: '2026-08-31T09:40:58+00:00',
    steps: { content: 'Passed', authority: 'Passed', validity: 'Passed', revocation: 'Passed', signer: 'Passed' },
    trustAnchor: 'SystemTrustStore',
    chainEnd: 'CN=Example Root Authority',
    validityFailure: null,
    revocationMethod: 'Ocsp',
    revocationCheckedAt: '2026-08-31T09:41:10+00:00',
    revokedAt: null,
    signerMatchesAuthor: true,
    signer,
};

/** The whole of what the message route answers with, which every test below narrows or breaks one field of. */
function described(overrides: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        storedEmailId: messageId,
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        sizeOctets: 40_960,
        headers: {
            subject: 'Quarterly invoice',
            sentAt: '2026-08-31T09:41:00+00:00',
            receivedAt: '2026-08-31T09:41:10+00:00',
            participants: [
                { role: 'From', address: 'billing@example.invalid', displayName: 'Billing' },
                { role: 'To', address: 'reader@example.invalid', displayName: null },
            ],
            messageId: 'abc@example.invalid',
            inReplyTo: null,
            references: [],
        },
        body: { availability: 'Readable', plainText: true, html: true },
        sender: verdict,
        signature: unsigned,
        attachments: [
            {
                position: 0,
                fileName: 'invoice.pdf',
                wasFileNameNormalized: false,
                mediaType: 'application/pdf',
                sizeOctets: 2_048,
            },
        ],
        carried: {
            attachmentCount: 1,
            totalSizeOctets: 2_048,
            inlineResourceCount: 3,
            encrypted: false,
            unverifiedSignature: true,
            unexpandedTnefPart: false,
        },
        unread: true,
        flagged: false,
        answered: false,
        ...overrides,
    });
}

type Answer = Omit<ClientResponse, 'headers'>;

function answering(response: Answer): MailFathomTransport {
    return () => Promise.resolve({ ...response, headers: {} });
}

function recording(response: Answer): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ ...response, headers: {} });
        },
    };
}

async function readingOf(body: string): Promise<MailMessage | null> {
    const result = await readMailMessage(session, answering({ status: 200, body }), messageId);

    return result.outcome === 'read' ? result.value : null;
}

describe('mailMessageRoute', () => {
    it('encodes the identifier it is given rather than writing it into the path as it stands', () => {
        expect(mailMessageRoute('a/../b')).toBe('/messages/a%2F..%2Fb');
    });
});

describe('readMailMessage', () => {
    it('asks for the message route on the client surface with the session it was given', async () => {
        const { transport, requests } = recording({ status: 200, body: described() });

        await readMailMessage(session, transport, messageId);

        expect(requests).toEqual([
            {
                method: 'GET',
                path: `https://mail.example.invalid/api/client/messages/${messageId}`,
                headers: { Accept: 'application/json', Authorization: 'Basic dGVzdA==' },
                longestAnswer: 4 * 1024 * 1024,
            },
        ]);
    });

    it('reads the message a well-formed answer describes', async () => {
        const message = await readingOf(described());

        expect(message?.headers.subject).toBe('Quarterly invoice');
        expect(message?.headers.participants).toEqual([
            { role: 'From', address: 'billing@example.invalid', displayName: 'Billing' },
            { role: 'To', address: 'reader@example.invalid', displayName: null },
        ]);
        expect(message?.attachments).toEqual([
            {
                position: 0,
                fileName: 'invoice.pdf',
                wasFileNameNormalized: false,
                mediaType: 'application/pdf',
                sizeOctets: 2_048,
            },
        ]);
    });

    it('reads the evidence the verdict was reached from beside the two outcomes it is published with', async () => {
        const message = await readingOf(described());

        expect(message?.sender).toEqual({
            ...verdict,
            authorRoute: null,
            trustEvidence: null,
            trustContact: null,
            addToContacts: null,
        });
    });

    it('reads a message nothing authenticated as one naming no domain', async () => {
        const message = await readingOf(
            described({
                sender: {
                    ...verdict,
                    authorAuthentication: 'NotEstablished',
                    deploymentTrust: 'Unknown',
                    authenticatedDomain: null,
                    dkimDomain: null,
                    spfDomain: null,
                    dmarc: 'NotReported',
                    trustSource: null,
                },
            }),
        );

        expect(message?.sender).toMatchObject({ authenticatedDomain: null, dkimDomain: null, spfDomain: null });
    });

    it('reads a verdict MailFathom reached by verifying the signatures itself as one it checked locally', async () => {
        const message = await readingOf(
            described({ sender: { ...verdict, authenticationSource: 'LocalVerification', spfDomain: null } }),
        );

        expect(message?.sender.authenticationSource).toBe('LocalVerification');
    });

    it.each([
        ['states nothing recognized the author', null],
        ['leaves out what recognized the author', undefined],
    ])('reads a verdict that %s as one with no trust source', async (_shape, trustSource) => {
        const message = await readingOf(described({ sender: { ...verdict, deploymentTrust: 'Unknown', trustSource } }));

        expect(message?.sender.trustSource).toBeNull();
    });

    it('reads a sender known by their address alone, under the source that knew them', async () => {
        const message = await readingOf(
            described({
                sender: {
                    ...verdict,
                    authorAuthentication: 'Failed',
                    deploymentTrust: 'Known',
                    trustSource: 'PersonalContact',
                },
            }),
        );

        expect(message?.sender).toMatchObject({ deploymentTrust: 'Known', trustSource: 'PersonalContact' });
    });

    it.each(['OwnAccountDomain', 'OrganizationMember', 'OwnAccountAddress', 'OrganizationContact', 'PersonalContact'])(
        'reads a sender %s recognized',
        async (trustSource) => {
            const message = await readingOf(described({ sender: { ...verdict, trustSource } }));

            expect(message?.sender.trustSource).toBe(trustSource);
        },
    );

    it.each([
        ['its organization by the name it carries now', 'Nordwind', 'Nordwind'],
        ['no organization where it has since been deleted', null, null],
        ['no organization where the verdict leaves it out', undefined, null],
    ])('reads an organization member with %s', async (_shape, trustOrganization, read) => {
        const message = await readingOf(
            described({ sender: { ...verdict, trustSource: 'OrganizationMember', trustOrganization } }),
        );

        expect(message?.sender).toMatchObject({ trustSource: 'OrganizationMember', trustOrganization: read });
    });

    it('reads what established the author, what tied it to the contact, and whose contact that is', async () => {
        const message = await readingOf(
            described({
                sender: {
                    ...verdict,
                    authorRoute: 'AlignedDkim',
                    trustEvidence: 'AuthorSignature',
                    trustContact: 'AnotherUser',
                },
            }),
        );

        expect(message?.sender).toMatchObject({
            authorRoute: 'AlignedDkim',
            trustEvidence: 'AuthorSignature',
            trustContact: 'AnotherUser',
        });
    });

    it('reads a verdict that leaves the route, the evidence, the holder and the offer out as none of them', async () => {
        const message = await readingOf(described({ sender: verdict }));

        expect(message?.sender).toMatchObject({
            authorRoute: null,
            trustEvidence: null,
            trustContact: null,
            addToContacts: null,
        });
    });

    it.each([
        ['a contact recorded afresh', { restatedContactId: null }, { restatedContactId: null }],
        ['a contact whose address is stated again', { restatedContactId: messageId }, { restatedContactId: messageId }],
    ])('reads an offer to add the author as %s', async (_shape, addToContacts, read) => {
        const message = await readingOf(described({ sender: { ...verdict, addToContacts } }));

        expect(message?.sender.addToContacts).toEqual(read);
    });

    it('reads a signed message’s signature whole, with every step and the signer it names', async () => {
        const message = await readingOf(described({ signature: verified }));

        expect(message?.signature).toEqual(verified);
    });

    it('reads a message carrying no signature as one that is not signed and names no signer', async () => {
        const message = await readingOf(described());

        expect(message?.signature).toMatchObject({ outcome: 'NotSigned', signer: null });
    });

    it('reads a signer whose material names nothing as one with every field empty', async () => {
        const message = await readingOf(described({ signature: { ...verified, signer: {} } }));

        expect(message?.signature.signer).toEqual(Object.fromEntries(Object.keys(signer).map((key) => [key, null])));
    });

    it('reads the photo its author is drawn by, and a message naming none as drawn by initials', async () => {
        const senderPhoto = { book: 'Member', id: '0198f4a1-2b6c-7a1d-9f3e-4c5d6e7f8a90', photo: 'revision-1' };

        const drawn = await readingOf(described({ senderPhoto }));
        const initials = await readingOf(described());

        expect([drawn?.senderPhoto, initials?.senderPhoto]).toEqual([senderPhoto, null]);
    });

    it('reads a message whose parts nothing has ever read as one carrying no counts', async () => {
        const message = await readingOf(described({ carried: null }));

        expect(message?.carried).toBeNull();
    });

    it.each([401, 403, 404, 500, 503])(
        'reports a status of %i as a failure rather than reading the answer',
        async (status) => {
            const result = await readMailMessage(session, answering({ status, body: '' }), messageId);

            expect(result.outcome).toBe('failed');
        },
    );

    // The one status on this route that says something a screen can act on rather than retry. It is separated because
    // a client that could not tell it from a deployment that is down would either offer a retry that cannot answer, or
    // close what a reader had open every time their deployment blinked.
    it.each<{ status: number; reason: string }>([
        { status: 404, reason: 'missing' },
        { status: 401, reason: 'unauthenticated' },
        { status: 403, reason: 'unauthorized' },
        { status: 503, reason: 'unavailable' },
    ])('reports a status of $status as $reason', async ({ status, reason }) => {
        const result = await readMailMessage(session, answering({ status, body: '' }), messageId);

        expect(result).toEqual({ outcome: 'failed', failure: { reason, status } });
    });

    it('reports a deployment that answered nothing at all as unavailable', async () => {
        const result = await readMailMessage(
            session,
            () => Promise.reject(new Error('the connection was refused')),
            messageId,
        );

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unavailable', status: null } });
    });

    it.each([
        ['a body that is not JSON at all', 'not json'],
        ['a body that is an array rather than a message', '[]'],
        ['a message with no headers block', described({ headers: undefined })],
        ['a subject that is not text', described({ headers: { subject: 7 } })],
        ['a participant in a role this build does not publish', headersNaming({ role: 'Approver' })],
        ['a participant with no address', headersNaming({ role: 'To', displayName: 'Nobody' })],
        ['an authentication outcome this build does not publish', senderWith({ authorAuthentication: 'Probably' })],
        ['a trust level this build does not publish', senderWith({ deploymentTrust: 'Suspect' })],
        ['a verdict that does not say who checked', senderWith({ authenticationSource: undefined })],
        ['a checker this build does not publish', senderWith({ authenticationSource: 'Recipient' })],
        ['a DKIM domain that is not text', senderWith({ dkimDomain: 7 })],
        ['an SPF domain that is not text', senderWith({ spfDomain: ['BOUNCES.EXAMPLE.INVALID'] })],
        ['a verdict with no DMARC result', senderWith({ dmarc: undefined })],
        ['a DMARC result this build does not publish', senderWith({ dmarc: 'Quarantine' })],
        ['a trust source this build does not publish', senderWith({ trustSource: 'Address' })],
        [
            'an organization name that is not text',
            senderWith({ trustSource: 'OrganizationMember', trustOrganization: ['Nordwind'] }),
        ],
        ['an organization named beside another trust source', senderWith({ trustOrganization: 'Nordwind' })],
        ['an author route this build does not publish', senderWith({ authorRoute: 'Spf' })],
        ['trust evidence this build does not publish', senderWith({ trustEvidence: 'Signature' })],
        ['a contact holder this build does not publish', senderWith({ trustContact: 'Mine' })],
        [
            'a contact holder beside another trust source',
            senderWith({ trustSource: 'OrganizationContact', trustContact: 'Reader' }),
        ],
        ['an offer that is not a record', senderWith({ addToContacts: true })],
        ['an offer naming a contact that is not text', senderWith({ addToContacts: { restatedContactId: 7 } })],
        ['a message saying nothing about its signature', described({ signature: undefined })],
        ['a signature that is not a record', described({ signature: 'Verified' })],
        ['a signature outcome this build does not publish', signatureWith({ outcome: 'Forged' })],
        ['a signer match that is not a boolean', signatureWith({ signerMatchesAuthor: null })],
        ['a signature format this build does not publish', signatureWith({ format: 'Pkcs7' })],
        ['an S/MIME type this build does not publish', signatureWith({ secureMimeType: 'Signed' })],
        ['a reason this build does not publish', signatureWith({ unverifiableReason: 'Untrusted' })],
        ['a validity failure this build does not publish', signatureWith({ validityFailure: 'Expired' })],
        ['a trust anchor this build does not publish', signatureWith({ trustAnchor: 'Pinned' })],
        ['a revocation method this build does not publish', signatureWith({ revocationMethod: 'Stapled' })],
        ['a signing time that is not text', signatureWith({ signedAt: 1_756_632_058 })],
        ['a signature with no steps', signatureWith({ steps: undefined })],
        ['a step result this build does not publish', signatureWith({ steps: { ...verified.steps, validity: 'Ok' } })],
        ['a step left out', signatureWith({ steps: { ...verified.steps, revocation: undefined } })],
        ['a signer that is not a record', signatureWith({ signer: 'Billing' })],
        ['a signer field that is not text', signatureWith({ signer: { ...signer, fingerprint: 42 } })],
        ['an attachment at a negative position', attachmentOf({ position: -1 })],
        ['an attachment with a fractional size', attachmentOf({ sizeOctets: 1.5 })],
        ['an attachment declaring no media type', attachmentOf({ mediaType: null })],
        ['a carried block with a count that is not a number', described({ carried: { attachmentCount: 'two' } })],
        [
            'a sender photo naming a book that carries none',
            described({ senderPhoto: { book: 'Collected', id: 'x', photo: 'y' } }),
        ],
        ['more attachments than a message may carry', tooManyAttachments()],
    ])('refuses %s rather than reading part of it', async (_shape, body) => {
        const result = await readMailMessage(session, answering({ status: 200, body }), messageId);

        expect(result).toEqual({ outcome: 'failed', failure: { reason: 'unreadable', status: 200 } });
    });
});

/** A message whose sender verdict differs from the well-formed one only in what is named. */
function senderWith(fields: Readonly<Record<string, unknown>>): string {
    return described({ sender: { ...verdict, ...fields } });
}

/** A message whose signature differs from a verified one only in what is named. */
function signatureWith(fields: Readonly<Record<string, unknown>>): string {
    return described({ signature: { ...verified, ...fields } });
}

/** A message whose only participant is the one named, which is how a malformed address is put in front of the parser. */
function headersNaming(participant: Readonly<Record<string, unknown>>): string {
    return described({
        headers: {
            subject: 'Quarterly invoice',
            sentAt: null,
            receivedAt: null,
            participants: [participant],
            messageId: null,
            inReplyTo: null,
            references: [],
        },
    });
}

/** A message whose only attachment is the one named, with the rest of a well-formed description around it. */
function attachmentOf(attachment: Readonly<Record<string, unknown>>): string {
    return described({
        attachments: [
            {
                position: 0,
                fileName: 'invoice.pdf',
                wasFileNameNormalized: false,
                mediaType: 'application/pdf',
                sizeOctets: 2_048,
                ...attachment,
            },
        ],
    });
}

// The bound is checked while the list is walked rather than after it, so the case worth proving is an answer larger than
// anything the service composes rather than one merely longer than a reader would open.
function tooManyAttachments(): string {
    return described({
        attachments: Array.from({ length: 1_025 }, (_unused, position) => ({
            position,
            fileName: 'invoice.pdf',
            wasFileNameNormalized: false,
            mediaType: 'application/pdf',
            sizeOctets: 2_048,
        })),
    });
}
