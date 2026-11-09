// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState, type ReactNode } from 'react';
import type { MailWrittenHeaders } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';

// A block of header lines the message typed, which `From: The CEO` proves nothing about. So the line it folds to is
// message text at the words' own measure and carries nothing a verified header does: no avatar and no sender mark.

export function FoldedWrittenHeaders({
    headers,
    children,
}: {
    readonly headers: MailWrittenHeaders;

    /** Every original line, drawn by what draws the rest of the message, so its links keep their checks. */
    readonly children: ReactNode;
}) {
    const { translate } = useLocalization();
    const [open, setOpen] = useState(false);

    return (
        <div className="flex flex-col gap-1.5">
            <button
                type="button"
                aria-expanded={open}
                className="-ms-1 inline-flex min-h-7 items-center gap-1 self-start rounded-md ps-1 pe-2.5 text-start leading-natural text-pretty transition hover:bg-hover pointer-coarse:min-h-11"
                onClick={() => {
                    setOpen(!open);
                }}
            >
                <Icon name={open ? 'expand_more' : 'chevron_right'} className="size-icon-md text-muted" />
                <span>
                    {open
                        ? translate('body.writtenHeaders.open')
                        : translate('body.writtenHeaders.folded', { author: headers.author, date: headers.date })}
                </span>
            </button>

            {open ? <div className="flex animate-disclose-in flex-col wrap-anywhere">{children}</div> : null}
        </div>
    );
}
