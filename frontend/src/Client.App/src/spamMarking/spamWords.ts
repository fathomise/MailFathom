// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type {
    MailImpersonatedParty,
    MailPhishingRequest,
    MailSpamJudge,
    MailSpamSubcategory,
    MailSpamVerdict,
} from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { Translate } from '../localization/useLocalization';

// What a spam verdict is called, wherever a screen names one: the badge in the Spam folder, the folded row's accessible
// name and its menu's header, the opened message's warning, and the chooser. One module, because the same verdict is
// named on all of them and a second table is how a row and its warning come to disagree.
//
// **Every sentence is composed from the closed values the deployment publishes**, never from anything the message wrote:
// what it asks for and whom it pretends to be are kinds rather than names, so a warning cannot repeat the message's own
// claims as fact. And a model's subcategory is said to be a model's judgement wherever it is said.

/** The three subcategories that warn: each carries a glyph and the error tone wherever it is named. */
const dangerous: ReadonlySet<MailSpamSubcategory> = new Set(['Phishing', 'BusinessEmailCompromise', 'MalwareLure']);

/** Whether the subcategory is one of the three that warn rather than inform. */
export function isDangerous(subcategory: MailSpamSubcategory | null): boolean {
    return subcategory !== null && dangerous.has(subcategory);
}

export const subcategoryLabels: Readonly<Record<MailSpamSubcategory, MessageKey>> = {
    Phishing: 'spam.subcategory.Phishing',
    BusinessEmailCompromise: 'spam.subcategory.BusinessEmailCompromise',
    MalwareLure: 'spam.subcategory.MalwareLure',
    Scam: 'spam.subcategory.Scam',
    Extortion: 'spam.subcategory.Extortion',
    Marketing: 'spam.subcategory.Marketing',
};

/** The subcategory as it is named inside a sentence rather than as a label. */
const subcategoriesNamed: Readonly<Record<MailSpamSubcategory, MessageKey>> = {
    Phishing: 'spam.subcategoryNamed.Phishing',
    BusinessEmailCompromise: 'spam.subcategoryNamed.BusinessEmailCompromise',
    MalwareLure: 'spam.subcategoryNamed.MalwareLure',
    Scam: 'spam.subcategoryNamed.Scam',
    Extortion: 'spam.subcategoryNamed.Extortion',
    Marketing: 'spam.subcategoryNamed.Marketing',
};

export const subcategoryHints: Readonly<Record<MailSpamSubcategory, MessageKey>> = {
    Phishing: 'spam.subcategoryHint.Phishing',
    BusinessEmailCompromise: 'spam.subcategoryHint.BusinessEmailCompromise',
    MalwareLure: 'spam.subcategoryHint.MalwareLure',
    Scam: 'spam.subcategoryHint.Scam',
    Extortion: 'spam.subcategoryHint.Extortion',
    Marketing: 'spam.subcategoryHint.Marketing',
};

const judgedBy: Readonly<Record<MailSpamJudge, MessageKey>> = {
    Deterministic: 'spam.judgedBy.Deterministic',
    Model: 'spam.judgedBy.Model',
    Person: 'spam.judgedBy.Person',
};

const judges: Readonly<Record<MailSpamJudge, MessageKey>> = {
    Deterministic: 'spam.judge.Deterministic',
    Model: 'spam.judge.Model',
    Person: 'spam.judge.Person',
};

// What a phishing message asks for and whom it pretends to be, where the record names something a sentence can say.
// `Other`, `NothingStated`, and `NobodyEvident` name nothing, so the warning says nothing about them rather than a
// sentence with nothing in it.
const requests: Readonly<Partial<Record<MailPhishingRequest, MessageKey>>> = {
    Credentials: 'spam.request.Credentials',
    PaymentDetails: 'spam.request.PaymentDetails',
    OneTimeCode: 'spam.request.OneTimeCode',
};

