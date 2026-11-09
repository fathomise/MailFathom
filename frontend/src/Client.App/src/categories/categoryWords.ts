// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailCategory, MailDefaultCategory } from '@mailfathom/client-backend';
import type { MessageKey } from '../localization/en';
import type { Translate } from '../localization/useLocalization';

// What each default is called, or nothing for the two the design draws no label for: mail a person wrote, and mail
// nothing else fits, are what a mailbox holds most of, and a label on them would say nothing about the row it stands on.
const defaultCategoryWords: Readonly<Record<MailDefaultCategory, MessageKey | null>> = {
    correspondence: null,
    subscriptions: 'category.subscriptions',
    finance: 'category.finance',
    shopping: 'category.shopping',
    travel: 'category.travel',
    notifications: 'category.notifications',
    other: null,
};

/** A category with a label to draw. */
export interface DrawnCategory {
    readonly identity: string;
    readonly name: string;
}

/** Whether anything of these categories draws a label, which is what a conversation row's height turns on. */
export function drawsCategories(categories: readonly MailCategory[]): boolean {
    return categories.some((category) => category.name !== null || defaultCategoryWords[category.identity] !== null);
}

/** The labels these categories draw, in the order the deployment published them. */
export function drawnCategories(categories: readonly MailCategory[], translate: Translate): readonly DrawnCategory[] {
    return categories.flatMap((category) => {
        if (category.name !== null) {
            return [{ identity: category.identity, name: category.name }];
        }

        const word = defaultCategoryWords[category.identity];

        return word === null ? [] : [{ identity: category.identity, name: translate(word) }];
    });
}
