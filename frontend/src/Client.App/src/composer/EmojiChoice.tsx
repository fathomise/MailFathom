// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { MouseEvent } from 'react';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';
import { offeredEmoji, pictureOf, type OfferedEmoji } from './offeredEmoji';

/** The emoji menu's grid: eight squares to a row under a mouse, and as many as the composer's width holds under a finger. */
export function EmojiChoice({ onChoose }: { readonly onChoose: (text: string) => void }) {
    return (
        <div className="flex flex-wrap gap-0.5 px-2.5 pt-0.5 pb-1.5">
            {offeredEmoji.map((emoji) => (
                <EmojiSquare
                    key={emoji.named}
                    emoji={emoji}
                    onChoose={() => {
                        onChoose(emoji.text);
                    }}
                />
            ))}
        </div>
    );
}

/** One emoji, drawn from its openly licensed picture and named for whoever cannot see it; choosing it writes it as text. */
function EmojiSquare({ emoji, onChoose }: { readonly emoji: OfferedEmoji; readonly onChoose: () => void }) {
    const { translate } = useLocalization();
    const named = translate(emoji.named);
    const { anchor, tip } = useTip({ says: named, onPress: onChoose });

    return (
        <>
            <button
                {...anchor}
                type="button"
                role="menuitem"
                aria-label={named}
                tabIndex={-1}
                className="flex size-8 items-center justify-center rounded-md transition hover:bg-hover"
                onMouseDown={keepTheSelection}
            >
                <img src={pictureOf(emoji.text)} alt="" draggable={false} className="size-5 pointer-coarse:size-6" />
            </button>
            {tip}
        </>
    );
}

// The selection a choice acts on is the editor's, and a press that reached the editor first would collapse it.
function keepTheSelection(event: MouseEvent): void {
    event.preventDefault();
}