const parties: Readonly<Partial<Record<MailImpersonatedParty, MessageKey>>> = {
    Bank: 'spam.party.Bank',
    Courier: 'spam.party.Courier',
    OnlineService: 'spam.party.OnlineService',
    Colleague: 'spam.party.Colleague',
    Executive: 'spam.party.Executive',
    Supplier: 'spam.party.Supplier',
};

/** What a spam row is called to assistive technology: the verdict, its kind, and who reached it. */
export function spamRowName(spam: MailSpamVerdict, translate: Translate): string {
    const judge = translate(judgedBy[spam.spamSubcategoryNamedBy ?? spam.decidedBy]);

    return spam.spamSubcategory === null
        ? translate('spam.rowNameNoKind', { judge })
        : translate('spam.rowName', { kind: translate(subcategoryLabels[spam.spamSubcategory]), judge });
}

/**
 * The line under a folded row's menu header, which carries the kind a finger has no hover to ask for. A message that
 * names nobody is left with the verdict as the row's accessible name says it.
 */
export function spamMenuDetail(correspondent: string | undefined, spam: MailSpamVerdict, translate: Translate): string {
    if (correspondent === undefined) {
        return spamRowName(spam, translate);
    }

    return spam.spamSubcategory === null
        ? translate('spam.menuDetailNoKind', { correspondent })
        : translate('spam.menuDetail', {
              correspondent,
              kind: translate(subcategoriesNamed[spam.spamSubcategory]),
          });
}

/** What the opened message's warning says, in the order it is drawn. */
export interface SpamWarning {
    /** Whether the kind is one of the dangerous ones, which draws the strong form. */
    readonly strong: boolean;
    readonly title: string;

    /** The sentences under the title; the last is what the mark holds back, which is drawn muted. */
    readonly lines: readonly string[];
}

/** Composes the warning an opened spam message carries. */
export function spamWarning(spam: MailSpamVerdict, translate: Translate): SpamWarning {
    const subcategory = spam.spamSubcategory;
    const strong = isDangerous(subcategory);
    const label = subcategory === null ? null : translate(subcategoryLabels[subcategory]);

    const title = strong
        ? translate('spam.warningDanger', { kind: label ?? '' })
        : label === null
          ? translate('spam.warningQuiet')
          : translate('spam.warningQuietKind', { kind: label });

    const asked = strong ? whatItAsks(spam, translate) : null;

    return {
        strong,
        title,
        lines: [
            ...(asked === null ? [] : [asked]),
            ...whoJudged(spam, translate),
            ...(strong ? [translate('spam.doNotGive')] : []),
            translate('spam.readOnly'),
        ],
    };
}

function whatItAsks(spam: MailSpamVerdict, translate: Translate): string | null {
    const request = spam.phishingRequest === null ? undefined : requests[spam.phishingRequest];
    const party = spam.impersonatedParty === null ? undefined : parties[spam.impersonatedParty];

    if (request !== undefined && party !== undefined) {
        return translate('spam.asksAndPoses', { request: translate(request), party: translate(party) });
    }

    if (request !== undefined) {
        return translate('spam.asks', { request: translate(request) });
    }

    return party === undefined ? null : translate('spam.poses', { party: translate(party) });
}

// Who judged it, and who named the kind where that was somebody else: the model names the kind of a verdict the mail
// server reached, and the warning says both rather than crediting either with the other's call.
function whoJudged(spam: MailSpamVerdict, translate: Translate): readonly string[] {
    const subcategory = spam.spamSubcategory;
    const namer = spam.spamSubcategoryNamedBy;

    if (subcategory === null || namer === null) {
        return [translate(judges[spam.decidedBy])];
    }

    const kind = translate(subcategoriesNamed[subcategory]);

    if (namer === spam.decidedBy) {
        return [translate(namer === 'Model' ? 'spam.judgeNamed.Model' : 'spam.judgeNamed.Person', { kind })];
    }

    return [
        translate(judges[spam.decidedBy]),
        translate(namer === 'Model' ? 'spam.kindNamedBy.Model' : 'spam.kindNamedBy.Person', { kind }),
    ];
}
