// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useId, useState, type ReactNode, type RefObject } from 'react';
import type {
    ClientFailureReason,
    ClientSession,
    MailFathomTransport,
    MailMessage,
    MailReceipts,
    MailRecipientReceipts,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { wordLastSeen } from '../localization/instants';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { receiptDetailsShape, type MarkAnchor } from '../markDetails/detailsPlacement';
import { MarkDetailsDialog } from '../markDetails/MarkDetailsDialog';
import { useAskedMessage } from '../markDetails/useAskedMessage';
import {
    deliveryOutcomeOf,
    receiptInk,
    receiptMarksDrawn,
    receiptPill,
    recipientNameOf,
    recipientOutcomeOf,
    recipientOutcomesDrawn,
    standingOfReceipts,
    wordOf,
} from './receiptMarks';

// What came back for one message the account sent: the mark and its word, what the message asked for, every recipient
// with their own outcome and a line for each report about them, and what a missing report does not mean.
//
// A row carries only the counts its mark is chosen from, so details opened from a row read the message when they open
// and say so while they do; details opened from a message already read are drawn from it at once — as the sender
// details are, in the same surface.

/** Whose receipts are being explained, and where the mark that asked stands. */
export interface AskedReceipts {
    readonly storedEmailId: string;

    /** The message where it has already been read, or `null` for details that read it themselves. */
    readonly message: MailMessage | null;

    /** Where the mark that opened them stands, or `null` where nothing on the screen did. */
    readonly anchor: MarkAnchor | null;
}

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

export function ReceiptDetails({
    asked,
    session,
    transport,
    dialog,
}: {
    /** The message being explained, or `null` while nothing is. */
    readonly asked: AskedReceipts | null;
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The dialog itself, opened by whichever surface asked, so that whether it is open is the element's own state. */
    readonly dialog: RefObject<HTMLDialogElement | null>;
}) {
    const { translate } = useLocalization();
    const names = useId();

    // Each press asks afresh, so each opening reads afresh: a second look shows a report that arrived since.
    const [shown, setShown] = useState({ asked, opening: 0 });

    if (shown.asked !== asked) {
        setShown({ asked, opening: shown.opening + 1 });
    }

    const close = (
        <SurfaceControl
            label={translate('receipt.close')}
            icon="close"
            onActivate={() => {
                dialog.current?.close();
            }}
        />
    );

    return (
        <MarkDetailsDialog
            dialog={dialog}
            anchor={asked?.anchor ?? null}
            shape={receiptDetailsShape}
            widthClass="desktop:w-90"
            names={names}
        >
            {asked === null ? (
                <h2 id={names} className="sr-only">
                    {translate('receipt.details')}
                </h2>
            ) : (
                <AskedReceiptDetails
                    key={shown.opening}
                    asked={asked}
                    session={session}
                    transport={transport}
                    names={names}
                    close={close}
                />
            )}
        </MarkDetailsDialog>
    );
}

function AskedReceiptDetails({
    asked,
    session,
    transport,
    names,
    close,
}: {
    readonly asked: AskedReceipts;
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly names: string;
    readonly close: ReactNode;
}) {
    const { translate } = useLocalization();
    const read = useAskedMessage(session, transport, asked.storedEmailId, asked.message);
    const receipts = read.message?.receipts ?? null;

    if (read.message === null || receipts === null) {
        return (
            <div className="flex flex-col gap-3 px-4.5 pt-1 pb-4.5 desktop:pt-4">
                <div className="flex items-start gap-3">
                    <h2 id={names} className="min-w-0 flex-1 pt-1 text-lg font-semibold">
                        {translate('receipt.details')}
                    </h2>

                    {close}
                </div>

                {read.message !== null ? (
                    <p className="text-base text-text-soft">{translate('receipt.asked.none')}</p>
                ) : read.failure === null ? (
                    <p className="text-base text-muted" role="status">
                        {translate('receipt.reading')}
                    </p>
                ) : (
                    <div className="flex flex-wrap items-center gap-1.5">
                        <p className="text-base text-warning" role="alert">
                            {translate('receipt.readFailed', {
                                reason: translate(failureLabels[read.failure.reason]),
                            })}
                        </p>

                        {read.failure.reason === 'unavailable' ? (
                            <SecondaryButton label={translate('connection.retry')} onActivate={read.readAgain} />
                        ) : null}
                    </div>
                )}
            </div>
        );
    }

    return <Receipts message={read.message} receipts={receipts} names={names} close={close} />;
}

function Receipts({
    message,
    receipts,
    names,
    close,
}: {
    readonly message: MailMessage;
    readonly receipts: MailReceipts;
    readonly names: string;
    readonly close: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());
    const standing = standingOfReceipts(receipts);
    const drawn = standing === null ? null : receiptMarksDrawn[standing.mark];
    // A delivered or a nothing-back mark is drawn in the neutral tone, so its title reads as plainly as any other.
    const titleInk =
        drawn === null || drawn.tone === 'plain' || drawn.tone === 'faint' ? 'text-text' : receiptInk[drawn.tone];
    const when = (instant: string): string => wordLastSeen(instant, locale, now, timeZone) ?? instant;
    const colleagues = receipts.recipients.some(fromTheOrganization);

    return (
        <>
            <div className="flex shrink-0 items-start gap-3 border-b border-line pt-1 pe-3 pb-3.5 ps-4.5 desktop:pt-4 desktop:pe-2.5">
                {drawn === null ? null : (
                    <span
                        aria-hidden="true"
                        className={`flex size-9 shrink-0 items-center justify-center rounded-field border ${receiptPill[drawn.tone]}`}
                    >
                        <Icon name={drawn.icon} className="size-icon-lg" />
                    </span>
                )}

                <div className="flex min-w-0 flex-1 flex-col gap-0.75 pt-px">
                    <h2 id={names} className={`text-lg/snug font-semibold ${titleInk}`}>
                        {standing === null ? translate('receipt.details') : wordOf(standing, translate, locale)}
                    </h2>
                    <p className="text-sm/normal text-pretty text-text-soft">{translate(askedFor(receipts))}</p>
                </div>

                {close}
            </div>

            <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto px-4.5 pt-3.5 pb-5.5 desktop:pb-4.5">
                <section aria-label={translate('receipt.recipients')} className="flex flex-col gap-1.5">
                    <h3 aria-hidden="true" className="text-xs/natural font-semibold text-muted">
                        {translate('receipt.recipients')}
                    </h3>

                    <ul className="flex flex-col rounded-lg border border-line bg-sunken px-3.5">
                        {receipts.recipients.map((recipient) => (
                            <Recipient
                                key={recipient.address}
                                recipient={recipient}
                                name={recipientNameOf(recipient.address, message.headers.participants)}
                                lines={recipientLines(recipient, receipts, when, translate)}
                            />
                        ))}
                    </ul>
                </section>

                {receipts.readRequested ? (
                    <p className="text-sm/normal text-pretty text-muted">{translate('receipt.note.read')}</p>
                ) : null}

                {receipts.deliveryRequested ? (
                    <p className="text-sm/normal text-pretty text-muted">{translate('receipt.note.delivery')}</p>
                ) : null}

                {colleagues ? (
                    <p className="text-sm/normal text-pretty text-muted">{translate('receipt.note.organization')}</p>
                ) : null}
            </div>
        </>
    );
}

function Recipient({
    recipient,
    name,
    lines,
}: {
    readonly recipient: MailRecipientReceipts;
    readonly name: string;
    readonly lines: readonly string[];
}) {
    const { translate } = useLocalization();
    const outcome = recipientOutcomesDrawn[recipientOutcomeOf(recipient)];

    return (
        <li className="flex flex-col gap-0.75 border-t border-line-soft py-2.5 first:border-t-0">
            <div className="flex min-w-0 items-center gap-2.5">
                <span className="min-w-0 flex-1 truncate text-base/natural font-semibold text-text">{name}</span>

                <span
                    className={`flex shrink-0 items-center gap-1 text-sm/natural font-semibold whitespace-nowrap ${receiptInk[outcome.tone]}`}
                >
                    <Icon name={outcome.icon} className="size-icon-sm" />
                    {translate(outcome.word)}
                </span>
            </div>

            {name === recipient.address ? null : (
                <p className="text-sm/natural wrap-anywhere text-muted">{recipient.address}</p>
            )}

            {lines.map((line) => (
                <p key={line} className="text-sm/normal text-pretty wrap-anywhere text-text-soft">
                    {line}
                </p>
            ))}
        </li>
    );
}

function askedFor(receipts: MailReceipts): MessageKey {
    if (receipts.deliveryRequested && receipts.readRequested) {
        return 'receipt.asked.both';
    }

    if (receipts.deliveryRequested) {
        return 'receipt.asked.delivery';
    }

    return receipts.readRequested ? 'receipt.asked.read' : 'receipt.asked.none';
}

// A colleague on this deployment is marked by MailFathom itself rather than by a report.
function fromTheOrganization(recipient: MailRecipientReceipts): boolean {
    return recipient.delivery?.source === 'Organization' || recipient.read?.source === 'Organization';
}

/** A line for each report about one recipient, and one for each thing asked that has not come back. */
function recipientLines(
    recipient: MailRecipientReceipts,
    receipts: MailReceipts,
    when: (instant: string) => string,
    translate: Translate,
): readonly string[] {
    const { delivery, read } = recipient;
    const displayed = read?.outcome === 'Displayed' ? read : null;

    if (fromTheOrganization(recipient)) {
        return [
            ...(displayed === null ? [] : [translate('receipt.line.opened', { at: when(displayed.reportedAt) })]),
            ...(delivery === null
                ? []
                : [translate('receipt.line.organizationDelivered', { at: when(delivery.reportedAt) })]),
        ];
    }

    const delivered = delivery === null ? null : deliveryOutcomeOf(delivery.outcome);
    const lines: string[] = [];

    if (displayed !== null) {
        lines.push(translate('receipt.line.displayed', { at: when(displayed.reportedAt) }));
    }

    if (delivery !== null && delivered === 'delivered') {
        lines.push(
            delivery.reportingServer === null
                ? translate('receipt.line.deliveredUnnamed', { at: when(delivery.reportedAt) })
                : translate('receipt.line.delivered', {
                      at: when(delivery.reportedAt),
                      server: delivery.reportingServer,
                  }),
        );
    }

    if (delivery !== null && (delivered === 'failed' || delivered === 'delayed')) {
        if (delivery.status !== null) {
            lines.push(translate('receipt.line.status', { status: delivery.status }));
        }

        lines.push(
            delivery.reportingServer === null
                ? translate('receipt.line.reportedUnnamed', { at: when(delivery.reportedAt) })
                : translate('receipt.line.reported', {
                      at: when(delivery.reportedAt),
                      server: delivery.reportingServer,
                  }),
        );
    }

    if (receipts.deliveryRequested && delivery === null) {
        lines.push(translate('receipt.line.noDelivery'));
    }

    if (receipts.readRequested && displayed === null && delivered !== 'failed') {
        lines.push(translate('receipt.line.noRead'));
    }

    return lines;
}
