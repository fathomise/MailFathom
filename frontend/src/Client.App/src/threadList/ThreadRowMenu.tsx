// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ListedMailThread } from '@mailfathom/client-backend';
import { categoriesDetail, drawnCategories } from '../categories/categoryWords';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';
import { actsDrawn, actsSaidInAMenu } from '../mailboxActs/drawnActs';
import { useMailboxActs } from '../mailboxActs/useMailboxActs';
import { useAgentHandOver } from '../routing/agentHandOver';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { offeredOver, readActOver, type ConversationAct } from './conversationMessages';
import { participantsNamed } from './threadRows';

// What a conversation's row offers, which is what is done to the whole conversation and nothing that belongs to one
// message of it: no summary, no answer, no flag and no details — those stay on a message inside the opened
// conversation. Which of its messages each act takes is `conversationMessages.ts`'s to say.
//
// The mailbox acts stand in the order a message row's menu draws them, with the spam mark before *Move* and the one
// that stands behind a question about destroying last.
const actsInAThreadMenu: readonly ConversationAct[] = ['archive', 'markUnread', 'spam', 'move', 'delete'];

export function ThreadRowMenu({
    thread,
    at,
    onSelect,
    onAsk,
    onClose,
}: {
    readonly thread: ListedMailThread;
    readonly at: MenuPoint;

    /** Puts this row into the selection, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    /** Asks one act of this conversation. */
    readonly onAsk: (act: ConversationAct) => void;

    readonly onClose: () => void;
}) {
    const { locale, translate } = useLocalization();
    const acts = useMailboxActs();
    const handToAgent = useAgentHandOver();
    const writeRefused = useWriteRefusal();
    const subject = thread.newest.subject ?? translate('list.noSubject');

    // Who took part, and every category the row draws at most two of: a finger has no hover, and the hold that would
    // ask a label for its whole name is what opened this menu.
    const categories = drawnCategories(thread.categories, translate);
    const people = new Intl.ListFormat(locale, { style: 'short', type: 'unit' }).format(
        participantsNamed(thread, translate('threadList.me')),
    );

    const offered = actsInAThreadMenu
        .map((slot) => (slot === 'markUnread' ? readActOver([thread]) : slot))
        // An act this credential or this account cannot carry is left out, as a message row's menu leaves it out, and
        // one refused for want of a connection stays and says so.
        .filter((act) => {
            const refusal = offeredOver(acts, act, [thread]);

            return refusal === null || refusal === 'noConnection';
        })
        .map((act): ContextMenuItem => {
            const label = translate(act === 'spam' ? 'spam.spamAsking' : actsSaidInAMenu[act]);

            return {
                icon: act === 'spam' ? 'report' : actsDrawn[act].icon,
                label,
                refused: writeRefused(label),
                destroys: act === 'delete',
                choose: () => {
                    onAsk(act);
                },
            };
        });

    return (
        <ContextMenu
            header={subject}
            detail={categoriesDetail(people, categories, translate)}
            detailWraps={categories.length > 0}
            at={at}
            items={[
                { icon: 'check_box', label: translate('threadList.selectThreads'), choose: onSelect },
                ...(handToAgent === null
                    ? []
                    : [
                          {
                              icon: 'auto_awesome' as const,
                              label: translate('agent.askAgent'),
                              choose: () => {
                                  handToAgent({ scope: { kind: 'thread', subject: thread.threadId }, title: subject });
                              },
                          },
                      ]),
                ...offered,
            ]}
            onClose={onClose}
        />
    );
}
