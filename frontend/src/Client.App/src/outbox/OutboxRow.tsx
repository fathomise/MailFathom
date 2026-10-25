// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import type { OutgoingMail } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { ReceivedAt } from '../controls/ReceivedAt';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { addresseeOf, standingLine, standingOf, standingShapes, toneInk } from './outboxStanding';

// One send in an Outbox, as the design draws it: whom it is for, when it was handed over, what it is about, and where
// it stands. Pressing it opens it, and that is all it does — the row is read-only by what an Outbox is, so it carries
// none of what a message row carries: no checkbox, no selection, no hover acts, no drag, no swipe, and no menu.

export function OutboxRow({
    send,
    open,
    onOpen,
}: {
    readonly send: OutgoingMail;

    /** Whether this is the send the pane beside the list is reading. */
    readonly open: boolean;

    readonly onOpen: () => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());
    const wording = { locale, timeZone, translate, now };
    const standing = standingOf(send);

    if (standing === null) {
        return null;
    }

    const shape = standingShapes[standing];
    const decisive = standing === 'unknown' || standing === 'refused';

    return (
        <li>
            <button
                type="button"
                aria-current={open ? 'true' : undefined}
                onClick={onOpen}
                // The open send is marked at the edge, as an open message is, which keeps the row's lines where they were.
                className={`flex w-full cursor-pointer flex-col justify-center gap-1.5 border-s-4 ps-3 pe-4 py-3.5 text-start shadow-raised transition max-workspace:min-h-26 workspace:gap-1.25 workspace:ps-2.5 workspace:pe-3.5 workspace:py-3 ${
                    open ? 'border-s-accent bg-accent-soft' : 'border-s-transparent bg-panel hover:bg-hover'
                }`}
            >
                <span className="flex min-w-0 items-center gap-2">
                    <span className="shrink-0 text-sm text-muted">{translate('outbox.to')}</span>
                    <span className="min-w-0 flex-1 truncate text-md leading-4.5 font-semibold">
                        {addresseeOf(send.recipients, send.otherRecipientCount, wording)}
                    </span>
                    <ReceivedAt at={send.recordedAt} />
                </span>

                <span className="truncate text-base leading-4.25 text-text-soft">
                    {send.subject ?? translate('message.noSubject')}
                </span>

                <span
                    className={`flex min-w-0 items-center gap-1.25 text-sm leading-4 ${decisive ? 'font-semibold' : ''} ${toneInk[shape.tone]}`}
                >
                    <Icon name={shape.icon} className={`size-icon-xs ${shape.moving ? 'animate-working' : ''}`} />
                    <span className="truncate">{standingLine(standing, send, wording)}</span>
                </span>
            </button>
        </li>
    );
}
