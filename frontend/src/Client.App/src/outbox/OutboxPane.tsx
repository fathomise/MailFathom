// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState, type ReactNode, type Ref } from 'react';
import {
    readOutgoingMail,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type OutgoingMail,
    type OutgoingMailPreview,
    type OutgoingMailRecipientRole,
} from '@mailfathom/client-backend';
import { Control } from '../controls/Control';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { sizeOf } from '../localization/octets';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { kindOf } from '../readingPane/fileKind';
import { useTwoPanes } from '../shell/useWideWorkspace';
import { useWorkspace, type OpenOutbox } from '../workspace/useWorkspace';
import {
    attemptsSaid,
    standingOf,
    standingShapes,
    standingText,
    standingTitle,
    toneBox,
    toneInk,
    type OutboxStanding,
    type OutboxWording,
} from './outboxStanding';
import { useMailOutbox } from './useMailOutbox';

// One send of an Outbox read whole, in the pane's place: what it is about, where it stands and what may be decided
// about it there, whom it is from and for and what the server said about each of them, the files it carries, and the
// message itself — and nothing that would be a mail act, because a send that has not left is not a message in a folder.
//
// Where it stands is read off the Outbox's own page rather than off the preview, because that page is what the
// deployment's signal moves: a send being tried again, then being sent, then gone, changes here as it changes in the
// list, and the preview is read again whenever it does.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

const roleLabels: Readonly<Record<OutgoingMailRecipientRole, MessageKey>> = {
    To: 'compose.to',
    Cc: 'compose.cc',
    Bcc: 'compose.bcc',
};

export function OutboxPane({
    session,
    transport,
    outbox,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly outbox: OpenOutbox;
}) {
    const { translate } = useLocalization();
    const { page } = useMailOutbox();
    const nothingOpen = useRef<HTMLDivElement>(null);
    const shownEntry = useRef(outbox.entry);

    // A send leaving takes the controls it was decided with along with it, so focus that stood on one would be left on
    // nothing. It moves to what the pane draws instead, and only when it was lost: a send leaving while the reader was
    // elsewhere takes nothing from them.
    useEffect(() => {
        const closed = shownEntry.current !== null && outbox.entry === null;

        shownEntry.current = outbox.entry;

        if (closed && (document.activeElement === null || document.activeElement === document.body)) {
            nothingOpen.current?.focus();
        }
    }, [outbox.entry]);

    if (outbox.entry === null) {
        const empty = page.kind === 'read' && page.sends.length === 0;

        return (
            <PaneNotice
                ref={nothingOpen}
                icon="outbox"
                title={translate('outbox.nothingOpen')}
                text={translate(empty ? 'outbox.nothingOpenEmpty' : 'outbox.nothingOpenText')}
            />
        );
    }

    const row = page.kind === 'read' ? page.sends.find((send) => send.outgoingEmailId === outbox.entry) : undefined;

    return (
        <OpenSend
            key={outbox.entry}
            session={session}
            transport={transport}
            outbox={outbox}
            entry={outbox.entry}
            row={row}
            online={page.kind !== 'offline'}
        />
    );
}

// The pane's one layout for having nothing to show — nothing picked, a preview that could not be read, a machine with
// no network — as the design gives the reading pane: a glyph, a title, a sentence, and one way on where one exists.
function PaneNotice({
    ref,
    icon,
    title,
    text,
    back,
    action,
}: {
    readonly ref?: Ref<HTMLDivElement>;
    readonly icon: IconName;
    readonly title: string;
    readonly text: string;
    readonly back?: ReactNode;
    readonly action?: ReactNode;
}) {
    return (
        <div className="flex min-h-full flex-1 flex-col bg-sunken">
            {back === undefined || back === null ? null : <div className="flex px-2 py-1.5">{back}</div>}
            <div
                ref={ref}
                role="status"
                tabIndex={-1}
                className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center"
            >
                <span className="flex size-11.5 items-center justify-center rounded-xl bg-rail text-muted">
                    <Icon name={icon} className="size-icon-2xl" />
                </span>
                <div className="flex max-w-105 flex-col items-center gap-1.75">
                    <p className="text-3xl font-semibold tracking-tight">{title}</p>
                    <p className="text-md/relaxed text-pretty text-muted">{text}</p>
                </div>
                {action}
            </div>
        </div>
    );
}

