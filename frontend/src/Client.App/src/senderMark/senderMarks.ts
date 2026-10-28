// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailFolderRole, MailListedSender, MailMessage } from '@mailfathom/client-backend';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';

// Which mark a received message carries about who sent it, decided once for every surface that draws one — a list row,
// a search result, a message in a conversation, and the message opened in the pane. The service publishes the facts
// side by side and never combines them, so the one place they are combined is here, and a row and the message it opens
// cannot come to disagree about one sender.
//
// The first rule that holds decides, in the design's order: a signature that does not match what it signs, then a
// sender this account trusts, then a signature the author made that verified, then a sender known by their address
// alone, then sender authentication that failed. Everything else is the neutral mark. A known sender outranks a failure
// because the service reaches it under one only where the domain publishes a policy that merely monitors. A trusted sender's verified signature is therefore drawn as trusted, a forged
// signature on a trusted sender as a warning, and a verified signature beside failed authentication as verified —
// which is how a forward or a mailing list usually reads.

/** The mark a message carries about its sender. */
export type SenderMark = 'signatureMismatch' | 'trusted' | 'verified' | 'known' | 'authenticationFailed' | 'neutral';

/** Chooses the mark from the facts a row, or a message, publishes about its sender. */
export function senderMarkOf(sender: MailListedSender): SenderMark {
    if (sender.signatureOutcome === 'Failed') {
        return 'signatureMismatch';
    }

    if (sender.deploymentTrust === 'Trusted') {
        return 'trusted';
    }

    if (sender.signatureOutcome === 'Verified' && sender.signerMatchesAuthor) {
        return 'verified';
    }

    if (sender.deploymentTrust === 'Known') {
        return 'known';
    }

    return sender.authorAuthentication === 'Failed' ? 'authenticationFailed' : 'neutral';
}

/** The same four facts a row carries, read out of the message route's fuller answer. */
export function listedSenderOf(message: MailMessage): MailListedSender {
    return {
        authorAuthentication: message.sender.authorAuthentication,
        deploymentTrust: message.sender.deploymentTrust,
        signatureOutcome: message.signature.outcome,
        signerMatchesAuthor: message.signature.signerMatchesAuthor,
    };
}

/**
 * Whether a message in a folder of that role is marked at all. A row in Sent or Drafts draws who it went to rather than
 * who wrote it, and the writer is the reader themselves, so there is no sender there to judge.
 */
export function marksSender(role: MailFolderRole | null): boolean {
    return role !== 'Sent' && role !== 'Drafts';
}

type Tone = 'warning' | 'trusted' | 'verified' | 'known' | 'plain';

/** How each mark is drawn: its symbol, its colour, its name, and the word a badge says beside the symbol. */
export const senderMarksDrawn: Readonly<
    Record<
        SenderMark,
        { readonly icon: IconName; readonly tone: Tone; readonly name: MessageKey; readonly word: MessageKey | null }
    >
> = {
    signatureMismatch: {
        icon: 'gpp_bad',
        tone: 'warning',
        name: 'senderMark.name.signatureMismatch',
        word: 'senderMark.word.suspicious',
    },
    trusted: {
        icon: 'verified_user',
        tone: 'trusted',
        name: 'senderMark.name.trusted',
        word: 'senderMark.word.trusted',
    },
    verified: {
        icon: 'signature',
        tone: 'verified',
        name: 'senderMark.name.verified',
        word: 'senderMark.word.verified',
    },
    known: {
        icon: 'person',
        tone: 'known',
        name: 'senderMark.name.known',
        word: 'senderMark.word.known',
    },
    authenticationFailed: {
        icon: 'gpp_bad',
        tone: 'warning',
        name: 'senderMark.name.authenticationFailed',
        word: 'senderMark.word.suspicious',
    },
    neutral: { icon: 'shield', tone: 'plain', name: 'senderMark.name.neutral', word: null },
};

/** The ink a mark's symbol is drawn in where it stands alone. */
export const senderMarkInk: Readonly<Record<Tone, string>> = {
    warning: 'text-error-text',
    trusted: 'text-healthy-text',
    verified: 'text-accent-deep',
    known: 'text-muted',
    plain: 'text-muted',
};

/** The fill and ink of a badge, and of the tile the details open with. */
export const senderMarkPill: Readonly<Record<Tone, string>> = {
    warning: 'border-error bg-error-soft text-error-text',
    trusted: 'border-transparent bg-healthy-soft text-healthy-text',
    verified: 'border-transparent bg-accent-soft text-accent-deep',
    known: 'border-line bg-sunken text-muted',
    plain: 'border-transparent bg-transparent text-muted',
};
