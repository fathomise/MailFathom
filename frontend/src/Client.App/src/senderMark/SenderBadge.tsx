// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import { useLocalization } from '../localization/useLocalization';
import { senderMarkPill, senderMarksDrawn, type SenderMark } from './senderMarks';

// The mark beside the sender of an opened message, which names the state it shows: a badge with a word where the mark
// says something, and the symbol alone for a sender nothing recognized. It is a control in its own right, unlike the
// mark on a list row, so a keyboard reaches the details through it; its name is the mark's whole name, which the word
// beside the symbol shortens.

export function SenderBadge({
    mark,
    onOpen,
}: {
    readonly mark: SenderMark;

    /** Opens the details, anchored under the badge. */
    readonly onOpen: (anchor: DOMRect) => void;
}) {
    const { translate } = useLocalization();
    const element = useRef<HTMLButtonElement | null>(null);
    const drawn = senderMarksDrawn[mark];

    return (
        <SymbolControl
            label={translate(drawn.name)}
            ref={(button) => {
                element.current = button;
            }}
            onPress={() => {
                if (element.current !== null) {
                    onOpen(element.current.getBoundingClientRect());
                }
            }}
            className="flex shrink-0 items-center justify-center self-center rounded-md transition hover:opacity-85"
        >
            <span
                aria-hidden="true"
                className={`flex h-5.5 items-center gap-1 rounded-sm border text-sm leading-none font-semibold whitespace-nowrap ${
                    senderMarkPill[drawn.tone]
                } ${drawn.word === null ? 'w-5.5 justify-center' : 'ps-1.5 pe-2'}`}
            >
                <Icon name={drawn.icon} className={drawn.word === null ? 'size-icon-sm' : 'size-3.75'} />
                {drawn.word === null ? null : translate(drawn.word)}
            </span>
        </SymbolControl>
    );
}
