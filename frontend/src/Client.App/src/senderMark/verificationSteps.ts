// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type {
    MailMessage,
    MailSecureMimeType,
    MailSenderVerdict,
    MailSignature,
    MailSignatureStepResult,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import { listedSenderOf, senderMarkOf, type SenderMark } from './senderMarks';

// What the sender details list: why the mark is the mark, then every step that was taken to reach it — sender
// authentication, trust, and the message's own signature — each with its result and, where it was not reached, the
// reason. It is arithmetic over what the message route answered and nothing else, so every sentence here is one the
// service's facts support: no step claims more than was recorded, and none is left out because it did not pass.
//
// Two things the design names are not recorded, and the sentences say what is rather than inventing them: which
// receiving mail server checked a message, and which address or domain on the trusted senders matched its author.

/** One sentence as the catalogue holds it, with the values its holes are filled from. */
export interface Said {
    readonly key: MessageKey;
    readonly values?: Readonly<Record<string, string>>;
}

/** One line of a certificate's or a key's details: what it is, and the value the signer's material carries. */
export interface SignerFact {
    readonly label: MessageKey;
    readonly value: string;
}

/** What one step found, or why it was not reached. */
export interface VerificationStep {
    readonly name: MessageKey;
    readonly result: MailSignatureStepResult;
    readonly detail: Said;

    /** The certificate's or the key's own details, where the step has them to show; `null` where it has none. */
    readonly facts: readonly SignerFact[] | null;
}

export interface VerificationGroup {
    readonly name: MessageKey;
    readonly steps: readonly VerificationStep[];
}

export interface SenderVerification {
    readonly mark: SenderMark;

    /** Who the message says it is from, which the details name under their title. */
    readonly author: string | null;
    readonly why: Said;
    readonly groups: readonly VerificationGroup[];

    /** Whether the signer's details are a key's rather than a certificate's, which decides what their toggle says. */
    readonly keyDetails: boolean;
}

/** Words an instant as the reader's clock does, or answers `null` where it is not one. */
export type WordInstant = (instant: string | null) => string | null;

// The `smime-type` parameter as a message declares it, which is a protocol token rather than a sentence.
const secureMimeTokens: Readonly<Record<MailSecureMimeType, string>> = {
    SignedData: 'signed-data',
    EnvelopedData: 'enveloped-data',
    AuthEnvelopedData: 'authEnveloped-data',
    CompressedData: 'compressed-data',
    CertsOnly: 'certs-only',
    NotDeclared: '-',
};

function step(
    name: MessageKey,
    result: MailSignatureStepResult,
    key: MessageKey,
    values?: Readonly<Record<string, string>>,
    facts: readonly SignerFact[] | null = null,
): VerificationStep {
    return { name, result, detail: values === undefined ? { key } : { key, values }, facts };
}

// A domain as the service stores it is normalized to upper case, which reads as shouting; a domain is
// case-insensitive, so it is said the way addresses are written.
function spoken(domain: string): string {
    return domain.toLowerCase();
}

function domainOf(address: string | null): string | null {
    if (address === null) {
        return null;
    }

    const at = address.lastIndexOf('@');

    return at < 0 ? null : spoken(address.slice(at + 1));
}

/** Everything the details say about one message's sender. */
export function senderVerificationOf(message: MailMessage, wordAt: WordInstant): SenderVerification {
    const author = message.headers.participants.find((participant) => participant.role === 'From')?.address ?? null;
    const mark = senderMarkOf(listedSenderOf(message));
    const named = { address: author ?? '-', domain: domainOf(author) ?? '-' };

    return {
        mark,
        author,
        why: whyOf(mark, message.sender, message.signature, named),
        groups: [
            { name: 'senderMark.group.authentication', steps: authenticationSteps(message.sender, named.domain) },
            { name: 'senderMark.group.trust', steps: [trustStep(message.sender, named)] },
            { name: 'senderMark.group.signature', steps: signatureSteps(message.signature, named.address, wordAt) },
        ],
        keyDetails: message.signature.format === 'OpenPgpMime',
    };
}

interface Named {
    readonly address: string;
    readonly domain: string;
}

