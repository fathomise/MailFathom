// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, useState, type ReactNode } from 'react';
import { ContextMenu } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { Icon } from '../controls/Icon';
import { SymbolControl } from '../controls/SymbolControl';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { Departing } from '../shell/Departing';
import type { Composition } from './composition';

// *Sending options*, the three-dot control at the end of the composer's foot, and the two requests one message makes
// from it: a delivery notification and a read receipt. While either is on it is said twice, so nobody sends unaware of
// it — the control stands on the accent ground, and a line directly above the foot says in words what will be asked
// and opens the same menu.
//
// Two places on the screen open one menu, so this answers the pieces rather than one element, the way `useTip` does:
// the composer puts the line above its foot and the control in it. Focus goes back to whichever opened the menu, and to
// the control where that was the line and the line went because both requests were turned off.

/** The two requests, as the composition holds them. */
export type ReceiptRequests = Pick<Composition, 'requestsDeliveryNotification' | 'requestsReadReceipt'>;

export interface SendingOptions {
    /** The line above the foot, or nothing while neither request is on. */
    readonly line: ReactNode;

    /** The control at the end of the foot, with the menu it opens. */
    readonly control: ReactNode;
}

type Asking = 'none' | 'deliveryNotification' | 'readReceipt' | 'both';

const controlNames: Readonly<Record<Asking, MessageKey>> = {
    none: 'compose.sendingOptions',
    deliveryNotification: 'compose.sendingOptionsAskDeliveryNotification',
    readReceipt: 'compose.sendingOptionsAskReadReceipt',
    both: 'compose.sendingOptionsAskBoth',
};

const lineSays: Readonly<Record<Exclude<Asking, 'none'>, MessageKey>> = {
    deliveryNotification: 'compose.asksDeliveryNotification',
    readReceipt: 'compose.asksReadReceipt',
    both: 'compose.asksBoth',
};

export function useSendingOptions({
    requested,
    onTurned,
}: {
    readonly requested: ReceiptRequests;

    /** Turns one request over, which the composition the requests belong to takes. */
    readonly onTurned: (change: Partial<ReceiptRequests>) => void;
}): SendingOptions {
    const { translate } = useLocalization();
    const control = useRef<HTMLButtonElement | null>(null);
    const line = useRef<HTMLButtonElement | null>(null);
    const [menu, setMenu] = useState<{ readonly at: MenuPoint; readonly fromLine: boolean } | null>(null);
    const asking = askingOf(requested);
    const readReceiptNote = requested.requestsReadReceipt ? translate('compose.readReceiptMayNotCome') : null;

    // The menu stands above whatever opened it with its end edge on that element's, since the foot is the composer's
    // last row and there is nothing below it to open into.
    function open(fromLine: boolean): void {
        const opener = (fromLine ? line : control).current?.getBoundingClientRect();

        setMenu({ at: opener === undefined ? { x: 0, y: 0 } : { x: opener.right, y: opener.top - 4 }, fromLine });
    }

    const lineTip = useTip({
        says: translate('compose.changeSendingOptions'),
        describes: true,
        onPress: () => {
            open(true);
        },
    });

    return {
        line:
            asking === 'none' ? null : (
                <>
                    <button
                        {...lineTip.anchor}
                        ref={(element) => {
                            lineTip.anchor.ref(element);
                            line.current = element;
                        }}
                        type="button"
                        className="flex w-full items-start gap-2 border-t border-line bg-panel px-3.75 py-1.75 text-start text-base/normal text-pretty text-text-soft transition hover:bg-hover pointer-coarse:min-h-11 pointer-coarse:py-1.5"
                    >
                        <Icon
                            name={requested.requestsReadReceipt ? 'mark_email_read' : 'move_to_inbox'}
                            className="size-icon-md shrink-0 text-accent-deep"
                        />
                        <span className="min-w-0">
                            {translate(lineSays[asking])}
                            {readReceiptNote === null ? null : (
                                <>
                                    {' '}
                                    <span className="text-muted">{readReceiptNote}</span>
                                </>
                            )}
                        </span>
                    </button>
                    {lineTip.tip}
                </>
            ),
        control: (
            <>
                <SymbolControl
                    label={translate(controlNames[asking])}
                    ref={(element) => {
                        control.current = element;
                    }}
                    className={`flex size-8 shrink-0 items-center justify-center rounded-md transition hover:bg-hover pointer-coarse:ms-auto pointer-coarse:size-11 ${
                        asking === 'none'
                            ? 'text-muted'
                            : 'bg-accent-soft text-accent-deep ring-1 ring-accent-line ring-inset'
                    }`}
                    onPress={() => {
                        open(false);
                    }}
                >
                    <Icon name="more_vert" className="size-icon-lg pointer-coarse:size-icon-xl" />
                </SymbolControl>

                <Departing value={menu}>
                    {(opened) => (
                        <ContextMenu
                            header={translate('compose.sendingOptions')}
                            at={opened.at}
                            endsAt
                            opensAbove
                            broad
                            items={[
                                {
                                    icon: 'move_to_inbox',
                                    label: translate('compose.requestDeliveryNotification'),
                                    checked: requested.requestsDeliveryNotification,
                                    choose: () => {
                                        onTurned({
                                            requestsDeliveryNotification: !requested.requestsDeliveryNotification,
                                        });
                                    },
                                },
                                {
                                    icon: 'mark_email_read',
                                    label: translate('compose.requestReadReceipt'),
                                    checked: requested.requestsReadReceipt,
                                    choose: () => {
                                        onTurned({ requestsReadReceipt: !requested.requestsReadReceipt });
                                    },
                                },
                            ]}
                            onClose={() => {
                                setMenu(null);
                                ((opened.fromLine ? line.current : null) ?? control.current)?.focus();
                            }}
                        />
                    )}
                </Departing>
            </>
        ),
    };
}

function askingOf(requested: ReceiptRequests): Asking {
    if (requested.requestsDeliveryNotification) {
        return requested.requestsReadReceipt ? 'both' : 'deliveryNotification';
    }

    return requested.requestsReadReceipt ? 'readReceipt' : 'none';
}
