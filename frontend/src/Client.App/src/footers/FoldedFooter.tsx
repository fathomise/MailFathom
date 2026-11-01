// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import type { MailDocumentBlock } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { MessageBlocks } from '../messageBody/MessageBlocks';

// The one quiet line a footer folds to wherever it is drawn, as the design draws it for sent mail, the composer, and an
// Agent's draft alike: folded until it is pressed, so a long footer never lengthens what it stands under, and folded
// again by the same press. What is under the line is the footer as the service serves it, drawn by what draws a
// message, so it shows exactly what will go out — its pictures included.

export function FoldedFooter({
    blocks,
    quiet = false,
}: {
    readonly blocks: readonly MailDocumentBlock[];

    /** Whether its lines are drawn quieter than the words beside them, which is how the composer tells the two apart. */
    readonly quiet?: boolean;
}) {
    const { translate } = useLocalization();
    const [open, setOpen] = useState(false);

    return (
        <div className="flex flex-col gap-1.5">
            <button
                type="button"
                aria-expanded={open}
                className="-ms-1 inline-flex min-h-7 items-center gap-1 self-start rounded-md ps-1 pe-2.5 text-base pointer-coarse:min-h-11 leading-natural text-muted transition hover:bg-hover hover:text-text-soft"
                onClick={() => {
                    setOpen(!open);
                }}
            >
                <Icon name={open ? 'expand_more' : 'chevron_right'} className="size-icon-md" />
                <span>{translate(open ? 'footer.open' : 'footer.folded')}</span>
            </button>

            {open ? (
                <div
                    className={`flex animate-disclose-in flex-col gap-px leading-normal text-pretty ${
                        quiet ? 'text-base text-muted' : 'text-md text-text-soft'
                    }`}
                >
                    <MessageBlocks blocks={blocks} />
                </div>
            ) : null}
        </div>
    );
}
