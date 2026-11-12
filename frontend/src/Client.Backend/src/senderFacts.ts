// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { asRecord } from './json';

// The facts every received message carries about who sent it, as the closed sets the service publishes them in.
// A list row and the message route both carry them, so the sets are stated once here rather than once per route:
// two spellings of one outcome are how a row and the message it opens come to disagree about one sender.
//
// They stay side by side and are never combined here. Which mark a screen draws from them is a rendering decision,
// and the package that owns the wire describes what the service said rather than how a screen will show it.

/** What was established about the author the message displays. */
export type MailAuthorAuthentication = 'NotEstablished' | 'Failed' | 'Authenticated';

/**
 * Whether this deployment recognizes that author.
 *
 * `Known` is a sender recognized by the `From` address alone, where nothing established that their domain sent the
 * message; it is a weaker claim than `Trusted` and never one.
 */
export type MailDeploymentTrust = 'Unknown' | 'Trusted' | 'Known';

/**
 * What verifying the message's own S/MIME or OpenPGP signature concluded.
 *
 * `Failed` is a signature that does not match the content it signs; `NotVerifiable` is one that could not be verified
 * here, which never says it was forged.
 */
export type MailSignatureOutcome = 'NotSigned' | 'Verified' | 'Failed' | 'NotVerifiable';

/**
 * What recognized a trusted author: one of the owner's own addresses, a domain of the owner's own accounts, a contact in
 * the receiving account's organization's book or in a reader's own, or the signature of the receiving account's
 * organization, which proves the author a member of it.
 */
export type MailTrustSource =
    | 'OwnAccountDomain'
    | 'OrganizationMember'
    | 'OrganizationDirectory'
    | 'OwnAccountAddress'
    | 'OrganizationContact'
    | 'PersonalContact';

/**
 * What a row publishes about who sent its message: enough to choose the sender mark and the External chip, and nothing
 * more.
 *
 * The evidence each fact was reached from stays on the message route, which is what a details surface reads.
 */
export interface MailListedSender {
    readonly authorAuthentication: MailAuthorAuthentication;
    readonly deploymentTrust: MailDeploymentTrust;
    readonly signatureOutcome: MailSignatureOutcome;
    readonly signerMatchesAuthor: boolean;

    /** What recognized the author, or `null` where nothing did; `OrganizationMember` is the receiving organization's signature. */
    readonly trustSource: MailTrustSource | null;

    /** The colleague's current name, which only `OrganizationDirectory` ever carries. */
    readonly trustMember: string | null;
}

export const authorAuthentications: readonly MailAuthorAuthentication[] = ['NotEstablished', 'Failed', 'Authenticated'];

export const deploymentTrusts: readonly MailDeploymentTrust[] = ['Unknown', 'Trusted', 'Known'];

export const signatureOutcomes: readonly MailSignatureOutcome[] = ['NotSigned', 'Verified', 'Failed', 'NotVerifiable'];

export const trustSources: readonly MailTrustSource[] = [
    'OwnAccountDomain',
    'OrganizationMember',
    'OrganizationDirectory',
    'OwnAccountAddress',
    'OrganizationContact',
    'PersonalContact',
];

/** The sender facts one row carries, or `null` where what arrived is not them. */
export function parseListedSender(value: unknown): MailListedSender | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const authorAuthentication = record['authorAuthentication'];
    const deploymentTrust = record['deploymentTrust'];
    const signatureOutcome = record['signatureOutcome'];
    const signerMatchesAuthor = record['signerMatchesAuthor'];
    const trustSource = record['trustSource'] ?? null;
    const trustMember = record['trustMember'] ?? null;

    if (!isOneOf(authorAuthentication, authorAuthentications) || !isOneOf(deploymentTrust, deploymentTrusts)) {
        return null;
    }

    if (!isOneOf(signatureOutcome, signatureOutcomes) || typeof signerMatchesAuthor !== 'boolean') {
        return null;
    }

    if (trustSource !== null && !isOneOf(trustSource, trustSources)) {
        return null;
    }

    // Only the directory source names a colleague, so a name beside any other is not what the service sent.
    if (trustMember !== null && (typeof trustMember !== 'string' || trustSource !== 'OrganizationDirectory')) {
        return null;
    }

    return { authorAuthentication, deploymentTrust, signatureOutcome, signerMatchesAuthor, trustSource, trustMember };
}

/** Whether the value is one of the members of a closed set this package parses. */
export function isOneOf<TValue extends string>(value: unknown, members: readonly TValue[]): value is TValue {
    return typeof value === 'string' && members.includes(value as TValue);
}
