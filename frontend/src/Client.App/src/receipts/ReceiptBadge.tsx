// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import type { MailParticipant, MailReceipts } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import { useLocalization } from '../localization/useLocalization';
import { receiptMarksDrawn, receiptNameOf, receiptPill, standingOfReceipts, wordOf } from './receiptMarks';

// The mark on the newest message the account sent in an opened conversation: a badge naming what came back, or that a
// request stands with nothing back yet, which only the opened message says. It is a control in its own right, unlike
// the mark on a Sent row, so a keyboard reaches the details through it; its name is the mark's whole name — every
// recipient's own outcome — which the word beside the symbol shortens.

export function ReceiptBadge({
    receipts,
    participants,
    onOpen,
}: {
    readonly receipts: MailReceipts | null;

    /** Who the message was written to, which is what names each recipient. */
    readonly participants: readonly MailParticipant[];

    /** Opens the details, anchored under the badge. */
    readonly onOpen: (anchor: DOMRect) => void;
}) {
    const { locale, translate } = useLocalization();
    const element = useRef<HTMLButtonElement | null>(null);
    const standing = standingOfReceipts(receipts);

    if (standing === null || receipts === null) {
        return null;
    }

    const drawn = receiptMarksDrawn[standing.mark];

    return (
        <SymbolControl
            label={receiptNameOf(standing, receipts, participants, translate, locale)}
            ref={(button) => {
                element.current = button;
            }}
            onPress={() => {
                if (element.current !== null) {
                    onOpen(element.current.getBoundingClientRect());
                }
            }}
            popsUp="dialog"
            className="flex shrink-0 items-center justify-center self-center rounded-md transition hover:opacity-85"
        >
            <span
                aria-hidden="true"
                className={`flex h-5.5 items-center gap-1 rounded-sm border ps-1.5 pe-2 text-sm leading-none font-semibold whitespace-nowrap ${receiptPill[drawn.tone]}`}
            >
                <Icon name={drawn.icon} className="size-3.75" />
                {wordOf(standing, translate, locale)}
            </span>
        </SymbolControl>
    );
}
