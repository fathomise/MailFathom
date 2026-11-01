// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';

// The mark the design puts on a colleague who uses the phone app: a small `smartphone` badge at the avatar's upper
// right, an icon of its own apart from the presence dot at its lower right, because a member who is *Offline* can carry
// it too. It is one shape in two sizes so the two places it stands cannot drift apart.
//
// **On a row it says what it means to a mouse resting on it**, and nothing to a finger, because a row's own hold opens
// its menu and one gesture opens one surface; the person's page says it in words beside the presence, which is where a
// finger reads it. On the page the badge is therefore hidden from the accessibility tree, the words standing for it.

const places = {
    contact: { size: 'size-3.5', icon: 'size-icon-3xs' },
    person: { size: 'size-4.75', icon: 'size-icon-xs' },
} as const;

export function PhoneAppBadge({ place }: { readonly place: keyof typeof places }) {
    const { translate } = useLocalization();
    const { anchor, tip } = useTip({ says: translate('presence.usesPhoneApp'), tapReveals: false });
    const { size, icon } = places[place];
    const shape = `absolute -end-0.75 -top-0.75 flex ${size} items-center justify-center rounded-full bg-panel text-muted ring-1 ring-line`;

    if (place === 'person') {
        return (
            <span aria-hidden="true" className={shape}>
                <Icon name="smartphone" className={icon} />
            </span>
        );
    }

    return (
        <>
            <span {...anchor} role="img" aria-label={translate('presence.usesPhoneApp')} className={shape}>
                <Icon name="smartphone" className={icon} />
            </span>
            {tip}
        </>
    );
}
