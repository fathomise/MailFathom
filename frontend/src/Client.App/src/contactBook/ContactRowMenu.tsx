// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import type { Contact } from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { ContextMenu, type ContextMenuItem } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { useLocalization } from '../localization/useLocalization';
import { useWriteRefusal } from '../shell/useWriteRefusal';
import { useAddressCopy } from './useAddressCopy';
import type { Person } from './useContactBook';

// What a contact's row offers, which is this list's own items handed to the one menu six lists share: where the menu
// stands, how it is walked, and how it is left are `contextMenu/ContextMenu.tsx`'s.
//
// **An item the client cannot yet perform is left out rather than drawn inert**, which is the rule
// `messageRows/MessageRowMenu.tsx` states and the reason two of the design's five items are absent here: *write
// a message* is absent where the credential may not write a draft, and *propose a meeting* is absent altogether,
// because the Calendar space is a placeholder and a menu item that opens nothing is worse than no item at all. It
// arrives with that screen rather than being drawn ahead of it.
//
// **Nothing here performs the erasure.** The question it stands behind outlives the menu — the menu is gone the moment
// an item is chosen — so what this does is ask the screen to raise it.
//
// **A colleague and a contact the reader's organization keeps offer no erasure**, because both are read-only to a
// member and the press would answer as a contact the books do not hold. Each offers *Copy address* in that place, as
// the design draws.

export function ContactRowMenu({
    contact,
    at,
    erasable,
    onSelect,
    onOpen,
    onAskErasure,
    onClose,
}: {
    readonly contact: Person;
    readonly at: MenuPoint;

    /** Whether this credential may take somebody out of the book at all. */
    readonly erasable: boolean;

    /** Puts this row into the selection the list holds, which is how a finger reaches one at all. */
    readonly onSelect: () => void;

    readonly onOpen: () => void;

    /** Raises the question the erasure stands behind, over this one person, who is one of the reader's own books'. */
    readonly onAskErasure: (contact: Contact) => void;

    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const composing = useComposing();
    const writeRefused = useWriteRefusal();
    const copy = useAddressCopy();
    const address = contact.preferredAddress;

    const items: ContextMenuItem[] = [
        { icon: 'check_box', label: translate('people.selectContacts'), choose: onSelect },
    ];

    if (composing.offered && address !== null) {
        items.push({
            icon: 'edit',
            label: translate(contact.book === 'Member' ? 'people.writeEmail' : 'people.writeMessage'),
            choose: () => {
                composing.compose({ kind: 'new', to: [address] });
            },
        });
    }

    items.push({ icon: 'person', label: translate('people.openContact'), choose: onOpen });

    if (contact.book === 'Member' || contact.book === 'Organization') {
        if (address !== null) {
            items.push({
                icon: 'content_copy',
                label: translate('people.copyAddress'),
                choose: () => {
                    copy([address]);
                },
            });
        }
    } else if (erasable) {
        // Said as the person's page says it: a contact somebody wrote down is deleted, and one their mail collected is
        // removed from the collected book. Either writes, so while the server is out of reach it stays here saying why
        // it cannot act.
        const erasure = translate(contact.book === 'Collected' ? 'people.removeFromCollected' : 'people.deleteContact');

        items.push({
            icon: 'delete',
            label: erasure,
            destroys: true,
            refused: writeRefused(erasure),
            choose: () => {
                onAskErasure(contact);
            },
        });
    }

    return <ContextMenu header={contact.displayName} at={at} items={items} onClose={onClose} />;
}
