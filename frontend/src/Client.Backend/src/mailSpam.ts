// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { asRecord } from './json';

// What spam classification concluded about one message, which the deployment publishes on every row of every mail
// surface and on the opened message alike, so a row's badge and the opened message's warning cannot disagree. Every
// value is the name of a closed set's member: nothing here is text a model or a sender wrote, and a screen that wants a
// sentence composes it from the value.

/** Whether a message is spam, as the deployment publishes it. */
export type MailSpamVerdictValue = 'Spam' | 'NotSpam' | 'Undetermined';

/** Who reached the verdict: the receiving mail server and the message itself, a model, or the person. */
export type MailSpamJudge = 'Deterministic' | 'Model' | 'Person';

/** What kind of spam a message is. The word is kept for spam alone: it is not a mail category. */
export type MailSpamSubcategory =
    'Phishing' | 'BusinessEmailCompromise' | 'MalwareLure' | 'Scam' | 'Extortion' | 'Marketing';

/** Who named the subcategory, which can differ from who reached the verdict. */
export type MailSpamSubcategoryNamer = 'Model' | 'Person';

/** What a phishing message asks for. */
export type MailPhishingRequest = 'Credentials' | 'PaymentDetails' | 'OneTimeCode' | 'Other' | 'NothingStated';

/** The kind of party a message pretends to come from — never a name. */
export type MailImpersonatedParty =
    'Bank' | 'Courier' | 'OnlineService' | 'Colleague' | 'Executive' | 'Supplier' | 'Other' | 'NobodyEvident';

/** The six subcategories in the order a person is offered them. */
export const mailSpamSubcategories: readonly MailSpamSubcategory[] = [
    'Phishing',
    'BusinessEmailCompromise',
    'MalwareLure',
    'Scam',
    'Extortion',
    'Marketing',
];

const verdicts: readonly MailSpamVerdictValue[] = ['Spam', 'NotSpam', 'Undetermined'];
const judges: readonly MailSpamJudge[] = ['Deterministic', 'Model', 'Person'];
const namers: readonly MailSpamSubcategoryNamer[] = ['Model', 'Person'];
const phishingRequests: readonly MailPhishingRequest[] = [
    'Credentials',
    'PaymentDetails',
    'OneTimeCode',
    'Other',
    'NothingStated',
];
const impersonatedParties: readonly MailImpersonatedParty[] = [
    'Bank',
    'Courier',
    'OnlineService',
    'Colleague',
    'Executive',
    'Supplier',
    'Other',
    'NobodyEvident',
];

/** What spam classification concluded about one message. */
export interface MailSpamVerdict {
    readonly verdict: MailSpamVerdictValue;
    readonly decidedBy: MailSpamJudge;

    /** The kind of spam, on a `Spam` verdict only, or `null` where nothing named one. */
    readonly spamSubcategory: MailSpamSubcategory | null;
    readonly spamSubcategoryNamedBy: MailSpamSubcategoryNamer | null;

    /** What a phishing message asks for, on `Phishing` only. */
    readonly phishingRequest: MailPhishingRequest | null;

    /** Whom the message pretends to be, on `Phishing` and `BusinessEmailCompromise` only. */
    readonly impersonatedParty: MailImpersonatedParty | null;
}

/**
 * What reading a message's spam field produced: the verdict, which is `null` for a message no stage has reached.
 *
 * A wrapper for the reason `ParsedEnrichment` is one: `null` is a state this field publishes, so it is not available
 * to mean *refused*.
 */
export interface ParsedSpamVerdict {
    readonly spam: MailSpamVerdict | null;
}

/**
 * Reads what a message says about its spam verdict.
 *
 * @param value The field as the deployment sent it.
 * @returns The verdict or its absence, or `null` where the field is neither.
 */
export function parseSpamVerdict(value: unknown): ParsedSpamVerdict | null {
    if (value === undefined || value === null) {
        return { spam: null };
    }

    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const verdict = record['verdict'];
    const decidedBy = record['decidedBy'];
    const spamSubcategory = record['spamSubcategory'] ?? null;
    const spamSubcategoryNamedBy = record['spamSubcategoryNamedBy'] ?? null;
    const phishingRequest = record['phishingRequest'] ?? null;
    const impersonatedParty = record['impersonatedParty'] ?? null;

    if (!isMember(verdicts, verdict) || !isMember(judges, decidedBy)) {
        return null;
    }

    if (
        !isOptionalMember(mailSpamSubcategories, spamSubcategory) ||
        !isOptionalMember(namers, spamSubcategoryNamedBy)
    ) {
        return null;
    }

    if (
        !isOptionalMember(phishingRequests, phishingRequest) ||
        !isOptionalMember(impersonatedParties, impersonatedParty)
    ) {
        return null;
    }

    // A subcategory stands beside a spam verdict and with who named it, or not at all; the deployment publishes no other
    // combination, and a row carrying one would draw a kind of spam on a message that is not spam.
    if (
        (spamSubcategory !== null && verdict !== 'Spam') ||
        (spamSubcategory === null) !== (spamSubcategoryNamedBy === null)
    ) {
        return null;
    }

    return {
        spam: {
            verdict,
            decidedBy,
            spamSubcategory,
            spamSubcategoryNamedBy,
            phishingRequest: spamSubcategory === 'Phishing' ? phishingRequest : null,
            impersonatedParty:
                spamSubcategory === 'Phishing' || spamSubcategory === 'BusinessEmailCompromise'
                    ? impersonatedParty
                    : null,
        },
    };
}

/** Whether a message is to be treated as spam: it carries a `Spam` verdict, whoever reached it. */
export function isSpam(spam: MailSpamVerdict | null): boolean {
    return spam?.verdict === 'Spam';
}

function isMember<T extends string>(members: readonly T[], value: unknown): value is T {
    return typeof value === 'string' && members.includes(value as T);
}

function isOptionalMember<T extends string>(members: readonly T[], value: unknown): value is T | null {
    return value === null || isMember(members, value);
}