function whyOf(mark: SenderMark, sender: MailSenderVerdict, signature: MailSignature, named: Named): Said {
    const authenticated = sender.authenticatedDomain === null ? named.domain : spoken(sender.authenticatedDomain);

    switch (mark) {
        case 'signatureMismatch':
            return { key: 'senderMark.why.signatureMismatch' };
        case 'authenticationFailed':
            return { key: 'senderMark.why.authenticationFailed', values: { domain: named.domain } };
        case 'trusted':
            return sender.trustSource === 'OwnAccountDomain'
                ? { key: 'senderMark.why.trustedOwnDomain', values: { authenticated, domain: named.domain } }
                : { key: 'senderMark.why.trustedSender', values: { authenticated } };
        case 'verified':
            return {
                key:
                    sender.authorAuthentication === 'Failed'
                        ? 'senderMark.why.verifiedDespiteAuthentication'
                        : 'senderMark.why.verified',
                values: { address: named.address },
            };
        case 'neutral':
            return neutralWhy(sender, signature, authenticated);
    }
}

function neutralWhy(sender: MailSenderVerdict, signature: MailSignature, authenticated: string): Said {
    if (signature.outcome !== 'NotSigned') {
        const signer = signature.signer?.address ?? null;

        return signature.outcome === 'Verified' && signer !== null
            ? { key: 'senderMark.why.signedByAnother', values: { signer } }
            : { key: 'senderMark.why.signedNotVerifiable' };
    }

    return sender.authorAuthentication === 'Authenticated'
        ? { key: 'senderMark.why.authenticatedUnrecognized', values: { authenticated } }
        : { key: 'senderMark.why.nothingEstablished' };
}

// Nobody checked a message when the reading that produced its verdict found nothing at all: the receiving server's
// header was absent, so nothing passed, nothing was reported, and no author was established. The service records
// which reading it took rather than that none was available, so this is the one place the client reads that case off
// what was recorded.
function nobodyChecked(sender: MailSenderVerdict): boolean {
    return (
        sender.authenticationSource === 'ReceivingServer' &&
        sender.authorAuthentication === 'NotEstablished' &&
        sender.dkimDomain === null &&
        sender.spfDomain === null &&
        sender.dmarc === 'NotReported'
    );
}

const dmarcSaid: Readonly<Record<MailSenderVerdict['dmarc'], readonly [MailSignatureStepResult, MessageKey]>> = {
    Pass: ['Passed', 'senderMark.dmarc.pass'],
    Fail: ['Failed', 'senderMark.dmarc.fail'],
    NoPolicyPublished: ['NotChecked', 'senderMark.dmarc.noPolicy'],
    TemporaryError: ['NotChecked', 'senderMark.dmarc.temporaryError'],
    PermanentError: ['NotChecked', 'senderMark.dmarc.permanentError'],
    NotReported: ['NotChecked', 'senderMark.dmarc.notReported'],
};

function authenticationSteps(sender: MailSenderVerdict, domain: string): readonly VerificationStep[] {
    const nobody = nobodyChecked(sender);
    const locally = sender.authenticationSource === 'LocalVerification';
    const notRun = step('senderMark.step.dkim', 'NotChecked', 'senderMark.notRun');

    if (nobody) {
        return [
            step('senderMark.step.whoChecked', 'NotChecked', 'senderMark.checked.nobody'),
            notRun,
            { ...notRun, name: 'senderMark.step.spf' },
            { ...notRun, name: 'senderMark.step.dmarc' },
            { ...notRun, name: 'senderMark.step.authorDomain' },
        ];
    }

    const [dmarcResult, dmarcKey] = dmarcSaid[sender.dmarc];

    return [
        locally
            ? step('senderMark.step.whoChecked', 'Passed', 'senderMark.checked.locally')
            : step('senderMark.step.whoChecked', 'Passed', 'senderMark.checked.receivingServer'),
        sender.dkimDomain === null
            ? step('senderMark.step.dkim', 'Failed', 'senderMark.dkim.failed')
            : step('senderMark.step.dkim', 'Passed', 'senderMark.dkim.passed', { domain: spoken(sender.dkimDomain) }),
        locally
            ? step('senderMark.step.spf', 'NotChecked', 'senderMark.spf.locally')
            : sender.spfDomain === null
              ? step('senderMark.step.spf', 'Failed', 'senderMark.spf.failed', { domain })
              : step('senderMark.step.spf', 'Passed', 'senderMark.spf.passed', { domain: spoken(sender.spfDomain) }),
        locally
            ? step('senderMark.step.dmarc', 'NotChecked', 'senderMark.dmarc.locally')
            : step('senderMark.step.dmarc', dmarcResult, dmarcKey, { domain }),
        authorStep(sender, domain),
    ];
}