function OpenSend({
    session,
    transport,
    outbox,
    entry,
    row,
    online,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly outbox: OpenOutbox;
    readonly entry: string;

    /** The send as the Outbox's page last said it, which is what its standing is read from while the page has it. */
    readonly row: OutgoingMail | undefined;
    readonly online: boolean;
}) {
    const { translate } = useLocalization();
    const { revise } = useWorkspace();
    const twoPanes = useTwoPanes();
    const [read, setRead] = useState<ClientResult<OutgoingMailPreview> | null>(null);
    const [attempt, setAttempt] = useState(0);

    // What the page says about the send, as one value: a change to any of it is the send having moved on, which is when
    // what the preview says about it is read again.
    const moved = row === undefined ? '' : `${row.stage}:${String(row.heldByAttempt)}:${String(row.attemptCount)}`;

    useEffect(() => {
        if (!online) {
            return;
        }

        let listening = true;

        void readOutgoingMail(session, transport, entry).then((result) => {
            if (!listening) {
                return;
            }

            // A send this user no longer holds is let go of here, the pane being what holds it.
            if (result.outcome === 'failed' && result.failure.reason === 'missing') {
                revise({ outbox: { account: outbox.account, entry: null } });

                return;
            }

            // A read under a preview already drawn that the deployment did not answer leaves the preview standing.
            setRead((current) => (result.outcome === 'failed' && current?.outcome === 'read' ? current : result));
        });

        return () => {
            listening = false;
        };
    }, [session, transport, entry, moved, outbox.account, revise, online, attempt]);

    const back = twoPanes ? null : (
        <Control
            label={translate('outbox.back')}
            icon="arrow_back"
            shape="symbol"
            onPress={() => {
                revise({ outbox: { account: outbox.account, entry: null } });
            }}
        />
    );

    if (!online && read?.outcome !== 'read') {
        return (
            <PaneNotice
                icon="cloud_off"
                title={translate('outbox.previewOfflineTitle')}
                text={translate('message.offline')}
                back={back}
            />
        );
    }

    if (read === null) {
        return (
            <div className="flex flex-col gap-2 p-4">
                {back}
                <p role="status" className="text-sm text-muted">
                    {translate('outbox.previewReading')}
                </p>
            </div>
        );
    }

    if (read.outcome === 'failed') {
        // Reading again is the way out of exactly one failure, for the reason `shell/ConnectionSummary.tsx` gives: the
        // other four repeat identically on a second attempt.
        return (
            <PaneNotice
                icon="warning"
                title={translate('outbox.previewFailedTitle')}
                text={translate('outbox.previewFailed', { reason: translate(failureLabels[read.failure.reason]) })}
                back={back}
                action={
                    read.failure.reason === 'unavailable' ? (
                        <SecondaryButton
                            label={translate('connection.retry')}
                            onActivate={() => {
                                setRead(null);
                                setAttempt((current) => current + 1);
                            }}
                        />
                    ) : undefined
                }
            />
        );
    }

    const preview = read.value;
    const standing = standingOf(row ?? preview);

    return (
        <article className="flex min-h-full flex-1 flex-col bg-panel">
            <div className={`flex shrink-0 items-center gap-2 ${twoPanes ? 'px-6 pt-4 pb-2.5' : 'px-2 py-1.5'}`}>
                {back}
                <h2
                    className={`min-w-0 flex-1 font-semibold tracking-tight text-pretty ${twoPanes ? 'text-3xl' : 'text-2xl/snug'}`}
                >
                    {preview.subject ?? translate('message.noSubject')}
                </h2>
            </div>

            <div className={`flex flex-col gap-4 ${twoPanes ? 'px-6 pt-1 pb-7' : 'px-4 pt-1 pb-6'}`}>
                {standing === null ? null : <StateBox standing={standing} preview={preview} row={row} />}
                <Facts preview={preview} />
                <Attachments preview={preview} />

                {preview.text === null ? null : (
                    <div className="flex max-w-180 flex-col gap-3">
                        {preview.text.split(/\n{2,}/u).map((paragraph, at) => (
                            <p key={at} className="text-lg/relaxed whitespace-pre-line text-pretty">
                                {paragraph}
                            </p>
                        ))}
                    </div>
                )}

                <p className="flex items-center gap-1.75 text-sm text-muted">
                    <Icon name="lock" className="size-icon-sm" />
                    {translate('outbox.readOnlyFoot')}
                </p>
            </div>
        </article>
    );
}

// The send as a decision takes it: the page's row where the page has it, and otherwise the preview read down to the
// recipients a row would have named, which is what a toast about the decision says it was for.
function decidedAbout(preview: OutgoingMailPreview, row: OutgoingMail | undefined): OutgoingMail {
    return (
        row ?? {
            ...preview,
            recipients: preview.recipients.map(({ address, role }) => ({ address, role })),
            otherRecipientCount: 0,
        }
    );
}

