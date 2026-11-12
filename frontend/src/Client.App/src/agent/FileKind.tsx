// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import { kindOf } from '../readingPane/fileKind';

/**
 * The tile a file of a question is drawn with: the kind it is, or the picture symbol for a picture. Decorative, because
 * the name beside it already says what the file is.
 *
 * @param carried Whether it is drawn on a question already sent, which the design draws smaller than a waiting file.
 */
export function FileKind({
    name,
    mediaType,
    failed = false,
    carried = false,
}: {
    readonly name: string | null;
    readonly mediaType: string;
    readonly failed?: boolean;
    readonly carried?: boolean;
}) {
    const kind = kindOf(name, mediaType);

    return (
        <span
            aria-hidden="true"
            className={`flex shrink-0 items-center justify-center text-xs font-semibold uppercase ${
                carried ? 'size-6.5 rounded-sm' : 'size-8 rounded-md'
            } ${failed ? 'bg-error-soft text-error-text' : 'bg-sunken text-muted'}`}
        >
            {kind === 'image' ? <Icon name="image" className={carried ? 'size-icon-sm' : 'size-icon-md'} /> : kind}
        </span>
    );
}
