// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { PresenceState } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { presenceSaid } from './presenceWords';

// A member's presence as a dot: laid over an avatar's lower right and ringed in the surface colour so it reads on a
// photo, or standing at the start of a line that names the state in words. *Active* is the healthy green, *Away* the
// warning amber, *Busy* the error red, *Do not disturb* that red crossed by a bar, *Offline* a ring, and *Hidden* —
// which only the member themselves is ever drawn as — a ring struck through.
//
// **The dot is never the only telling.** Everywhere the design draws one, the state is said in words beside it, so the
// dot is hidden from the accessibility tree. The one place it stands alone — the account control in the rail — the
// words are held beside it for that control to be described by.

/** Where a dot stands, which decides its size: the design draws eight. */
export type PresenceDotPlace = 'contact' | 'person' | 'rail' | 'head' | 'sheet' | 'line' | 'header' | 'suggestion';

// Over an avatar, ringed; or inline at the start of a line. The ring a dot for *Offline* is drawn as thickens with the
// dot, as the design draws it: two pixels up to 11, three from 13.
const places: Readonly<
    Record<PresenceDotPlace, { readonly size: string; readonly over: boolean; readonly thick: boolean }>
> = {
    contact: { size: 'size-2.75', over: true, thick: false },
    person: { size: 'size-3.75', over: true, thick: true },
    rail: { size: 'size-3.25 pointer-coarse:size-3.5', over: true, thick: true },
    head: { size: 'size-2.75 max-workspace:size-3.25', over: true, thick: false },
    sheet: { size: 'size-3.25', over: true, thick: true },
    line: { size: 'size-2.5', over: false, thick: false },
    header: { size: 'size-2.25', over: false, thick: false },
    suggestion: { size: 'size-2.5', over: true, thick: false },
};

const fills: Readonly<Record<PresenceState, string>> = {
    Active: 'bg-healthy',
    Away: 'bg-warning',
    Busy: 'bg-error',
    DoNotDisturb: 'presence-crossed',
    Offline: 'bg-panel inset-ring-faint',
    Hidden: 'presence-struck inset-ring-faint',
};

// The two that are a ring take its width; the others draw none.
function ringOf(presence: PresenceState, thick: boolean): string {
    if (presence !== 'Offline' && presence !== 'Hidden') {
        return '';
    }

    return thick ? 'inset-ring-3' : 'inset-ring-2';
}

export function PresenceDot({
    presence,
    place,
    id,
}: {
    readonly presence: PresenceState;
    readonly place: PresenceDotPlace;

    /** States the presence in words under this identity, for a control the dot stands on alone to be described by. */
    readonly id?: string;
}) {
    const { translate } = useLocalization();
    const { size, over, thick } = places[place];
    const shape = `${size} shrink-0 rounded-full ${fills[presence]} ${ringOf(presence, thick)} ${
        over ? 'absolute -end-px -bottom-px ring-2 ring-panel' : ''
    }`;

    // The word is held hidden rather than put on the dot as a label: a description is read from the text of what it
    // names, and a hidden element named by one is still read.
    return (
        <>
            <span aria-hidden="true" className={shape} />
            {id === undefined ? null : (
                <span id={id} hidden>
                    {translate(presenceSaid[presence])}
                </span>
            )}
        </>
    );
}
