// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';
import type { ExternalMark } from './externalMark';

// The External chip, told by its word rather than its colour. On a row it is the sender mark's sibling: a pointer's
// shortcut to the sender details, hidden from the accessibility tree because a row is an `option` that holds no
// focusable descendant, and said in words as part of the row's own name; its press stops at it, so it neither opens
// nor selects the message. Beside an opened message's sender it is a control of its own, as the sender badge is, named
// by its whole reason.

const pill =
    'flex items-center border border-error bg-error-soft text-sm leading-none font-semibold text-error-text whitespace-nowrap';

/** The chip on a message row, which opens the sender details under itself. */
export function RowExternalChip({
    mark,
    onPress,
}: {
    readonly mark: ExternalMark;

    /** Opens the sender details, anchored under the chip. */
    readonly onPress: (anchor: DOMRect) => void;
}) {
    const { translate } = useLocalization();
    const anchor = useRef<HTMLElement | null>(null);
    const name = translate(mark.name.key, mark.name.values);
    const said = useTip({
        says: name,
        describes: true,
        onPress: () => {
            const at = anchor.current?.getBoundingClientRect();

            if (at !== undefined) {
                onPress(at);
            }
        },
    });

    return (
        <>
            <span
                {...said.anchor}
                ref={(element) => {
                    anchor.current = element;
                    said.anchor.ref(element);
                }}
                aria-hidden="true"
                className="flex shrink-0 cursor-pointer items-center justify-center rounded-md transition hover:opacity-85 pointer-coarse:-my-3.25 pointer-coarse:min-h-11"
                onPointerDown={(event) => {
                    event.stopPropagation();
                    said.anchor.onPointerDown(event);
                }}
                onPointerUp={(event) => {
                    event.stopPropagation();
                    said.anchor.onPointerUp();
                }}
                onClick={(event) => {
                    event.stopPropagation();
                    said.anchor.onClick(event);
                }}
                onDoubleClick={(event) => {
                    event.stopPropagation();
                }}
            >
                <span className={`${pill} h-4.5 rounded-chip px-1.5`}>{translate('senderMark.external.word')}</span>
            </span>
            {said.tip}

            <span className="sr-only">{name}</span>
        </>
    );
}

/**
 * The chip on a conversation row, which is one press as a whole, so the chip is said as part of the row's name rather
 * than offered as a control — as the sender mark beside it is.
 */
export function ExternalPill({ mark }: { readonly mark: ExternalMark }) {
    const { translate } = useLocalization();

    return (
        <span className="flex shrink-0 items-center">
            <span aria-hidden="true" className={`${pill} h-4.5 rounded-chip px-1.5`}>
                {translate('senderMark.external.word')}
            </span>
            <span className="sr-only">{translate(mark.name.key, mark.name.values)}</span>
        </span>
    );
}

/** The chip beside an opened message's sender: the badge's own shape, with its glyph. */
export function ExternalBadge({
    mark,
    onOpen,
}: {
    readonly mark: ExternalMark;

    /** Opens the sender details, anchored under the chip. */
    readonly onOpen: (anchor: DOMRect) => void;
}) {
    const { translate } = useLocalization();
    const element = useRef<HTMLButtonElement | null>(null);

    return (
        <SymbolControl
            label={translate(mark.name.key, mark.name.values)}
            popsUp="dialog"
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
            <span aria-hidden="true" className={`${pill} h-5.5 gap-1 rounded-sm ps-1.5 pe-2`}>
                <Icon name="domain_disabled" className="size-3.75" />
                {translate('senderMark.external.word')}
            </span>
        </SymbolControl>
    );
}
