// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef } from 'react';
import type { ClientSession, MailFathomTransport, MailTimelineEntry } from '@mailfathom/client-backend';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useLocalization } from '../localization/useLocalization';
import { readingsOf } from '../messageRows/messageReadings';
import { ReadingsAsked } from '../messageRows/ReadingsAsked';

// What MailFathom read from a conversation, opened from the head of its newest received message — however the list is
// read, and the one place a conversation offers it, since a row of the list read by conversation draws none. It opens
// the same dialog a message row's menu does, over the readings of that message, and is offered only where there are
// readings to open.

export function SummaryControl({
    email,
    session,
    transport,
}: {
    readonly email: MailTimelineEntry;
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
}) {
    const { translate } = useLocalization();
    const dialog = useRef<HTMLDialogElement>(null);
    const marks = readingsOf(email.enrichment);

    if (marks.length === 0) {
        return null;
    }

    return (
        <>
            <SurfaceControl
                label={translate('reading.check')}
                icon="auto_awesome"
                onActivate={() => {
                    dialog.current?.showModal();
                }}
            />

            <ReadingsAsked
                asked={{ storedEmailId: email.id, subject: email.subject, marks }}
                session={session}
                transport={transport}
                dialog={dialog}
            />
        </>
    );
}