function authorStep(sender: MailSenderVerdict, domain: string): VerificationStep {
    const authenticated = sender.authenticatedDomain === null ? null : spoken(sender.authenticatedDomain);

    switch (sender.authorAuthentication) {
        case 'Authenticated':
            return authenticated === null || authenticated === domain
                ? step('senderMark.step.authorDomain', 'Passed', 'senderMark.author.itself', { domain })
                : step('senderMark.step.authorDomain', 'Passed', 'senderMark.author.by', { domain, authenticated });
        case 'Failed':
            return authenticated === null
                ? step('senderMark.step.authorDomain', 'Failed', 'senderMark.author.failedNobody', { domain })
                : step('senderMark.step.authorDomain', 'Failed', 'senderMark.author.failedInstead', {
                      domain,
                      authenticated,
                  });
        case 'NotEstablished':
            return step('senderMark.step.authorDomain', 'NotChecked', 'senderMark.author.notEstablished', { domain });
    }
}

function trustStep(sender: MailSenderVerdict, named: Named): VerificationStep {
    if (sender.authorAuthentication !== 'Authenticated') {
        return step('senderMark.step.trustedSender', 'Skipped', 'senderMark.trust.skipped');
    }

    if (sender.deploymentTrust !== 'Trusted') {
        return step('senderMark.step.trustedSender', 'Failed', 'senderMark.trust.unrecognized', {
            address: named.address,
            domain: named.domain,
        });
    }

    return sender.trustSource === 'OwnAccountDomain'
        ? step('senderMark.step.trustedSender', 'Passed', 'senderMark.trust.ownDomain', { domain: named.domain })
        : step('senderMark.step.trustedSender', 'Passed', 'senderMark.trust.trustedSenders');
}

// What a step says when nothing more particular applies: one not reached because an earlier one did not pass, one the
// verification never got to, and a result this client has no more particular sentence for.
const genericSaid: Readonly<Record<MailSignatureStepResult, MessageKey>> = {
    Passed: 'senderMark.passed',
    Failed: 'senderMark.failed',
    NotChecked: 'senderMark.notChecked',
    Skipped: 'senderMark.skipped',
};

function generic(name: MessageKey, result: MailSignatureStepResult): VerificationStep {
    return step(name, result, genericSaid[result]);
}

const signedSaid: Readonly<
    Record<NonNullable<MailSignature['format']>, { readonly dated: MessageKey; readonly undated: MessageKey }>
> = {
    SecureMimeDetached: { dated: 'senderMark.signed.detachedAt', undated: 'senderMark.signed.detached' },
    SecureMimeOpaque: { dated: 'senderMark.signed.opaqueAt', undated: 'senderMark.signed.opaque' },
    OpenPgpMime: { dated: 'senderMark.signed.openPgpAt', undated: 'senderMark.signed.openPgp' },
};

function signatureSteps(signature: MailSignature, address: string, wordAt: WordInstant): readonly VerificationStep[] {
    if (signature.outcome === 'NotSigned' || signature.format === null) {
        return [step('senderMark.step.signed', 'Skipped', 'senderMark.signed.not')];
    }

    const when = wordAt(signature.signedAt);
    const said = signedSaid[signature.format];
    const type = secureMimeTokens[signature.secureMimeType ?? 'NotDeclared'];
    const signed =
        when === null
            ? step('senderMark.step.signed', 'Passed', said.undated, { type })
            : step('senderMark.step.signed', 'Passed', said.dated, { type, when });

    const content =
        signature.steps.content === 'Passed'
            ? step('senderMark.step.content', 'Passed', 'senderMark.content.passed')
            : signature.steps.content === 'Failed'
              ? step('senderMark.step.content', 'Failed', 'senderMark.content.failed')
              : step('senderMark.step.content', signature.steps.content, 'senderMark.content.notChecked');

    const rest =
        signature.format === 'OpenPgpMime'
            ? keySteps(signature, address, wordAt)
            : certificateSteps(signature, address, wordAt);

    return [signed, content, ...rest];
}

