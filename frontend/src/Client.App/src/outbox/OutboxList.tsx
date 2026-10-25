// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useState } from 'react';
import type { ClientFailureReason } from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useMailboxesDrawer } from '../mailSpace/mailboxesDrawer';
import { useAccountName } from '../shell/accountNames';
import { useWorkspace, type OpenOutbox } from '../workspace/useWorkspace';
import { OutboxRow } from './OutboxRow';
import { useMailOutbox } from './useMailOutbox';

// An account's Outbox in the list's place: its head, the line saying it is read-only, and the sends that have not left
// yet, newest first. It is no folder, so nothing here offers a mail act — the line says so, which is what makes the
// missing acts read as what an Outbox is rather than as a fault.
//
// One page of the newest sends is what is drawn, which is the most the service serves at once. An Outbox holds what
// has not left yet, which is a handful, so the ceiling is one nobody is expected to reach; past it the oldest are the
// ones not shown, and reading on is the cursor the page already carries.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

export function OutboxList({ outbox }: { readonly outbox: OpenOutbox }) {
    const { translate } = useLocalization();
    const { revise } = useWorkspace();
    const { page, readAgain, goToSent } = useMailOutbox();
    const [leaving, setLeaving] = useState(false);
    const openMailboxes = useMailboxesDrawer();
    const accountName = useAccountName(outbox.account);

    return (
        <div className="flex min-h-0 flex-1 flex-col">
            <div className="flex shrink-0 items-center gap-2 border-b border-line px-3 py-2 workspace:bg-sunken desktop:gap-2.25 desktop:py-2.5 max-workspace:min-h-14 max-workspace:py-1.5 max-workspace:ps-4">
                {openMailboxes === null ? null : (
                    <SurfaceControl label={translate('mailboxes.open')} icon="menu" edged onActivate={openMailboxes} />
                )}

                <div className="flex min-w-0 flex-1 flex-col gap-px">
                    <h2 className="text-3xl/tight font-semibold tracking-tight workspace:text-xl/tight">
                        {translate('folder.outbox')}
                    </h2>
                    {accountName === null ? null : <p className="truncate text-sm/snug text-muted">{accountName}</p>}
                </div>
            </div>

            <p className="flex shrink-0 items-start gap-2 border-b border-line bg-rail px-4 py-2.5 text-sm leading-4.25 text-muted workspace:px-3.5 workspace:py-2.25">
                <Icon name="lock" className="mt-px size-icon-sm" />
                <span className="text-pretty">{translate('outbox.readOnly')}</span>
            </p>

            <div className="relative min-h-40 flex-1 overflow-auto bg-sunken">
                {page.kind === 'offline' ? (
                    <p className="px-4 py-3 text-sm text-muted">{translate('connection.offline')}</p>
                ) : page.kind === 'reading' ? (
                    <p role="status" className="px-4 py-3 text-sm text-muted">
                        {translate('outbox.reading')}
                    </p>
                ) : page.kind === 'failed' ? (
                    <div className="flex flex-col items-start gap-2 px-4 py-3">
                        <p className="text-sm text-warning-text">
                            {translate('outbox.failed', { reason: translate(failureLabels[page.reason]) })}
                        </p>
                        {page.reason === 'unavailable' ? (
                            <SecondaryButton label={translate('connection.retry')} onActivate={readAgain} />
                        ) : null}
                    </div>
                ) : page.sends.length === 0 ? (
                    <div role="status" className="flex flex-col items-center gap-3 px-6 py-12 text-center">
                        <Icon name="check_circle" className="size-icon-3xl text-healthy" />
                        <p className="text-lg font-semibold">{translate('outbox.emptyTitle')}</p>
                        <p className="max-w-75 text-base/relaxed text-pretty text-muted">
                            {accountName === null
                                ? translate('outbox.emptyTextUnnamed')
                                : translate('outbox.emptyText', { account: accountName })}
                        </p>
                        <Control
                            label={translate('outbox.goToSent')}
                            shape="primary"
                            held={leaving}
                            onPress={() => {
                                setLeaving(true);
                                void goToSent().finally(() => {
                                    setLeaving(false);
                                });
                            }}
                        />
                    </div>
                ) : (
                    <ul aria-label={translate('folder.outbox')} className="flex flex-col gap-px py-px">
                        {page.sends.map((send) => (
                            <OutboxRow
                                key={send.outgoingEmailId}
                                send={send}
                                open={outbox.entry === send.outgoingEmailId}
                                onOpen={() => {
                                    revise({ outbox: { account: outbox.account, entry: send.outgoingEmailId } });
                                }}
                            />
                        ))}
                    </ul>
                )}
            </div>
        </div>
    );
}
