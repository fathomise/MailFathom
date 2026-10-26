// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import { PlannedControl } from '../controls/PlannedControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { allDrawnSpam, useMailboxActs, type ActedMessage } from '../mailboxActs/useMailboxActs';

// The one spam control, as a strip, the toolbar, and a message's head draw it. It reads *Not spam* while every message
// under it is drawn spam and the other way otherwise, so a mix reads as the way in and is refused with nothing sent when
// it is pressed — the refusal is `toggleSpam`'s, which every surface reaches the same way.

export function SpamControl({
    messages,
    shape,
    wording,
    onActed,
}: {
    readonly messages: readonly ActedMessage[];
    readonly shape: ControlShape;

    /** `strip` names the way in *Spam*, which a selection bar has room for; `mark` names it *Mark spam*. */
    readonly wording: 'strip' | 'mark';

    readonly onActed?: (() => void) | undefined;
}) {
    const { translate } = useLocalization();
    const acts = useMailboxActs();
    const out = allDrawnSpam(acts, messages);
    const label = translate(out ? 'spam.notSpam' : wording === 'strip' ? 'spam.spam' : 'spam.markSpam');
    const icon = out ? 'report_off' : 'report';

    // The way in is drawn in the error tone, as the design draws it; the way out is an ordinary act.
    const tone = out ? '' : 'text-error-text';

    const refusal: MessageKey | null = !acts.spamOffered
        ? 'act.notOffered'
        : messages.length === 0
          ? 'act.nothingToActOn'
          : null;

    if (refusal !== null) {
        return <PlannedControl label={label} icon={icon} shape={shape} why={translate(refusal, { control: label })} />;
    }

    return (
        <Control
            label={label}
            icon={icon}
            shape={shape}
            className={tone}
            onPress={() => {
                acts.toggleSpam(messages, onActed);
            }}
        />
    );
}
