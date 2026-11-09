// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { asRecord } from './json';

// What sort of mail a message is, which the deployment publishes on a list row, on a conversation row, and on the opened
// message alike: a model's reading of it, and never evidence about who sent it. A default arrives as an identity the
// client names in the reader's language, and a category the mailbox declared arrives with the name somebody wrote.

/** The seven categories every categorizing mailbox answers from, by their published identities. */
export type MailDefaultCategory =
    'correspondence' | 'subscriptions' | 'finance' | 'shopping' | 'travel' | 'notifications' | 'other';

/** One category a message or a conversation shows. */
export type MailCategory =
    | { readonly identity: MailDefaultCategory; readonly name: null }
    | { readonly identity: string; readonly name: string };

const defaultCategories: readonly MailDefaultCategory[] = [
    'correspondence',
    'subscriptions',
    'finance',
    'shopping',
    'travel',
    'notifications',
    'other',
];

// A mailbox holds the seven defaults and twenty of its own at most, so a longer list is not an answer about mail.
const mostCategories = 27;

// Far above the longest name a mailbox may declare, and there for the answer that is not a list of categories at all.
const longestCategoryName = 256;

/**
 * Reads the categories of a message or a conversation, in the order the deployment published them.
 *
 * @param value The field as the deployment sent it.
 * @returns The categories, empty where it shows none, or `null` where the field is missing or is not a list of them.
 */
export function parseMailCategories(value: unknown): readonly MailCategory[] | null {
    if (!Array.isArray(value) || value.length > mostCategories) {
        return null;
    }

    const categories: MailCategory[] = [];
    for (const entry of value) {
        const category = parseCategory(entry);
        if (category === null) {
            return null;
        }

        categories.push(category);
    }

    return categories;
}

function parseCategory(value: unknown): MailCategory | null {
    const record = asRecord(value);
    if (record === null) {
        return null;
    }

    const identity = record['identity'];
    const name = record['name'] ?? null;

    if (name === null) {
        return isDefaultCategory(identity) ? { identity, name } : null;
    }

    return isCategoryName(identity) && isCategoryName(name) ? { identity, name } : null;
}

function isDefaultCategory(value: unknown): value is MailDefaultCategory {
    return defaultCategories.some((category) => category === value);
}

function isCategoryName(value: unknown): value is string {
    return typeof value === 'string' && value.length > 0 && value.length <= longestCategoryName;
}
