// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { useTip } from '../controls/useTip';

// A mark a row draws on its message that opens the details behind it — the mark on who sent it, and the mark on what
// came back for it. A pointer's shortcut to those details rather than a control: a row is an `option` and holds no
// focusable descendant, so the keyboard reaches the same details through the row's menu, and the state itself is said
// to a screen reader in words, as part of the row's own name. Its own press stops at it, so pressing it neither opens
// nor selects the message, and holding it reads its name rather than opening the row's menu.

export function RowMark({
    icon,
    ink,
    name,
    onPress,
}: {
    readonly icon: IconName;

    /** The colour the symbol is drawn in. */
    readonly ink: string;

    /** What the mark says, which is both its name bubble and the words the row's own name carries. */
    readonly name: string;

    /** Opens the details, anchored under the mark. */
    readonly onPress: (anchor: DOMRect) => void;
}) {
    const anchor = useRef<HTMLElement | null>(null);
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
                className={`-mx-0.75 -my-1 flex size-6 shrink-0 cursor-pointer items-center justify-center rounded-md hover:bg-hover pointer-coarse:-mx-2.5 pointer-coarse:-my-3 pointer-coarse:size-11 ${ink}`}
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
                <Icon name={icon} className="size-icon-md pointer-coarse:size-icon-lg" />
            </span>
            {said.tip}

            <span className="sr-only">{name}</span>
        </>
    );
}
