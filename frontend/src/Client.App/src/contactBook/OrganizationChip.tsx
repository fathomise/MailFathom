// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';

// The mark the design puts on a contact the reader's organization keeps in its own book: a small outlined chip naming
// the organization, at the end of the row and beside the name on the person's page. It is one shape in two sizes so the
// two places it stands cannot drift apart.
//
// **On the page it also says whose book the contact comes from**, to a mouse resting on it and a finger tapping it; on a
// row it says nothing more, because a row's own hold opens its menu and one gesture opens one surface.
//
// The name is the one the session answered with. Where it answered none — the organization deleted since, or a name the
// client could not read — the chip says *Organization* rather than the identifier the contact carries. A name may run to
// what an organization can carry, so the chip takes at most half its line and cuts the rest, and on the page the bubble
// still says it whole.

export function OrganizationChip({
    organizationName,
    place,
}: {
    readonly organizationName: string | null;
    readonly place: 'row' | 'page';
}) {
    const { translate } = useLocalization();
    const name = organizationName ?? translate('people.organization');
    const { anchor, tip } = useTip({
        says: translate('people.organizationBook', {
            organization: organizationName ?? translate('people.yourOrganization'),
        }),
        describes: true,
    });

    if (place === 'row') {
        return (
            <span className="ms-auto flex max-w-1/2 shrink-0 items-center gap-1 rounded-sm border border-line-strong px-1.5 py-px text-xs whitespace-nowrap text-text-soft">
                <Icon name="corporate_fare" className="size-icon-xs shrink-0" />
                <span className="truncate">{name}</span>
            </span>
        );
    }

    return (
        <>
            <span
                {...anchor}
                className="flex max-w-1/2 shrink-0 items-center gap-1.25 rounded-sm border border-line-strong px-1.75 py-0.5 text-xs font-semibold whitespace-nowrap text-text-soft"
            >
                <Icon name="corporate_fare" className="size-icon-xs shrink-0" />
                <span className="truncate">{name}</span>
            </span>
            {tip}
        </>
    );
}
