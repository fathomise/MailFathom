// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState } from 'react';
import {
    readMailDrafts,
    type ClientSession,
    type MailDraft,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import { Icon } from '../controls/Icon';
import { ReceivedAt } from '../controls/ReceivedAt';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { useSignalledChanges, type SignalledChange } from '../signals/signalledChanges';

// The drafts MailFathom keeps whose copy in the account's Drafts folder on the mail server may be missing or out of
// date, at the top of a Drafts folder and apart from the drafts that folder lists. The folder lists what the server
// holds, so these are exactly the ones it may be wrong about — and what another mail app shows of them too.
//
// It stands above the list rather than inside its scroll, because the list draws only the rows in view and reserves the
// space of the rest by arithmetic over one row height: a group of another height inside it would put every row below it
// out of step with the space the list drew for it.
//
// A read that fails draws nothing, and so does an account with none: the folder below still lists what the server
// holds, and this is a correction to it rather than a screen of its own.
//
// It is read again whenever a draft may have moved: the composer opening or closing here, and every statement the
// Drafts list itself reads again on, for the accounts it covers. An answer is drawn only for the accounts it was asked
// about, so a folder opened on another account never shows the last one's drafts while its own read is out.

export function DraftsOutOfStep({
    session,
    transport,
    accounts,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The accounts whose Drafts folder is open: one, or every account where the Drafts of all of them are. */
    readonly accounts: readonly string[];
}) {
    const { translate } = useLocalization();
    const composing = useComposing();
    const signalledChanges = useSignalledChanges();
    const [answered, setAnswered] = useState<{ readonly asked: string; readonly drafts: readonly MailDraft[] } | null>(
        null,
    );
    const [readAgain, setReadAgain] = useState(0);
    const asked = accounts.join(',');
    const composerOpen = composing.opening !== null;

    useEffect(() => {
        let listening = true;

        void Promise.all(asked.split(',').map((account) => readMailDrafts(session, transport, account))).then(
            (answers) => {
                if (listening) {
                    setAnswered({
                        asked,
                        drafts: answers
                            .flatMap((answer) => (answer.outcome === 'read' ? answer.value.drafts : []))
                            .filter((draft) => draft.divergence !== null),
                    });
                }
            },
        );

        return () => {
            listening = false;
        };
    }, [session, transport, asked, readAgain, composerOpen]);

    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (movesADraft(signal, asked.split(','))) {
                    setReadAgain((token) => token + 1);
                }
            }),
        [signalledChanges, asked],
    );

    const drafts = answered?.asked === asked ? answered.drafts : [];

    if (drafts.length === 0) {
        return null;
    }

    return (
        <section aria-label={translate('drafts.outOfStep')} className="shrink-0 border-b border-line bg-sunken pb-2">
            <p className="flex items-center gap-1.75 border-b border-line-soft bg-warning-soft px-3.5 pt-2.5 pb-1.5 text-sm font-semibold text-warning-text max-workspace:px-4 max-workspace:pt-3 max-workspace:pb-2">
                <Icon name="cloud_off" className="size-icon-sm" />
                {translate('drafts.outOfStep')}
            </p>

            <ul className="flex flex-col">
                {drafts.map((draft) => (
                    <li key={draft.draftId}>
                        <button
                            type="button"
                            onClick={() => {
                                composing.compose({ kind: 'held', draftId: draft.draftId });
                            }}
                            className="flex w-full cursor-pointer flex-col gap-1.25 border-b border-line-soft bg-panel px-3.5 py-3 text-start transition hover:bg-hover max-workspace:px-4"
                        >
                            <span className="flex min-w-0 items-center gap-2">
                                <span className="shrink-0 text-sm text-muted">{translate('drafts.outOfStepTo')}</span>
                                <span className="min-w-0 flex-1 truncate text-md font-semibold">
                                    {addressedTo(draft, translate)}
                                </span>
                                <Icon name="cloud_off" className="size-icon-xs shrink-0 text-warning-text" />
                                <ReceivedAt at={draft.revisedAt} />
                            </span>
                            <span className="truncate text-base text-text-soft">
                                {draft.subject === '' ? translate('message.noSubject') : draft.subject}
                            </span>
                        </button>
                    </li>
                ))}
            </ul>
        </section>
    );
}

function movesADraft(signal: SignalledChange, accounts: readonly string[]): boolean {
    switch (signal.kind) {
        case 'refresh':
        case 'notification.raised':
            return true;
        case 'mail.arrived':
        case 'mail.changed':
        case 'outbox.changed':
            return accounts.includes(signal.account);
        default:
            return false;
    }
}

function addressedTo(draft: MailDraft, translate: Translate): string {
    const [first, ...more] = draft.recipients;

    if (first === undefined) {
        return translate('drafts.nobody');
    }

    return more.length === 0
        ? first.address
        : translate('drafts.outOfStepMore', { first: first.address, more: String(more.length) });
}
