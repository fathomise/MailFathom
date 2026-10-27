// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type { MailMessage, MailSenderVerdict, MailSignature } from '@mailfathom/client-backend';
import { senderVerificationOf, type VerificationStep } from './verificationSteps';

const unsigned: MailSignature = {
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

const signedByAuthor: MailSignature = {
    ...unsigned,
    outcome: 'Verified',
    format: 'SecureMimeDetached',
    signedAt: '2026-08-27T09:13:00+00:00',
    steps: { content: 'Passed', authority: 'Passed', validity: 'Passed', revocation: 'Passed', signer: 'Passed' },
    trustAnchor: 'SystemTrustStore',
    chainEnd: 'Harbourline Public Email CA R4',
    revocationMethod: 'Ocsp',
    revocationCheckedAt: '2026-08-27T09:14:00+00:00',
    signerMatchesAuthor: true,
    signer: {
        name: 'Anna Kowalska',
        address: 'a.kowalska@contoso.example',
        issuer: 'Harbourline Public Email CA R4',
        validFrom: null,
        validUntil: null,
        serialNumber: '4F1C9A027ED35518',
        fingerprint: null,
        keyId: null,
        publicKeyAlgorithm: 'RSA',
        digestAlgorithm: 'SHA256',
    },
};

const authenticated: MailSenderVerdict = {
    authorAuthentication: 'Authenticated',
    deploymentTrust: 'Unknown',
    authenticatedDomain: 'CONTOSO.EXAMPLE',
    authenticationSource: 'ReceivingServer',
    dkimDomain: 'CONTOSO.EXAMPLE',
    spfDomain: 'CONTOSO.EXAMPLE',
    dmarc: 'Pass',
    trustSource: null,
    trustOrganization: null,
};

function message(sender: MailSenderVerdict, signature: MailSignature = unsigned): MailMessage {
    return {
        storedEmailId: 'message-1',
        account: 'account-1',
        folder: 'folder-1',
        threadId: null,
        sizeOctets: 1024,
        headers: {
            subject: 'Invoice',
            sentAt: null,
            receivedAt: null,
            participants: [{ role: 'From', address: 'a.kowalska@contoso.example', displayName: 'Anna Kowalska' }],
            messageId: null,
            inReplyTo: null,
            references: [],
        },
        body: { availability: 'Readable', plainText: true, html: false },
        sender,
        signature,
        attachments: [],
        carried: null,
        unread: false,
        flagged: false,
        answered: false,
        spam: null,
    };
}

// Words an instant by repeating it, so a test reads which instant a sentence was filled from.
const asSent = (instant: string | null) => instant;

function stepsOf(read: MailMessage): readonly VerificationStep[] {
    return senderVerificationOf(read, asSent).groups.flatMap((group) => group.steps);
}

function stepNamed(read: MailMessage, name: VerificationStep['name']): VerificationStep | undefined {
    return stepsOf(read).find((step) => step.name === name);
}

describe('senderVerificationOf', () => {
    it('lists sender authentication, trust and the signature as three groups, in that order', () => {
        expect(senderVerificationOf(message(authenticated), asSent).groups.map((group) => group.name)).toEqual([
            'senderMark.group.authentication',
            'senderMark.group.trust',
            'senderMark.group.signature',
        ]);
    });

    it('says the domains a check passed for the way addresses are written rather than as stored', () => {
        expect(stepNamed(message(authenticated), 'senderMark.step.dkim')?.detail).toEqual({
            key: 'senderMark.dkim.passed',
            values: { domain: 'contoso.example' },
        });
    });

    it('marks every authentication step not checked when nobody checked the message', () => {
        const nobody = message({
            ...authenticated,
            authorAuthentication: 'NotEstablished',
            authenticatedDomain: null,
            dkimDomain: null,
            spfDomain: null,
            dmarc: 'NotReported',
        });

        expect(senderVerificationOf(nobody, asSent).groups[0]?.steps.map((step) => step.result)).toEqual([
            'NotChecked',
            'NotChecked',
            'NotChecked',
            'NotChecked',
            'NotChecked',
        ]);
        expect(stepNamed(nobody, 'senderMark.step.whoChecked')?.detail.key).toBe('senderMark.checked.nobody');
    });

    it('says SPF and DMARC were not checked where MailFathom checked the DKIM signature itself', () => {
        const locally = message({ ...authenticated, authenticationSource: 'LocalVerification', spfDomain: null });

        expect(stepNamed(locally, 'senderMark.step.whoChecked')?.detail.key).toBe('senderMark.checked.locally');
        expect(stepNamed(locally, 'senderMark.step.spf')).toMatchObject({ result: 'NotChecked' });
        expect(stepNamed(locally, 'senderMark.step.dmarc')).toMatchObject({ result: 'NotChecked' });
    });

    it.each([
        ['Fail', 'Failed', 'senderMark.dmarc.fail'],
        ['NoPolicyPublished', 'NotChecked', 'senderMark.dmarc.noPolicy'],
        ['TemporaryError', 'NotChecked', 'senderMark.dmarc.temporaryError'],
        ['PermanentError', 'NotChecked', 'senderMark.dmarc.permanentError'],
        ['NotReported', 'NotChecked', 'senderMark.dmarc.notReported'],
    ] as const)('reads a DMARC result of %s as %s', (dmarc, result, key) => {
        expect(stepNamed(message({ ...authenticated, dmarc }), 'senderMark.step.dmarc')).toMatchObject({
            result,
            detail: { key },
        });
    });

    it('names the domain that authenticated a message whose author did not hold', () => {
        const failed = message({
            ...authenticated,
            authorAuthentication: 'Failed',
            authenticatedDomain: 'LISTS.PARTNERS.EXAMPLE',
        });

        expect(stepNamed(failed, 'senderMark.step.authorDomain')).toEqual({
            name: 'senderMark.step.authorDomain',
            result: 'Failed',
            detail: {
                key: 'senderMark.author.failedInstead',
                values: { domain: 'contoso.example', authenticated: 'lists.partners.example' },
            },
            facts: null,
        });
    });

    // No SPF domain is published both where a check failed and where none was evaluated, so the step names no cause.
    it('says no SPF check passed, and nothing about why, where none is published as passing', () => {
        const spf = stepNamed(message({ ...authenticated, spfDomain: null }), 'senderMark.step.spf');

        expect(spf?.result).toBe('Failed');
        expect(spf?.detail).toEqual({ key: 'senderMark.spf.failed' });
    });

    it('skips the trust step where the author was not authenticated', () => {
        expect(
            stepNamed(message({ ...authenticated, authorAuthentication: 'Failed' }), 'senderMark.step.trustedSender'),
        ).toMatchObject({ result: 'Skipped' });
    });

    it('says what recognized a trusted author, and never which entry matched, since that is not recorded', () => {
        const trusted = message({ ...authenticated, deploymentTrust: 'Trusted', trustSource: 'StoredTrustedSender' });

        expect(senderVerificationOf(trusted, asSent).why).toEqual({
            key: 'senderMark.why.trustedSender',
            values: { authenticated: 'contoso.example' },
        });
        expect(stepNamed(trusted, 'senderMark.step.trustedSender')?.detail.key).toBe('senderMark.trust.trustedSenders');
    });

    it('says a domain of the reader’s own accounts recognized an author it did', () => {
        const own = message({ ...authenticated, deploymentTrust: 'Trusted', trustSource: 'OwnAccountDomain' });

        expect(stepNamed(own, 'senderMark.step.trustedSender')?.detail).toEqual({
            key: 'senderMark.trust.ownDomain',
            values: { domain: 'contoso.example' },
        });
    });

    it('says one of the reader’s own addresses recognized an author who wrote as it', () => {
        const own = message({ ...authenticated, deploymentTrust: 'Trusted', trustSource: 'OwnAccountAddress' });

        expect(senderVerificationOf(own, asSent).why).toEqual({
            key: 'senderMark.why.trustedOwnAddress',
            values: { authenticated: 'contoso.example', address: 'a.kowalska@contoso.example' },
        });
        expect(stepNamed(own, 'senderMark.step.trustedSender')).toMatchObject({
            result: 'Passed',
            detail: { key: 'senderMark.trust.ownAddress', values: { address: 'a.kowalska@contoso.example' } },
        });
    });

    it('fails the trust step for an authenticated author nothing recognized, naming the address alone', () => {
        const stranger = message({ ...authenticated, deploymentTrust: 'Unknown', trustSource: null });

        expect(stepNamed(stranger, 'senderMark.step.trustedSender')).toMatchObject({
            result: 'Failed',
            detail: { key: 'senderMark.trust.unrecognized', values: { address: 'a.kowalska@contoso.example' } },
        });
    });

    // The organization's signature proves the address rather than a domain, so it holds where no author was established.
    it('passes the trust step for a member of the reader’s organization, naming it, whatever sender authentication found', () => {
        const member = message({
            ...authenticated,
            authorAuthentication: 'NotEstablished',
            authenticatedDomain: null,
            deploymentTrust: 'Trusted',
            trustSource: 'OrganizationMember',
            trustOrganization: 'Nordwind',
        });

        expect(senderVerificationOf(member, asSent).why).toEqual({
            key: 'senderMark.why.trustedOrganizationMember',
            values: { organization: 'Nordwind' },
        });
        expect(stepNamed(member, 'senderMark.step.trustedSender')).toMatchObject({
            result: 'Passed',
            detail: {
                key: 'senderMark.trust.organizationMember',
                values: { organization: 'Nordwind', address: 'a.kowalska@contoso.example' },
            },
        });
    });

    it('passes the trust step for a member of an organization deleted since, saying why it names none', () => {
        const member = message({ ...authenticated, deploymentTrust: 'Trusted', trustSource: 'OrganizationMember' });

        expect(senderVerificationOf(member, asSent).why).toEqual({
            key: 'senderMark.why.trustedOrganizationMemberDeleted',
        });
        expect(stepNamed(member, 'senderMark.step.trustedSender')).toMatchObject({
            result: 'Passed',
            detail: {
                key: 'senderMark.trust.organizationMemberDeleted',
                values: { address: 'a.kowalska@contoso.example' },
            },
        });
    });

    it('lists a single skipped step for a message that carries no signature', () => {
        expect(senderVerificationOf(message(authenticated), asSent).groups[2]?.steps).toEqual([
            {
                name: 'senderMark.step.signed',
                result: 'Skipped',
                detail: { key: 'senderMark.signed.not' },
                facts: null,
            },
        ]);
    });

    it('lists each step of a verified certificate, with the certificate’s details on the authority step', () => {
        const steps = senderVerificationOf(message(authenticated, signedByAuthor), asSent).groups[2]?.steps ?? [];

        expect(steps.map((step) => [step.name, step.result])).toEqual([
            ['senderMark.step.signed', 'Passed'],
            ['senderMark.step.content', 'Passed'],
            ['senderMark.step.authority', 'Passed'],
            ['senderMark.step.validity', 'Passed'],
            ['senderMark.step.revocation', 'Passed'],
            ['senderMark.step.signer', 'Passed'],
        ]);
        expect(steps[0]?.detail).toEqual({
            key: 'senderMark.signed.detachedAt',
            values: { type: '-', when: '2026-08-27T09:13:00+00:00' },
        });
        expect(steps[2]?.facts).toEqual([
            { label: 'senderMark.fact.name', value: 'Anna Kowalska' },
            { label: 'senderMark.fact.email', value: 'a.kowalska@contoso.example' },
            { label: 'senderMark.fact.issuer', value: 'Harbourline Public Email CA R4' },
            { label: 'senderMark.fact.serial', value: '4F1C9A027ED35518' },
            { label: 'senderMark.fact.algorithms', value: 'RSA · SHA256' },
        ]);
        expect(steps[4]?.detail).toEqual({
            key: 'senderMark.revocation.ocsp',
            values: { when: '2026-08-27T09:14:00+00:00' },
        });
    });

    it('explains a verified signature beside failed authentication as a forward would read', () => {
        const forwarded = message({ ...authenticated, authorAuthentication: 'Failed' }, signedByAuthor);

        expect(senderVerificationOf(forwarded, asSent)).toMatchObject({
            mark: 'verified',
            why: { key: 'senderMark.why.verifiedDespiteAuthentication' },
        });
    });

    it('warns about a signature that does not match its content, even from a trusted sender', () => {
        const forged = message(
            { ...authenticated, deploymentTrust: 'Trusted', trustSource: 'ConfiguredTrustedSender' },
            { ...signedByAuthor, outcome: 'Failed', steps: { ...signedByAuthor.steps, content: 'Failed' } },
        );

        expect(senderVerificationOf(forged, asSent)).toMatchObject({
            mark: 'signatureMismatch',
            why: { key: 'senderMark.why.signatureMismatch' },
        });
        expect(stepNamed(forged, 'senderMark.step.content')).toMatchObject({ result: 'Failed' });
    });

    it('names who signed where a verified signature is not the author’s', () => {
        const another = message(authenticated, {
            ...signedByAuthor,
            signerMatchesAuthor: false,
            steps: { ...signedByAuthor.steps, signer: 'Failed' },
            signer:
                signedByAuthor.signer === null
                    ? null
                    : { ...signedByAuthor.signer, address: 'hr-system@contoso.example' },
        });

        expect(senderVerificationOf(another, asSent).why).toEqual({
            key: 'senderMark.why.signedByAnother',
            values: { signer: 'hr-system@contoso.example' },
        });
        expect(stepNamed(another, 'senderMark.step.signer')?.detail).toEqual({
            key: 'senderMark.signer.certificateOther',
            values: { address: 'a.kowalska@contoso.example', signer: 'hr-system@contoso.example' },
        });
    });

    it('skips what follows an untrusted chain and says why', () => {
        const untrusted = message(authenticated, {
            ...signedByAuthor,
            outcome: 'NotVerifiable',
            unverifiableReason: 'UntrustedRoot',
            chainEnd: 'Wrona Law Office Internal CA',
            steps: { ...signedByAuthor.steps, authority: 'Failed', revocation: 'Skipped' },
        });

        expect(stepNamed(untrusted, 'senderMark.step.authority')?.detail).toEqual({
            key: 'senderMark.authority.failed',
            values: { authority: 'Wrona Law Office Internal CA' },
        });
        expect(stepNamed(untrusted, 'senderMark.step.revocation')?.detail.key).toBe('senderMark.skipped.noAuthority');
    });

    it('reads an OpenPGP signature as a key rather than a certificate', () => {
        const openPgp = message(authenticated, {
            ...signedByAuthor,
            format: 'OpenPgpMime',
            trustAnchor: 'ConfiguredKey',
            steps: { ...signedByAuthor.steps, validity: 'Failed' },
            validityFailure: 'ExpiredKey',
            signer:
                signedByAuthor.signer === null
                    ? null
                    : { ...signedByAuthor.signer, keyId: '91F76A03D5E82C19', validUntil: '2026-06-30T00:00:00+00:00' },
        });
        const verification = senderVerificationOf(openPgp, asSent);

        expect(verification.keyDetails).toBe(true);
        expect(stepNamed(openPgp, 'senderMark.step.knownKey')?.detail).toEqual({
            key: 'senderMark.key.known',
            values: { keyId: '91F76A03D5E82C19' },
        });
        expect(stepNamed(openPgp, 'senderMark.step.keyValidity')?.detail).toEqual({
            key: 'senderMark.keyValidity.expired',
            values: { when: '2026-06-30T00:00:00+00:00' },
        });
    });
});
