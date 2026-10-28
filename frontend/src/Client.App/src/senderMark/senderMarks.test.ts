// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { describe, expect, it } from 'vitest';
import type {
    MailAuthorAuthentication,
    MailDeploymentTrust,
    MailListedSender,
    MailSignatureOutcome,
} from '@mailfathom/client-backend';
import { marksSender, senderMarkOf } from './senderMarks';

const authentications: readonly MailAuthorAuthentication[] = ['NotEstablished', 'Failed', 'Authenticated'];
const trusts: readonly MailDeploymentTrust[] = ['Unknown', 'Trusted', 'Known'];
const outcomes: readonly MailSignatureOutcome[] = ['NotSigned', 'Verified', 'Failed', 'NotVerifiable'];

// Every combination of the four facts a row can carry: three authentications, three trusts, four outcomes, and whether
// the signer is the author — seventy-two senders, each of which the rule has to place somewhere.
const everySender: readonly MailListedSender[] = authentications.flatMap((authorAuthentication) =>
    trusts.flatMap((deploymentTrust) =>
        outcomes.flatMap((signatureOutcome) =>
            [false, true].map((signerMatchesAuthor) => ({
                authorAuthentication,
                deploymentTrust,
                signatureOutcome,
                signerMatchesAuthor,
            })),
        ),
    ),
);

function named(sender: MailListedSender): string {
    return `${sender.authorAuthentication}, ${sender.deploymentTrust}, ${sender.signatureOutcome}, signer ${
        sender.signerMatchesAuthor ? 'is' : 'is not'
    } the author`;
}

const forged = everySender.filter((sender) => sender.signatureOutcome === 'Failed');
const trusted = everySender.filter(
    (sender) => sender.signatureOutcome !== 'Failed' && sender.deploymentTrust === 'Trusted',
);
const verified = everySender.filter(
    (sender) =>
        sender.deploymentTrust !== 'Trusted' && sender.signatureOutcome === 'Verified' && sender.signerMatchesAuthor,
);
const known = everySender.filter(
    (sender) =>
        sender.deploymentTrust === 'Known' &&
        sender.signatureOutcome !== 'Failed' &&
        !(sender.signatureOutcome === 'Verified' && sender.signerMatchesAuthor),
);
const failed = everySender.filter(
    (sender) =>
        sender.deploymentTrust === 'Unknown' &&
        sender.signatureOutcome !== 'Failed' &&
        !(sender.signatureOutcome === 'Verified' && sender.signerMatchesAuthor) &&
        sender.authorAuthentication === 'Failed',
);

describe('senderMarkOf', () => {
    it('places every one of the seventy-two senders in exactly one of the six marks', () => {
        const placed = [forged, trusted, verified, known, failed].flat();
        const neutral = everySender.filter((sender) => !placed.includes(sender));

        expect(everySender).toHaveLength(72);
        expect(new Set(placed).size).toBe(placed.length);
        expect(neutral.map(senderMarkOf).every((mark) => mark === 'neutral')).toBe(true);
    });

    it.each(forged.map((sender) => [named(sender), sender] as const))(
        'warns about a signature that does not match what it signs, whatever else holds: %s',
        (_, sender) => {
            expect(senderMarkOf(sender)).toBe('signatureMismatch');
        },
    );

    it.each(trusted.map((sender) => [named(sender), sender] as const))(
        'marks a sender this account trusts as trusted, a verified signature included: %s',
        (_, sender) => {
            expect(senderMarkOf(sender)).toBe('trusted');
        },
    );

    it.each(verified.map((sender) => [named(sender), sender] as const))(
        'marks a signature the author made that verified as verified, beside failed authentication too: %s',
        (_, sender) => {
            expect(senderMarkOf(sender)).toBe('verified');
        },
    );

    it.each(known.map((sender) => [named(sender), sender] as const))(
        'marks a sender known by their address alone as known, above failed authentication: %s',
        (_, sender) => {
            expect(senderMarkOf(sender)).toBe('known');
        },
    );

    it.each(failed.map((sender) => [named(sender), sender] as const))(
        'warns about failed sender authentication nothing above outranks: %s',
        (_, sender) => {
            expect(senderMarkOf(sender)).toBe('authenticationFailed');
        },
    );

    it('marks a verified signature somebody other than the author made as neutral rather than verified', () => {
        expect(
            senderMarkOf({
                authorAuthentication: 'Authenticated',
                deploymentTrust: 'Unknown',
                signatureOutcome: 'Verified',
                signerMatchesAuthor: false,
            }),
        ).toBe('neutral');
    });

    it('marks a signature that could not be verified as neutral rather than as a warning', () => {
        expect(
            senderMarkOf({
                authorAuthentication: 'Authenticated',
                deploymentTrust: 'Unknown',
                signatureOutcome: 'NotVerifiable',
                signerMatchesAuthor: true,
            }),
        ).toBe('neutral');
    });
});

describe('marksSender', () => {
    it.each([['Sent'], ['Drafts']] as const)('draws no mark in %s, whose rows draw who mail went to', (role) => {
        expect(marksSender(role)).toBe(false);
    });

    it.each([['Inbox'], ['Junk'], ['Archive'], [null]] as const)('draws the mark on a row in %s', (role) => {
        expect(marksSender(role)).toBe(true);
    });
});
