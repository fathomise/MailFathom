// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ListedMailThread } from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import type { ControlShape } from '../controls/controlShapes';
import { PlannedControl } from '../controls/PlannedControl';
import { useLocalization } from '../localization/useLocalization';
import { actsDrawn, refusalSaid } from '../mailboxActs/drawnActs';
import { useMailboxActs } from '../mailboxActs/useMailboxActs';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { offeredOver, readActOver, type ConversationAct } from './conversationMessages';

// The acts over a selection of conversations, drawn where `mailboxActs/MailboxActControls.tsx` draws a selection of
// messages' and in its order — less the flag, which is a message's — with the read control going the way the rows
// under it say. A control that cannot act says so before it is pressed, as that strip's do.
const actsOnAThreadStrip: readonly ConversationAct[] = ['archive', 'delete', 'markUnread', 'spam', 'move'];

export function ConversationActControls({
    threads,
    shape,
    onAsk,
}: {
    /** The conversations every one of these acts is about. */
    readonly threads: readonly ListedMailThread[];

    readonly shape: ControlShape;

    /** Asks one act of them, which the list they were picked out of carries out. */
    readonly onAsk: (act: ConversationAct) => void;
}) {
    const { translate } = useLocalization();
    const acts = useMailboxActs();
    const writeRefused = useWriteRefusal();

    return (
        <>
            {actsOnAThreadStrip.map((slot) => {
                const act = slot === 'markUnread' ? readActOver(threads) : slot;
                const label = translate(act === 'spam' ? 'spam.spam' : actsDrawn[act].label);
                const icon = act === 'spam' ? 'report' : actsDrawn[act].icon;
                const refusal = offeredOver(acts, act, threads);
                const why =
                    refusal !== null && refusal !== 'noConnection'
                        ? translate(refusalSaid[refusal], { control: label })
                        : writeRefused(label);

                return why === null ? (
                    <Control
                        key={slot}
                        label={label}
                        icon={icon}
                        shape={shape}
                        className={act === 'spam' ? 'text-error-text' : ''}
                        onPress={() => {
                            onAsk(act);
                        }}
                    />
                ) : (
                    <PlannedControl key={slot} label={label} icon={icon} shape={shape} why={why} />
                );
            })}
        </>
    );
}
