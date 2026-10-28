// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { ClientNotification } from '@mailfathom/client-backend';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { failureDecisionShapes } from './notificationKinds';
import type { RowDecision } from './NotificationRow';
import { wordNotification } from './notificationWords';

// What a notification row answers a press with. It is this row's items and nothing else: where the menu stands, how it
// is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s, which is the same component five other lists open.
//
// **Picking the row out is first**, because it is the one act a finger has no other route to — there is no modifier
// key to hold on a touch screen, so the menu is where a selection starts.
//
// **An item the client cannot yet perform is left out rather than drawn inert**, which is the rule the message row's
// own menu states. One item the design draws is absent under it: *open the source*, on a notification that
// names no target, because there is nothing to open.
//
// **A merged arrival offers *Summarize* beside opening it**, the same act as the control drawn on the row, because a
// finger holding the row is asking what it can do with it.
//
// **A failure offers its decisions here too**, the same ones its row draws as pills, for the reason Summarize is here.
//
// **Deleting is last and drawn as what it is.** It takes a row out of the centre for good, so it sits apart from the
// three acts above it and the question in front of it is the panel's rather than this menu's — a menu that closes as it
// is chosen from cannot also hold a dialog open.

export function NotificationRowMenu({
    notification,
    at,
    onSelect,
    onToggleRead,
    onOpen,
    onSummarize,
    decisions,
    onDelete,
    onClose,
}: {
    readonly notification: ClientNotification;
    readonly at: MenuPoint;

    /** Puts this row into the panel's own selection, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    readonly onToggleRead: () => void;

    /** Reads it and goes where it leads, which is what the row's own press does. */
    readonly onOpen: () => void;

    /** Hands the messages a merged arrival stands for to the agent, or `null` on every row that stands for none. */
    readonly onSummarize: (() => void) | null;

    /** What may be decided about the failure this row reports, which the row draws as well. */
    readonly decisions: readonly RowDecision[];

    /** Asks the panel to put the question in front of removing this row for good. */
    readonly onDelete: () => void;

    readonly onClose: () => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const writeRefused = useWriteRefusal();
    const marking = translate(notification.read ? 'notifications.markUnread' : 'notifications.markRead');
    const deleting = translate('notifications.delete');

    // Marking, deciding, and deleting each write, so while the server is out of reach they say so in place rather
    // than being chosen and failing; picking out, opening, and summarizing write nothing and stay as they are.
    const items: readonly ContextMenuItem[] = [
        { icon: 'check_box', label: translate('notifications.select'), choose: onSelect },
        {
            icon: notification.read ? 'mark_email_unread' : 'mark_email_read',
            label: marking,
            refused: writeRefused(marking),
            choose: onToggleRead,
        },
        ...(notification.target.kind === 'Nothing'
            ? []
            : [{ icon: 'open_in_new' as const, label: translate('notifications.openSource'), choose: onOpen }]),
        ...(onSummarize === null
            ? []
            : [{ icon: 'auto_awesome' as const, label: translate('notifications.summarize'), choose: onSummarize }]),
        ...decisions.map(({ decision, take, refused }) => ({
            icon: failureDecisionShapes[decision].icon,
            label: translate(failureDecisionShapes[decision].label),
            refused: refused ?? null,
            choose: take,
        })),
        { icon: 'delete', label: deleting, destroys: true, refused: writeRefused(deleting), choose: onDelete },
    ];

    // The row shows how long ago it happened; the instant itself stands here for a finger, which has no hover to ask.
    const happened = wordInstant(notification.occurredAt, locale, 'full', timeZone) ?? undefined;

    return (
        <ContextMenu
            header={wordNotification(notification, locale, translate).title}
            detail={happened}
            at={at}
            items={items}
            onClose={onClose}
        />
    );
}
