// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';
import type { DrawnCategory } from './categoryWords';

// What sort of mail a message is, as the design draws it: a square label in one quiet tone whatever the category, since
// none of them is a warning. Shown and nothing more — no press here filters by one or changes one.
//
// A row is one press, so a label on it says more only under a mouse, and a finger reads the same names under the row's
// hold menu; the opened message has no press of its own to compete with.

const labelShape = 'block rounded-xs border border-line-strong px-1.25 text-sm/4 whitespace-nowrap text-muted';

// A name this long may have ended in an ellipsis, which is the only case its label has more to say. It is also the
// label that gives way first, so a short name beside it stays whole.
const longestSureToFit = 18;

function CategoryLabel({
    name,
    widest,
    tapReveals,
    narrowHides = false,
}: {
    readonly name: string;
    readonly widest: string;
    readonly tapReveals: boolean;
    readonly narrowHides?: boolean;
}) {
    const wholeName = useTip({ says: name, tapReveals });
    const mayEnd = name.length > longestSureToFit;

    return (
        <>
            <span
                {...(mayEnd ? wholeName.anchor : {})}
                className={`${labelShape} min-w-0 truncate ${widest} ${mayEnd ? 'shrink' : 'shrink-0'} ${
                    narrowHides ? '@max-list-compact:hidden' : ''
                }`}
            >
                {name}
            </span>

            {mayEnd ? wholeName.tip : null}
        </>
    );
}

function MoreCategories({
    rest,
    shownWhere,
}: {
    readonly rest: readonly DrawnCategory[];
    readonly shownWhere: string;
}) {
    const { locale, translate } = useLocalization();
    const named = new Intl.ListFormat(locale, { style: 'short', type: 'unit' }).format(
        rest.map((category) => category.name),
    );
    const tip = useTip({ says: named, tapReveals: false });

    return (
        <>
            <span {...tip.anchor} className={`${labelShape} shrink-0 ${shownWhere}`}>
                <span aria-hidden="true">{translate('category.more', { count: String(rest.length) })}</span>
                <span className="sr-only">{named}</span>
            </span>

            {tip.tip}
        </>
    );
}

/**
 * What a row of a list draws: two names at most, one where the list is narrow, and `+N` naming the rest.
 *
 * Both counts are in the document and the list's own width picks between them, because how wide the list is is a
 * container query rather than anything a render knows.
 */
export function RowCategories({
    categories,
    className = '',
}: {
    readonly categories: readonly DrawnCategory[];
    readonly className?: string;
}) {
    if (categories.length === 0) {
        return null;
    }

    return (
        <span className={`flex min-w-0 items-center gap-1 ${className}`}>
            {categories.slice(0, 2).map((category, at) => (
                <CategoryLabel
                    key={category.identity}
                    name={category.name}
                    widest="max-w-category-row"
                    tapReveals={false}
                    narrowHides={at > 0}
                />
            ))}

            {categories.length > 2 ? (
                <MoreCategories rest={categories.slice(2)} shownWhere="@max-list-compact:hidden" />
            ) : null}

            {categories.length > 1 ? (
                <MoreCategories rest={categories.slice(1)} shownWhere="hidden @max-list-compact:block" />
            ) : null}
        </span>
    );
}

/** What the opened message draws: every one of its categories, wrapping with the line they stand on. */
export function OpenedCategories({ categories }: { readonly categories: readonly DrawnCategory[] }) {
    if (categories.length === 0) {
        return null;
    }

    return (
        <span className="inline-flex max-w-full min-w-0 flex-wrap gap-1">
            {categories.map((category) => (
                <CategoryLabel key={category.identity} name={category.name} widest="max-w-category-opened" tapReveals />
            ))}
        </span>
    );
}