function StateBox({
    standing,
    preview,
    row,
}: {
    readonly standing: OutboxStanding;
    readonly preview: OutgoingMailPreview;
    readonly row: OutgoingMail | undefined;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const { cancel, sendAgain, dismiss } = useMailOutbox();
    const [now] = useState(() => Date.now());
    const wording: OutboxWording = { locale, timeZone, translate, now };
    const shape = standingShapes[standing];
    const tone = toneBox[shape.tone];
    const send = decidedAbout(preview, row);
    const said: OutgoingMailPreview =
        row === undefined
            ? preview
            : {
                  ...preview,
                  stage: row.stage,
                  heldByAttempt: row.heldByAttempt,
                  dueAt: row.dueAt,
                  availableAt: row.availableAt,
                  attemptCount: row.attemptCount,
                  failure: row.failure,
              };

    return (
        <div role="status" className={`flex items-start gap-2.75 rounded-xl border px-3.5 py-3 ${tone.box}`}>
            <Icon
                name={shape.icon}
                className={`size-icon-lg ${toneInk[shape.tone]} ${shape.moving ? 'animate-working' : ''}`}
            />
            <div className="flex min-w-0 flex-1 flex-col gap-1">
                <p className={`text-md font-semibold ${tone.title}`}>{standingTitle(standing, said, wording)}</p>
                <p className="text-base/relaxed text-pretty text-text-soft">{standingText(standing, said, wording)}</p>

                {shape.decisions === 'decide' ? (
                    <div className="flex flex-wrap gap-2 pt-1.5">
                        <Control
                            label={translate('outbox.sendAgain')}
                            shape="primary"
                            onPress={() => {
                                sendAgain(send);
                            }}
                        />
                        <SecondaryButton
                            label={translate('outbox.dismiss')}
                            shape="form"
                            onActivate={() => {
                                dismiss(send);
                            }}
                        />
                    </div>
                ) : shape.decisions === 'cancel' ? (
                    <div className="flex flex-wrap gap-2 pt-1.5">
                        <SecondaryButton
                            label={translate('outbox.cancel')}
                            shape="form"
                            onActivate={() => {
                                cancel(send);
                            }}
                        />
                    </div>
                ) : null}
            </div>
        </div>
    );
}

function Facts({ preview }: { readonly preview: OutgoingMailPreview }) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const [now] = useState(() => Date.now());
    const wording: OutboxWording = { locale, timeZone, translate, now };

    const facts: readonly {
        readonly key: string;
        readonly label: string;
        readonly value: string;
        readonly said?: string;
    }[] = [
        ...(preview.sender === null
            ? []
            : [{ key: 'from', label: translate('outbox.fact.from'), value: preview.sender }]),
        ...preview.recipients.map((recipient, at) => ({
            key: `recipient-${String(at)}`,
            label: translate(roleLabels[recipient.role]),
            value: recipient.address,
            ...(recipient.status === 'Refused' ? { said: translate('outbox.recipientRefused') } : {}),
        })),
        {
            key: 'handed',
            label: translate('outbox.fact.handed'),
            value: wordInstant(preview.recordedAt, locale, 'stamp', timeZone) ?? '',
        },
        ...(preview.dueAt === null
            ? []
            : [
                  {
                      key: 'leaves',
                      label: translate('outbox.fact.leaves'),
                      value: wordInstant(preview.dueAt, locale, 'stamp', timeZone) ?? '',
                  },
              ]),
        ...(preview.attemptCount === 0
            ? []
            : [
                  {
                      key: 'tried',
                      label: translate('outbox.fact.tried'),
                      value: attemptsSaid(preview.attemptCount, wording),
                  },
              ]),
    ];

    return (
        <dl className="flex flex-col gap-1 border-b border-line-soft pb-3 text-base text-text-soft">
            {facts.map((fact) => (
                <div key={fact.key} className="flex min-w-0 flex-wrap gap-x-2 gap-y-0.5">
                    <dt className="w-13 shrink-0 text-muted">{fact.label}</dt>
                    <dd className="min-w-0 flex-1 basis-50 wrap-anywhere">{fact.value}</dd>
                    {fact.said === undefined ? null : (
                        <dd className="ms-15 shrink-0 text-sm text-error-text">{fact.said}</dd>
                    )}
                </div>
            ))}
        </dl>
    );
}

// Each file by name, kind and size, which opens nothing: a send that has not left holds its files for the send rather
// than as something to read here.
function Attachments({ preview }: { readonly preview: OutgoingMailPreview }) {
    const { locale, translate } = useLocalization();

    if (preview.attachments.length === 0) {
        return null;
    }

    return (
        <ul aria-label={translate('outbox.attachments')} className="flex flex-wrap gap-2">
            {preview.attachments.map((attachment, at) => (
                <li
                    key={at}
                    className="flex max-w-full min-w-0 items-center gap-2.25 rounded-md border border-line bg-sunken px-3 py-2"
                >
                    <Icon name="description" className="size-icon-lg text-muted" />
                    <span className="flex min-w-0 flex-col gap-px">
                        <span className="truncate text-base">
                            {attachment.fileName ?? translate('attachment.unnamed')}
                        </span>
                        <span className="text-sm text-muted">
                            {translate('outbox.attachmentMeta', {
                                kind: kindOf(attachment.fileName, attachment.mediaType),
                                size: sizeOf(attachment.sizeOctets, locale),
                            })}
                        </span>
                    </span>
                </li>
            ))}
        </ul>
    );
}