function signerFacts(signature: MailSignature, labels: 'certificate' | 'key', wordAt: WordInstant): SignerFact[] {
    const signer = signature.signer;

    if (signer === null) {
        return [];
    }

    const algorithms = [signer.publicKeyAlgorithm, signer.digestAlgorithm]
        .filter((value) => value !== null)
        .join(' · ');
    const userId =
        signer.name !== null && signer.address !== null
            ? `${signer.name} <${signer.address}>`
            : (signer.name ?? signer.address);

    const facts: readonly (readonly [MessageKey, string | null])[] =
        labels === 'key'
            ? [
                  ['senderMark.fact.fingerprint', signer.fingerprint],
                  ['senderMark.fact.keyId', signer.keyId],
                  ['senderMark.fact.userId', userId],
                  ['senderMark.fact.created', wordAt(signer.validFrom)],
                  ['senderMark.fact.expires', wordAt(signer.validUntil)],
                  ['senderMark.fact.algorithms', algorithms === '' ? null : algorithms],
              ]
            : [
                  ['senderMark.fact.name', signer.name],
                  ['senderMark.fact.email', signer.address],
                  ['senderMark.fact.issuer', signer.issuer],
                  ['senderMark.fact.validFrom', wordAt(signer.validFrom)],
                  ['senderMark.fact.validUntil', wordAt(signer.validUntil)],
                  ['senderMark.fact.serial', signer.serialNumber],
                  ['senderMark.fact.fingerprint', signer.fingerprint],
                  ['senderMark.fact.algorithms', algorithms === '' ? null : algorithms],
              ];

    return facts.flatMap(([label, value]) => (value === null ? [] : [{ label, value }]));
}

function certificateSteps(signature: MailSignature, address: string, wordAt: WordInstant): VerificationStep[] {
    const { authority, validity, revocation, signer } = signature.steps;
    const facts = signerFacts(signature, 'certificate', wordAt);
    const chainEnd = signature.chainEnd ?? '-';

    const authorityStep =
        authority === 'Passed'
            ? step(
                  'senderMark.step.authority',
                  'Passed',
                  signature.trustAnchor === 'ConfiguredAuthority'
                      ? 'senderMark.authority.configured'
                      : 'senderMark.authority.public',
                  { authority: chainEnd },
                  facts,
              )
            : authority === 'Failed'
              ? step(
                    'senderMark.step.authority',
                    'Failed',
                    signature.chainEnd === null ? 'senderMark.authority.failedUnnamed' : 'senderMark.authority.failed',
                    { authority: chainEnd },
                    facts,
                )
              : { ...generic('senderMark.step.authority', authority), facts };

    return [
        authorityStep,
        validityStep(signature, validity, wordAt),
        revocationStep(signature, revocation, wordAt),
        signerStep(signature, signer, address, 'certificate'),
    ];
}

