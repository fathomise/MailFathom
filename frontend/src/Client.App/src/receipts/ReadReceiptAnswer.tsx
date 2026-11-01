// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, useState } from 'react';
import { flushSync } from 'react-dom';
import {
    sendReadReceipt,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
    type MailReadReceiptRequest,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import { wordLastSeen } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useWriteRefusal } from '../shell/useWriteRefusal';

// A received message asking the reader for a read receipt. MailFathom answers one only when the reader says so, once,
// and keeps saying afterwards that it did: the offer stands until it is pressed, and what replaces it for good is one
// quiet line naming when the receipt went and where. Nothing sends one on its own — opening the message is not consent.
//
// A message marked spam is offered nothing, whatever it asks, because answering would confirm to its sender that the
// address reads mail; the deployment withholds the request on a spam verdict, and the surface drawing this withholds
// it on the reader's own mark from this session as well.

/** Where answering the request stands within the message's life on the screen. */
type Answering =
    | { readonly phase: 'offered' | 'sending' }
    | { readonly phase: 'failed'; readonly reason: MessageKey }
    | { readonly phase: 'sent'; readonly sentAt: string; readonly arrived: boolean };

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

export function ReadReceiptAnswer({
    session,
    transport,
    storedEmailId,
    request,
    sender,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly storedEmailId: string;
    readonly request: MailReadReceiptRequest;

    /** Who asked, as the message names its author. */
    readonly sender: string;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());
    const [answering, setAnswering] = useState<Answering>(
        request.sentAt === null ? { phase: 'offered' } : { phase: 'sent', sentAt: request.sentAt, arrived: false },
    );
    const sentLine = useRef<HTMLParagraphElement>(null);
    const writeRefused = useWriteRefusal();
    const label = translate('readReceipt.send');
    const refused = writeRefused(label);
    const said = useTip({ says: refused ?? label, describes: true });

    async function send(): Promise<void> {
        setAnswering({ phase: 'sending' });

        const result = await sendReadReceipt(session, transport, storedEmailId);

        if (result.outcome === 'failed') {
            setAnswering({ phase: 'failed', reason: failureLabels[result.failure.reason] });

            return;
        }

        const sent = result.value;

        if (sent.outcome === 'refused') {
            setAnswering({ phase: 'failed', reason: 'readReceipt.refused' });

            return;
        }

        // The button that was pressed is gone, so focus goes to the line that took its place rather than to the top of
        // the document.
        flushSync(() => {
            setAnswering({ phase: 'sent', sentAt: sent.sentAt, arrived: true });
        });
        sentLine.current?.focus();
    }

    if (answering.phase === 'sent') {
        return (
            <p
                ref={sentLine}
                tabIndex={-1}
                role={answering.arrived ? 'status' : undefined}
                className="flex items-start gap-2 text-sm text-muted outline-none"
            >
                <Icon name="mark_email_read" className="size-icon-sm shrink-0" />
                <span className="wrap-anywhere">
                    {translate('readReceipt.sent', {
                        at: wordLastSeen(answering.sentAt, locale, now, timeZone) ?? answering.sentAt,
                        address: request.address,
                    })}
                </span>
            </p>
        );
    }

    const sending = answering.phase === 'sending';
    const held = sending || refused !== null;

    return (
        <aside className="flex flex-wrap items-center gap-x-3 gap-y-2.5 rounded-lg border border-line bg-sunken px-3.5 py-2.75 pointer-coarse:py-3">
            <Icon name="mark_email_read" className="size-icon-lg shrink-0 self-start text-muted" />

            <div className="flex min-w-0 flex-1 basis-60 flex-col gap-0.5">
                <p className="text-base text-pretty wrap-anywhere text-text">
                    {request.namesAnotherAddress
                        ? translate('readReceipt.askedElsewhere', { sender, address: request.address })
                        : translate('readReceipt.asked', { sender })}
                </p>
                <p className="text-sm text-pretty text-muted">{translate('readReceipt.note')}</p>

                {answering.phase === 'failed' ? (
                    <p role="alert" className="text-sm text-pretty text-error-text">
                        {translate('readReceipt.failed', { reason: translate(answering.reason) })}
                    </p>
                ) : null}
            </div>

            <button
                {...(refused === null ? {} : said.anchor)}
                type="button"
                aria-disabled={held ? true : undefined}
                className={`ms-auto inline-flex h-8 shrink-0 items-center justify-center rounded-md border border-line-strong bg-panel px-3.25 text-base whitespace-nowrap text-text-soft transition hover:bg-hover aria-disabled:opacity-60 ${
                    sending ? 'aria-disabled:cursor-progress' : 'aria-disabled:cursor-not-allowed'
                }`}
                onClick={(event) => {
                    if (refused !== null) {
                        said.anchor.onClick(event);

                        return;
                    }

                    if (!held) {
                        void send();
                    }
                }}
            >
                {translate(sending ? 'readReceipt.sending' : 'readReceipt.send')}
            </button>
            {refused === null ? null : said.tip}
        </aside>
    );
}
