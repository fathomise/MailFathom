// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId } from 'react';
import type { OwnPresence } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { PersonAvatar } from '../controls/PersonAvatar';
import { useLocalization } from '../localization/useLocalization';
import { PresenceDot } from './PresenceDot';
import { presenceSaid } from './presenceWords';
import { openStatusMenu } from './statusMenuOpening';

// The row that leads to the status menu: under the member's name in the user menu, and at the head of the *More* sheet
// on a phone, where it carries the avatar. It names the state in words — *Hidden* saying that others see the member as
// *Offline* — and under it the status message, or *Set a status message* where there is none. A reader outside an
// organization has no presence, and the frame draws no row for them.

export function OwnPresenceRow({
    presence,
    place,
    displayName,
    picture,
}: {
    /** What the member reads as to themselves, and what they tell the others. */
    readonly presence: OwnPresence;

    readonly place: 'menu' | 'sheet';

    /** What this deployment records the member as called, which the sheet's avatar is drawn from. */
    readonly displayName: string | null;

    /** Where their picture may be drawn from, or `null` where they have none. */
    readonly picture: string | null;
}) {
    const { translate } = useLocalization();
    const stated = useId();
    const said = useId();
    const words = translate(presence.state === 'Hidden' ? 'presence.hiddenOwn' : presenceSaid[presence.state]);
    const message = presence.statusMessage?.text ?? null;

    return (
        <button
            type="button"
            aria-label={translate('presence.yours')}
            aria-describedby={`${stated} ${said}`}
            className={
                place === 'menu'
                    ? '-mx-1.5 mt-1 mb-0.5 flex items-center gap-2.25 rounded-md p-1.5 text-start transition hover:bg-hover'
                    : 'mb-1 flex min-h-13 w-full items-center gap-3 rounded-xl border-b border-line-soft px-2.5 py-1.5 text-start transition hover:bg-hover'
            }
            onClick={(pressed) => {
                openStatusMenu(pressed.currentTarget);
            }}
        >
            {place === 'menu' ? (
                <PresenceDot presence={presence.state} place="line" />
            ) : (
                <span className="relative shrink-0">
                    <PersonAvatar displayName={displayName} picture={picture} place="menu" />
                    <PresenceDot presence={presence.state} place="sheet" />
                </span>
            )}

            <span className={`flex min-w-0 flex-1 flex-col ${place === 'menu' ? 'gap-px' : 'gap-0.5'}`}>
                <span id={stated} className={`truncate ${place === 'menu' ? 'text-sm' : 'text-lg'}`}>
                    {words}
                </span>
                <span
                    id={said}
                    className={`truncate ${place === 'menu' ? 'text-xs' : 'text-base'} ${
                        message === null ? 'text-faint' : 'text-text-soft'
                    }`}
                >
                    {message ?? translate('presence.setMessage')}
                </span>
            </span>

            <Icon
                name="chevron_right"
                className={`${place === 'menu' ? 'size-icon-md' : 'size-icon-xl'} shrink-0 text-faint`}
            />
        </button>
    );
}