function keySteps(signature: MailSignature, address: string, wordAt: WordInstant): VerificationStep[] {
    const { authority, validity, revocation, signer } = signature.steps;
    const keyId = signature.signer?.keyId ?? '-';
    const facts = signerFacts(signature, 'key', wordAt);
    const unknown = authority !== 'Passed';
    const skippedUnknown = (name: MessageKey) => step(name, 'Skipped', 'senderMark.skipped.keyUnknown');

    const known =
        authority === 'Passed'
            ? step('senderMark.step.knownKey', 'Passed', 'senderMark.key.known', { keyId }, facts)
            : authority === 'Failed'
              ? step('senderMark.step.knownKey', 'Failed', 'senderMark.key.unknown', { keyId }, facts)
              : { ...generic('senderMark.step.knownKey', authority), facts };

    const keyValidity = (): VerificationStep => {
        const until = wordAt(signature.signer?.validUntil ?? null);

        switch (validity) {
            case 'Passed':
                return until === null
                    ? step('senderMark.step.keyValidity', 'Passed', 'senderMark.keyValidity.passedOpen')
                    : step('senderMark.step.keyValidity', 'Passed', 'senderMark.keyValidity.passed', { when: until });
            case 'Failed':
                return signature.validityFailure === 'ExpiredKey' && until !== null
                    ? step('senderMark.step.keyValidity', 'Failed', 'senderMark.keyValidity.expired', { when: until })
                    : step('senderMark.step.keyValidity', 'Failed', 'senderMark.keyValidity.failed');
            default:
                return unknown
                    ? skippedUnknown('senderMark.step.keyValidity')
                    : generic('senderMark.step.keyValidity', validity);
        }
    };

    const notRevoked =
        revocation === 'Passed'
            ? step('senderMark.step.revocation', 'Passed', 'senderMark.keyRevocation.passed')
            : revocation === 'Failed'
              ? step('senderMark.step.revocation', 'Failed', 'senderMark.keyRevocation.failed')
              : unknown
                ? skippedUnknown('senderMark.step.revocation')
                : generic('senderMark.step.revocation', revocation);

    const signerIs =
        unknown && signer !== 'Passed' && signer !== 'Failed'
            ? skippedUnknown('senderMark.step.signer')
            : signerStep(signature, signer, address, 'key');

    return [known, keyValidity(), notRevoked, signerIs];
}

function validityStep(
    signature: MailSignature,
    validity: MailSignatureStepResult,
    wordAt: WordInstant,
): VerificationStep {
    const name = 'senderMark.step.validity';

    if (validity === 'Passed') {
        return step(name, 'Passed', 'senderMark.validity.passed');
    }

    if (validity === 'Skipped') {
        return step(name, 'Skipped', 'senderMark.skipped.noAuthority');
    }

    if (validity !== 'Failed') {
        return generic(name, validity);
    }

    const until = wordAt(signature.signer?.validUntil ?? null);

    switch (signature.validityFailure) {
        case 'OutsideValidityPeriod':
            return until === null
                ? step(name, 'Failed', 'senderMark.validity.outside')
                : step(name, 'Failed', 'senderMark.validity.expired', { when: until });
        case 'WrongKeyUsage':
            return step(name, 'Failed', 'senderMark.validity.purpose');
        default:
            return step(name, 'Failed', 'senderMark.validity.failed');
    }
}

function revocationStep(
    signature: MailSignature,
    revocation: MailSignatureStepResult,
    wordAt: WordInstant,
): VerificationStep {
    const name = 'senderMark.step.revocation';
    const checkedAt = wordAt(signature.revocationCheckedAt);

    switch (revocation) {
        case 'Passed':
            if (checkedAt === null) {
                return step(name, 'Passed', 'senderMark.revocation.passed');
            }

            return signature.revocationMethod === 'Crl'
                ? step(name, 'Passed', 'senderMark.revocation.crl', { when: checkedAt })
                : step(name, 'Passed', 'senderMark.revocation.ocsp', { when: checkedAt });
        case 'Failed': {
            const revokedAt = wordAt(signature.revokedAt);

            return revokedAt === null
                ? step(name, 'Failed', 'senderMark.revocation.revokedUndated')
                : step(name, 'Failed', 'senderMark.revocation.revoked', { when: revokedAt });
        }
        case 'NotChecked':
            return step(name, 'NotChecked', 'senderMark.revocation.notChecked');
        case 'Skipped':
            return step(name, 'Skipped', 'senderMark.skipped.noAuthority');
    }
}

function signerStep(
    signature: MailSignature,
    result: MailSignatureStepResult,
    address: string,
    material: 'certificate' | 'key',
): VerificationStep {
    const name = 'senderMark.step.signer';
    const signer = signature.signer?.address ?? '-';

    switch (result) {
        case 'Passed':
            return step(
                name,
                'Passed',
                material === 'key' ? 'senderMark.signer.key' : 'senderMark.signer.certificate',
                { address },
            );
        case 'Failed':
            return step(
                name,
                'Failed',
                material === 'key' ? 'senderMark.signer.keyOther' : 'senderMark.signer.certificateOther',
                { address, signer },
            );
        default:
            return generic(name, result);
    }
}
