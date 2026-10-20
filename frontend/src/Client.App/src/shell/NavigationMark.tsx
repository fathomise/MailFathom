// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ReactNode } from 'react';

/**
 * Where the bottom bar marks the place being shown: a pill behind the symbol rather than a tile around the whole item,
 * because a tile the height of the bar reads as a pressed button and the bar is navigation. The rail marks its current
 * place on the item itself, so the pill is the bar's alone.
 */
export function NavigationMark({
    current,
    children,
    className = '',
}: {
    readonly current: boolean;
    readonly children: ReactNode;
    readonly className?: string;
}) {
    return (
        <span
            className={`${className} flex h-8 w-14 items-center justify-center rounded-xl transition workspace:h-auto workspace:w-auto workspace:rounded-none workspace:bg-transparent ${
                current ? 'bg-accent-soft' : ''
            }`}
        >
            {children}
        </span>
    );
}
