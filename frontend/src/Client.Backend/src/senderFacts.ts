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

/** Whether this deployment recognizes that author. */
export type MailDeploymentTrust = 'Unknown' | 'Trusted';

/**
 * What verifying the message's own S/MIME or OpenPGP signature concluded.
 *
 * `Failed` is a signature that does not match the content it signs; `NotVerifiable` is one that could not be verified
 * here, which never says it was forged.
 */
export type MailSignatureOutcome = 'NotSigned' | 'Verified' | 'Failed' | 'NotVerifiable';

/**
 * What a row publishes about who sent its message: enough to choose the sender mark, and nothing more.
 *
 * The evidence each fact was reached from stays on the message route, which is what a details surface reads.
 */
export interface MailListedSender {
    readonly authorAuthentication: MailAuthorAuthentication;
    readonly deploymentTrust: MailDeploymentTrust;
    readonly signatureOutcome: MailSignatureOutcome;
    readonly signerMatchesAuthor: boolean;
}

export const authorAuthentications: readonly MailAuthorAuthentication[] = ['NotEstablished', 'Failed', 'Authenticated'];

export const deploymentTrusts: readonly MailDeploymentTrust[] = ['Unknown', 'Trusted'];

export const signatureOutcomes: readonly MailSignatureOutcome[] = ['NotSigned', 'Verified', 'Failed', 'NotVerifiable'];

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

    if (!isOneOf(authorAuthentication, authorAuthentications) || !isOneOf(deploymentTrust, deploymentTrusts)) {
        return null;
    }

    if (!isOneOf(signatureOutcome, signatureOutcomes) || typeof signerMatchesAuthor !== 'boolean') {
        return null;
    }

    return { authorAuthentication, deploymentTrust, signatureOutcome, signerMatchesAuthor };
}

/** Whether the value is one of the members of a closed set this package parses. */
export function isOneOf<TValue extends string>(value: unknown, members: readonly TValue[]): value is TValue {
    return typeof value === 'string' && members.includes(value as TValue);
}
