// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientMailListMode } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useMailboxesForm } from '../workspace/mailboxesForm';

// The one control that turns the list between one message and one conversation per row. It stands at the top of the
// mailbox column, above the AI filters and the accounts and outside their scroll, so it never scrolls away — docked and
// as a drawer alike, which makes it the drawer's first row on the phone and the fold.
//
// It names the mode the list is in rather than the one it would switch to, because that is what a person looks at it to
// learn; the glyph at its end and its sentence say that a press turns it over. Folded to the strip it is the mode's
// glyph alone, and the sentence carries the rest.

const modes: Readonly<
    Record<
        ClientMailListMode,
        {
            readonly icon: IconName;
            readonly label: MessageKey;
            readonly says: MessageKey;
            readonly named: MessageKey;
            readonly other: ClientMailListMode;
        }
    >
> = {
    messages: {
        icon: 'mail',
        label: 'listMode.messages',
        says: 'listMode.switchToThreads',
        named: 'listMode.messagesNamed',
        other: 'threads',
    },
    threads: {
        icon: 'forum',
        label: 'listMode.threads',
        says: 'listMode.switchToMessages',
        named: 'listMode.threadsNamed',
        other: 'messages',
    },
};

export function ListModeSwitch({
    mode,
    onChoose,
}: {
    readonly mode: ClientMailListMode;
    readonly onChoose: (mode: ClientMailListMode) => void;
}) {
    // Read through the form rather than handed in, because the switch is built where the preference is held and handed
    // to the column already built, as the folder tree is.
    const folded = useMailboxesForm() === 'strip';

    return (
        <ModeControl
            mode={mode}
            folded={folded}
            onSwitch={() => {
                onChoose(modes[mode].other);
            }}
        />
    );
}

function ModeControl({
    mode,
    folded,
    onSwitch,
}: {
    readonly mode: ClientMailListMode;
    readonly folded: boolean;
    readonly onSwitch: () => void;
}) {
    const { translate } = useLocalization();
    const drawn = modes[mode];
    const { anchor, tip } = useTip({ says: translate(drawn.says), onPress: onSwitch });

    return (
        <div className={`shrink-0 pt-2.5 pb-1.5 ${folded ? 'px-2.25' : 'px-2.5'}`}>
            <button
                {...anchor}
                type="button"
                aria-label={translate(drawn.named)}
                className={`flex items-center rounded-field border border-line bg-panel text-lg font-semibold text-text shadow-raised transition hover:border-line-strong hover:bg-hover ${
                    folded ? 'mx-auto size-10 justify-center' : 'min-h-11 w-full gap-2.5 px-3 pointer-coarse:min-h-12'
                }`}
            >
                <Icon name={drawn.icon} className="size-icon-xl shrink-0 text-accent-deep" />

                {folded ? null : (
                    <>
                        <span className="min-w-0 flex-1 truncate text-start">{translate(drawn.label)}</span>
                        <Icon name="swap_vert" className="size-icon-md shrink-0 text-muted" />
                    </>
                )}
            </button>
            {tip}
        </div>
    );
}
