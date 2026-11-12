// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailAccount, MailListedSender } from '@mailfathom/client-backend';
import { createContext, useContext } from 'react';
import type { Said, VerificationStep } from './verificationSteps';

// The External chip: a received message on an account of the reader's organization that the organization's signature
// does not prove, marked in so many words once the organization switched the mark on. It asks about the signature and
// nothing else, so it stands beside the sender mark and changes no mark, and a colleague writing from another mail
// application earns it as a stranger does — which is why what it says always explains that. Decided once here for every
// surface that draws it, as the sender mark is decided once in `senderMarks.ts`.

/** Which accounts mark external mail, and the organization that does — what every surface drawing the chip reads. */
export interface ExternalMarking {
    /** The accounts whose organization turned the mark on. */
    readonly accounts: ReadonlySet<string>;

    /**
     * The reader's organization's name, which every sentence the chip says names. `null` draws no chip at all, since a
     * chip never stands without its explanation: an account of an organization is assigned only to its members, so a
     * reader whose session names none holds no account that marks.
     */
    readonly organization: string | null;
}

const nothingMarked: ExternalMarking = { accounts: new Set<string>(), organization: null };

export const ExternalMarkingContext = createContext<ExternalMarking>(nothingMarked);

/** What the frame said about the mark, or that no account marks anything where nothing provided it. */
export function useExternalMarking(): ExternalMarking {
    return useContext(ExternalMarkingContext);
}

/** Composes the marking from the accounts the deployment answered and the reader's organization. */
export function externalMarkingOf(accounts: readonly MailAccount[], organization: string | null): ExternalMarking {
    return {
        accounts: new Set(accounts.filter((account) => account.marksExternalMail).map((account) => account.id)),
        organization,
    };
}

/** What the chip says: its short form for a row menu's header, its whole name, and the sentence behind it. */
export interface ExternalMark {
    readonly short: Said;
    readonly name: Said;
    readonly why: Said;
}

/** The facts a row and a message both carry that the chip reads: what recognized the author, and whose address it is. */
export type ExternalSender = Pick<MailListedSender, 'trustSource' | 'trustMember'>;

/** The organization whose signature is asked about on this account, or `null` where nothing there marks. */
function signingOrganizationOf(marking: ExternalMarking, account: string): string | null {
    return marking.accounts.has(account) ? marking.organization : null;
}

// A colleague the member directory names is a colleague by their address alone, which proves nobody, so the sentence
// says how a colleague's message comes to be marked and never that this one is a colleague's.
function colleagueOf(sender: ExternalSender): string | null {
    return sender.trustSource === 'OrganizationDirectory' ? sender.trustMember : null;
}

/**
 * The chip a received message on an account carries, or `null` where it carries none: an account that does not mark,
 * and a message the organization's signature proves.
 *
 * Which rows are received mail at all — not Sent, Drafts, or the Outbox, not the reader's own — is decided where the
 * sender mark is, and a surface draws the chip only where it draws that mark.
 */
export function externalMarkOf(marking: ExternalMarking, account: string, sender: ExternalSender): ExternalMark | null {
    const organization = signingOrganizationOf(marking, account);

    if (organization === null || sender.trustSource === 'OrganizationMember') {
        return null;
    }

    const member = colleagueOf(sender);
    const short: Said = { key: 'senderMark.external.short', values: { organization } };

    return member === null
        ? {
              short,
              name: { key: 'senderMark.external.name', values: { organization } },
              why: { key: 'senderMark.external.why', values: { organization } },
          }
        : {
              short,
              name: { key: 'senderMark.external.nameColleague', values: { organization, member } },
              why: { key: 'senderMark.external.whyColleague', values: { organization, member } },
          };
}

/**
 * The step the details open their *Trust* group with on an account that marks: whether the organization's signature
 * proved the message. `null` where the account does not mark, which draws the group as it stood.
 */
export function organizationSignatureStepOf(
    marking: ExternalMarking,
    account: string,
    sender: ExternalSender & { readonly trustOrganization: string | null },
): VerificationStep | null {
    const organization = signingOrganizationOf(marking, account);

    if (organization === null) {
        return null;
    }

    // A member whose organization was since deleted was signed by an organization the service no longer names, so the
    // step says that rather than crediting the reader's own.
    if (sender.trustSource === 'OrganizationMember') {
        return {
            name: 'senderMark.step.organizationSignature',
            result: 'Passed',
            detail:
                sender.trustOrganization === null
                    ? { key: 'senderMark.trust.organizationSignedDeleted' }
                    : {
                          key: 'senderMark.trust.organizationSigned',
                          values: { organization: sender.trustOrganization },
                      },
            facts: null,
        };
    }

    const member = colleagueOf(sender);

    return {
        name: 'senderMark.step.organizationSignature',
        result: 'Failed',
        detail:
            member === null
                ? { key: 'senderMark.trust.organizationUnsigned', values: { organization } }
                : { key: 'senderMark.trust.organizationUnsignedColleague', values: { organization, member } },
        facts: null,
    };
}
