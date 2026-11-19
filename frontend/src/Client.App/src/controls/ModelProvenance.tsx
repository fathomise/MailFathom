// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useLocalization } from '../localization/useLocalization';
import { Icon } from './Icon';
import { useTip } from './useTip';

// The mark before text a model wrote: the one AI glyph, muted and inline, and not a control. The same glyph in the
// accent colour inside a control is an action that asks AI for something, so the colour is what tells the two apart and
// the glyph is what says AI — never an "AI" text badge, which repeats on every row of a list.
//
// It is named *Written by AI* for a screen reader and says so in the client's one bubble, except where the surface
// already announces the passage as a model's in a sentence of its own: there it is hidden from the accessibility tree,
// so the fact is said once rather than twice.

export function ModelProvenance({
    announced = true,
    inRow = false,
}: {
    /** Whether the mark is the passage's only statement that a model wrote it, and is therefore named. */
    readonly announced?: boolean;

    /** Whether it stands inside a row a tap already acts on, where a finger reads the fact in the row's menu instead. */
    readonly inRow?: boolean;
}) {
    const { translate } = useLocalization();
    const said = translate('reading.byModel');
    const { anchor, tip } = useTip({ says: said, tapReveals: !inRow });

    return (
        <>
            <span
                {...anchor}
                {...(announced ? { role: 'img', 'aria-label': said } : { 'aria-hidden': true })}
                className="inline-flex shrink-0 text-muted"
            >
                <Icon name="auto_awesome" className="size-icon-sm" />
            </span>

            {tip}
        </>
    );
}
