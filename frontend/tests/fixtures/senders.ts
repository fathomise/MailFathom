// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// What the service established about who sent each message of the corpus, in the two shapes it publishes that in: the
// four facts every row carries, and the evidence the message route answers beside them. Both are stated from one kind
// per message, so a row and the message it opens never disagree about one sender.
//
// The kinds are the five marks a screen draws and the two ways a warning is reached, each on a message the first
// screen of the folder shows, so every mark can be looked at without scrolling. A member of the reader's organization
// is trusted twice over: once naming the organization, and once where it has since been deleted and no name is left.
// A sender known by their address alone is known twice as well: once where nothing established the author, and once
// where the domain's checks failed under a policy that only monitors.

import { markupOnlyId, newsletterId } from './messages';

/** Which of the sender states a message of the corpus is in. */
export type SenderKind =
    | 'trusted'
    | 'verified'
    | 'forgedSignature'
    | 'failedAuthentication'
    | 'unrecognized'
    | 'organizationMember'
    | 'deletedOrganizationMember'
    | 'known'
    | 'knownMonitoringOnly';

const kindsById: Readonly<Record<string, SenderKind>> = {
    [newsletterId]: 'trusted',
    [markupOnlyId]: 'unrecognized',
    'message-0': 'trusted',
    'message-1': 'verified',
    'message-2': 'forgedSignature',
    'message-4': 'failedAuthentication',
    'message-5': 'deletedOrganizationMember',
    'message-6': 'organizationMember',
    'message-3': 'known',
    'message-7': 'knownMonitoringOnly',
};

/** The sender state of the message the corpus holds under that identity; anything not named is an unrecognized one. */
export function senderKindOf(storedEmailId: string): SenderKind {
    return kindsById[storedEmailId] ?? 'unrecognized';
}

// The newsletter's own domain, as the service publishes a stored domain: normalized to upper case.
const authorDomain = 'EXAMPLE.INVALID';

const notSigned = {
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
    name: 'Example',
    address: 'news@example.invalid',
    issuer: 'Harbourline Public Email CA R4',
    validFrom: '2026-01-12T00:00:00+00:00',
    validUntil: '2028-01-11T23:59:59+00:00',
    serialNumber: '4F1C9A027ED35518',
    fingerprint: '9C41E70B225AD8136FA03E91B47C582D9C41E70B225AD8136FA03E91B47C582D',
    keyId: null,
    publicKeyAlgorithm: 'RSA',
    digestAlgorithm: 'SHA256',
};

const verifiedSignature = {
    ...notSigned,
    outcome: 'Verified',
    format: 'SecureMimeDetached',
    signedAt: '2026-08-31T09:40:12+00:00',
    steps: { content: 'Passed', authority: 'Passed', validity: 'Passed', revocation: 'Passed', signer: 'Passed' },
    trustAnchor: 'SystemTrustStore',
    chainEnd: 'Harbourline Public Email CA R4',
    revocationMethod: 'Ocsp',
    revocationCheckedAt: '2026-08-31T09:41:10+00:00',
    signerMatchesAuthor: true,
    signer,
};

const failedSignature = {
    ...verifiedSignature,
    outcome: 'Failed',
    format: 'SecureMimeOpaque',
    secureMimeType: 'SignedData',
    steps: { content: 'Failed', authority: 'Passed', validity: 'Passed', revocation: 'Passed', signer: 'Passed' },
};

const authenticated = {
    authorAuthentication: 'Authenticated',
    deploymentTrust: 'Unknown',
    authenticatedDomain: authorDomain,
    authenticationSource: 'ReceivingServer',
    dkimDomain: authorDomain,
    spfDomain: authorDomain,
    dmarc: 'Pass',
    trustSource: null,
    trustOrganization: null,
    authorRoute: 'DmarcPass',
    trustEvidence: null,
    trustContact: null,
    addToContacts: null,
};

const trusted = {
    ...authenticated,
    deploymentTrust: 'Trusted',
    trustSource: 'PersonalContact',
    trustEvidence: 'Authentication',
    trustContact: 'Reader',
};

// The organization's signature proves the address itself, so the member whose organization was deleted is one no
// receiving server established anything about and is trusted all the same.
const organizationMember = {
    ...authenticated,
    deploymentTrust: 'Trusted',
    trustSource: 'OrganizationMember',
    trustOrganization: 'Nordwind',
    trustEvidence: 'Authentication',
};
const deletedOrganizationMember = {
    ...organizationMember,
    authorAuthentication: 'NotEstablished',
    authenticatedDomain: null,
    dkimDomain: null,
    spfDomain: null,
    dmarc: 'NotReported',
    trustOrganization: null,
    authorRoute: null,
    trustEvidence: 'AuthorSignature',
};

// Known by the address alone: a signature the sender's invoicing service made in its own name, which aligns with nothing.
const knownSender = {
    ...authenticated,
    authorAuthentication: 'NotEstablished',
    deploymentTrust: 'Known',
    authenticatedDomain: null,
    dkimDomain: 'INVOICING.EXAMPLE.INVALID',
    spfDomain: 'INVOICING.EXAMPLE.INVALID',
    dmarc: 'NotReported',
    trustSource: 'PersonalContact',
    authorRoute: null,
    trustContact: 'Reader',
};
const knownMonitoredSender = {
    ...knownSender,
    authorAuthentication: 'Failed',
    authenticatedDomain: 'INVOICING.EXAMPLE.INVALID',
    dmarc: 'Fail',
    trustSource: 'OrganizationContact',
    trustContact: null,
};

const evidence = {
    trusted: { sender: trusted, signature: notSigned },
    verified: { sender: authenticated, signature: verifiedSignature },
    forgedSignature: { sender: trusted, signature: failedSignature },
    failedAuthentication: {
        sender: {
            ...authenticated,
            authorAuthentication: 'Failed',
            authenticatedDomain: 'LISTS.EXAMPLE.INVALID',
            dkimDomain: null,
            spfDomain: 'LISTS.EXAMPLE.INVALID',
            dmarc: 'Fail',
            authorRoute: null,
            addToContacts: { restatedContactId: null },
        },
        signature: notSigned,
    },
    // The details offer to write an author nobody recognizes down as a new contact of the reader's own.
    unrecognized: { sender: { ...authenticated, addToContacts: { restatedContactId: null } }, signature: notSigned },
    organizationMember: { sender: organizationMember, signature: notSigned },
    deletedOrganizationMember: { sender: deletedOrganizationMember, signature: notSigned },
    known: { sender: knownSender, signature: notSigned },
    knownMonitoringOnly: { sender: knownMonitoredSender, signature: notSigned },
} as const;

/** What the message route answers about who sent a message of that kind: its `sender` and its `signature`. */
export function messageSenderEvidence(kind: SenderKind) {
    return evidence[kind];
}

/** What a row of a message of that kind publishes about its sender: the four facts a mark is chosen from. */
export function listedSender(kind: SenderKind) {
    const { sender, signature } = evidence[kind];

    return {
        authorAuthentication: sender.authorAuthentication,
        deploymentTrust: sender.deploymentTrust,
        signatureOutcome: signature.outcome,
        signerMatchesAuthor: signature.signerMatchesAuthor,
    };
}
