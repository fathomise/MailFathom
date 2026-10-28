// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { ContextMenu } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';
import { useWriteRefusal } from '../shell/useWriteRefusal';

/**
 * What a conversation row offers when it is pressed and held: picking it out, opening it, putting it away or taking it
 * back out of the archive, and deleting it.
 */
export function ConversationRowMenu({
    title,
    archived,
    at,
    onSelect,
    onOpen,
    onArchive,
    onAskDeletion,
    onClose,
}: {
    readonly title: string;

    /** Whether the conversation is archived, which turns archiving it into restoring it. */
    readonly archived: boolean;

    readonly at: MenuPoint;
    readonly onSelect: () => void;
    readonly onOpen: () => void;
    readonly onArchive: () => void;
    readonly onAskDeletion: () => void;
    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const writeRefused = useWriteRefusal();
    const archiving = translate(archived ? 'agent.restoreConversation' : 'agent.archiveConversation');
    const deleting = translate('agent.deleteConversation');

    // Picking out and opening read nothing new; archiving, restoring, and deleting each write, so while the server is
    // out of reach those say so in place rather than being pressed and failing.
    return (
        <ContextMenu
            header={title}
            at={at}
            items={[
                { icon: 'check_box', label: translate('agent.selectConversations'), choose: onSelect },
                { icon: 'forum', label: translate('agent.openConversation'), choose: onOpen },
                {
                    icon: archived ? 'unarchive' : 'archive',
                    label: archiving,
                    refused: writeRefused(archiving),
                    choose: onArchive,
                },
                {
                    icon: 'delete',
                    label: deleting,
                    destroys: true,
                    refused: writeRefused(deleting),
                    choose: onAskDeletion,
                },
            ]}
            onClose={onClose}
        />
    );
}
