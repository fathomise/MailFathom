// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MailSpamSubcategory } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { isDangerous, subcategoryLabels } from './spamWords';

// The kind of spam a row in the Spam folder names, in a square label where its other mail would carry its AI line. The
// three dangerous kinds take the error tone and a warning glyph, the rest the quiet tone; the word says it either way.

export function SpamBadge({ subcategory }: { readonly subcategory: MailSpamSubcategory }) {
    const { translate } = useLocalization();
    const danger = isDangerous(subcategory);

    return (
        <span
            className={`inline-flex shrink-0 items-center gap-0.75 rounded-sm border px-1.25 text-sm/4 whitespace-nowrap ${
                danger ? 'border-error bg-error-soft text-error-text' : 'border-line-strong text-muted'
            }`}
        >
            {danger ? <Icon name="warning" className="size-icon-xs" /> : null}
            {translate(subcategoryLabels[subcategory])}
        </span>
    );
}
