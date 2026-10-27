// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, useState } from 'react';
import { ContextMenu } from '../contextMenu/ContextMenu';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { Control } from '../controls/Control';
import { PlannedControl } from '../controls/PlannedControl';
import { useLocalization } from '../localization/useLocalization';
import { SurfaceWindow } from '../mailSpace/SurfaceWindow';
import { Departing } from '../shell/Departing';
import { MessageSourceSurface } from './MessageSourceSurface';
import { useMessageSourceReading } from './messageSourceReading';

// The three-dot control the design puts after the spam control, in a message's head and — where two panes under a
// finger leave the head its subject alone — in the toolbar. It opens the context menu under itself, its end edge on the
// control's, and *Show source* is the one thing in it today.
//
// Whoever draws it holds what it opened: the menu and the window are its own state rather than the workspace's,
// because nothing about either outlives the press that asked for it, and a source is read again whenever it is asked
// for again.

interface Opened {
    readonly storedEmailId: string;
}

export function MoreActions({
    storedEmailId,
    subject,
}: {
    /** The message the acts are about, or `null` where the control stands over nothing. */
    readonly storedEmailId: string | null;

    /** The message's subject where whoever draws this holds it, which heads the menu and names the source. */
    readonly subject: string | null;
}) {
    const { translate } = useLocalization();
    const reading = useMessageSourceReading();
    const opener = useRef<HTMLSpanElement>(null);
    const [menu, setMenu] = useState<MenuPoint | null>(null);
    const [source, setSource] = useState<Opened | null>(null);
    const label = translate('message.moreActions');
    const acting = storedEmailId !== null && reading !== null;

    // The menu and the source are about one message, so both go once another stands here — adjusted during the render
    // that sees the change, so neither is drawn over the next message nor comes back when the first is opened again.
    const [standingOver, setStandingOver] = useState(storedEmailId);

    if (standingOver !== storedEmailId) {
        setStandingOver(storedEmailId);
        setMenu(null);
        setSource(null);
    }

    const shown = reading !== null && source !== null ? { storedEmailId: source.storedEmailId, reading } : null;

    return (
        <>
            {/* A box of its own rather than `contents`, so a strip adding up its controls' widths counts this one. */}
            <span ref={opener} className="flex shrink-0">
                {acting ? (
                    <Control
                        label={label}
                        icon="more_vert"
                        shape="symbol"
                        onPress={() => {
                            const control = opener.current?.querySelector('button')?.getBoundingClientRect();

                            setMenu(
                                control === undefined ? { x: 0, y: 0 } : { x: control.right, y: control.bottom + 4 },
                            );
                        }}
                    />
                ) : (
                    <PlannedControl
                        label={label}
                        icon="more_vert"
                        shape="symbol"
                        why={translate('act.nothingToActOn', { control: label })}
                    />
                )}
            </span>

            <Departing value={menu}>
                {(at) => (
                    <ContextMenu
                        header={subject ?? label}
                        at={at}
                        endsAt
                        items={[
                            {
                                icon: 'data_object',
                                label: translate('source.show'),
                                choose: () => {
                                    if (storedEmailId !== null) {
                                        setSource({ storedEmailId });
                                    }
                                },
                            },
                        ]}
                        onClose={() => {
                            setMenu(null);
                            opener.current?.querySelector('button')?.focus();
                        }}
                    />
                )}
            </Departing>

            <Departing value={shown}>
                {(opened) => (
                    <SurfaceWindow
                        label={translate('source.title')}
                        drawn="source"
                        onClosed={() => {
                            setSource(null);
                        }}
                    >
                        {(close) => (
                            <MessageSourceSurface
                                reading={opened.reading}
                                storedEmailId={opened.storedEmailId}
                                subject={subject}
                                onClose={close}
                            />
                        )}
                    </SurfaceWindow>
                )}
            </Departing>
        </>
    );
}
