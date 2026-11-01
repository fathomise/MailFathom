// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type ReactNode, type RefObject } from 'react';
import { flushSync } from 'react-dom';
import {
    addAuthorToContacts,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
    type MailMessage,
    type MailSignatureStepResult,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { SecondaryButton } from '../controls/SecondaryButton';
import { SurfaceControl } from '../controls/SurfaceControl';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { senderDetailsShape, type MarkAnchor } from '../markDetails/detailsPlacement';
import { MarkDetailsDialog } from '../markDetails/MarkDetailsDialog';
import { useAskedMessage } from '../markDetails/useAskedMessage';
import { listedSenderOf, senderMarkInk, senderMarkOf, senderMarkPill, senderMarksDrawn } from './senderMarks';
import { senderVerificationOf, type Said, type VerificationStep } from './verificationSteps';

// What was established about who sent one message, step by step: the mark and why it is the mark, then every check
// that reached it, each with its result and the reason a step was not reached. The one act here is the one the service
// offers on the message: writing its author down in the reader's own contacts, said before it is done, and leaving the
// mark as it stands until the mail is judged again.
//
// They stand in the surface every mark on a message opens its details in, `markDetails/MarkDetailsDialog.tsx`.
//
// **A row carries the four facts and the details need the evidence**, so details opened from a row read the message
// when they open, and say so while they do; details opened from a message already read are drawn from it at once.

/** Whose sender is being explained, and where the mark that asked stands. */
export interface AskedSender {
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

const resultDrawn: Readonly<
    Record<MailSignatureStepResult, { readonly icon: IconName; readonly ink: string; readonly word: MessageKey }>
> = {
    Passed: { icon: 'check_circle', ink: 'text-healthy-text', word: 'senderMark.result.Passed' },
    Failed: { icon: 'cancel', ink: 'text-error-text', word: 'senderMark.result.Failed' },
    NotChecked: { icon: 'help', ink: 'text-muted', word: 'senderMark.result.NotChecked' },
    Skipped: { icon: 'do_not_disturb_on', ink: 'text-muted', word: 'senderMark.result.Skipped' },
};

export function SenderDetails({
    asked,
    session,
    transport,
    dialog,
}: {
    /** The sender being explained, or `null` while nothing is. */
    readonly asked: AskedSender | null;
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The dialog itself, opened by whichever surface asked, so that whether it is open is the element's own state. */
    readonly dialog: RefObject<HTMLDialogElement | null>;
}) {
    const { translate } = useLocalization();
    const names = useId();

    // A surface asks afresh on every press, so each question is its own opening, and each opening its own read: a
    // second look at the same message shows a trust change made since, and leaves behind a failure no retry is offered
    // for, rather than drawing the last answer again.
    const [shown, setShown] = useState({ asked, opening: 0 });

    if (shown.asked !== asked) {
        setShown({ asked, opening: shown.opening + 1 });
    }

    return (
        <DetailsDialog dialog={dialog} anchor={asked?.anchor ?? null} names={names}>
            {/* Keyed by the opening, so asking again — about this sender or another — starts its own read rather than
                drawing an earlier answer under the mark that was just pressed. */}
            {asked === null ? (
                <h2 id={names} className="sr-only">
                    {translate('senderMark.details')}
                </h2>
            ) : (
                <AskedSenderDetails
                    key={shown.opening}
                    asked={asked}
                    session={session}
                    transport={transport}
                    names={names}
                    onClose={() => {
                        dialog.current?.close();
                    }}
                />
            )}
        </DetailsDialog>
    );
}

/** The details of a message already read, which need no read of their own. */
export function MessageSenderDetails({
    session,
    transport,
    message,
    anchor,
    dialog,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly message: MailMessage;
    readonly anchor: AskedSender['anchor'];
    readonly dialog: RefObject<HTMLDialogElement | null>;
}) {
    const { translate } = useLocalization();
    const names = useId();

    // These details stay mounted while the message is open, so each opening starts the act afresh, and an author
    // already added is not offered again from the message read before the addition.
    const [opening, setOpening] = useState(0);
    const [added, setAdded] = useState(false);

    return (
        <DetailsDialog
            dialog={dialog}
            anchor={anchor}
            names={names}
            onOpened={() => {
                setOpening(opening + 1);
            }}
        >
            <Verification
                message={message}
                names={names}
                act={
                    <AddToContacts
                        key={opening}
                        session={session}
                        transport={transport}
                        message={message}
                        added={added}
                        onAdded={() => {
                            setAdded(true);
                        }}
                    />
                }
                close={
                    <SurfaceControl
                        label={translate('senderMark.close')}
                        icon="close"
                        onActivate={() => {
                            dialog.current?.close();
                        }}
                    />
                }
            />
        </DetailsDialog>
    );
}

function DetailsDialog({
    dialog,
    anchor,
    names,
    onOpened,
    children,
}: {
    readonly dialog: RefObject<HTMLDialogElement | null>;
    readonly anchor: AskedSender['anchor'];
    readonly names: string;
    readonly onOpened?: () => void;
    readonly children: ReactNode;
}) {
    return (
        <MarkDetailsDialog
            dialog={dialog}
            anchor={anchor}
            shape={senderDetailsShape}
            widthClass="desktop:w-98"
            names={names}
            {...(onOpened === undefined ? {} : { onOpened })}
        >
            {children}
        </MarkDetailsDialog>
    );
}

function AskedSenderDetails({
    asked,
    session,
    transport,
    names,
    onClose,
}: {
    readonly asked: AskedSender;
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly names: string;
    readonly onClose: () => void;
}) {
    const { translate } = useLocalization();
    const read = useAskedMessage(session, transport, asked.storedEmailId, asked.message);
    const close = <SurfaceControl label={translate('senderMark.close')} icon="close" onActivate={onClose} />;

    if (read.message === null) {
        return (
            <div className="flex flex-col gap-3 px-4.5 pt-1 pb-4.5 desktop:pt-4">
                <div className="flex items-start gap-3">
                    <h2 id={names} className="min-w-0 flex-1 pt-1 text-lg font-semibold">
                        {translate('senderMark.details')}
                    </h2>

                    {close}
                </div>

                {read.failure === null ? (
                    <p className="text-base text-muted" role="status">
                        {translate('senderMark.reading')}
                    </p>
                ) : (
                    <div className="flex flex-wrap items-center gap-1.5">
                        <p className="text-base text-warning" role="alert">
                            {translate('senderMark.readFailed', {
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

    return (
        <Verification
            message={read.message}
            names={names}
            close={close}
            act={<AddToContacts session={session} transport={transport} message={read.message} />}
        />
    );
}

function Verification({
    message,
    names,
    close,
    act,
}: {
    readonly message: MailMessage;
    readonly names: string;
    readonly close: ReactNode;
    readonly act: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const verification = senderVerificationOf(message, (instant) => wordInstant(instant, locale, 'full', timeZone));
    const drawn = senderMarksDrawn[verification.mark];
    // A known sender is drawn in the neutral tone, so its title reads as plainly as an unrecognized one's.
    const plain = drawn.tone === 'plain' || drawn.tone === 'known';

    return (
        <>
            <div className="flex shrink-0 items-start gap-3 border-b border-line pt-1 pe-3 pb-3.5 ps-4.5 desktop:pt-4 desktop:pe-2.5">
                <span
                    aria-hidden="true"
                    className={`flex size-9 shrink-0 items-center justify-center rounded-field border ${
                        plain ? 'border-line bg-sunken text-muted' : senderMarkPill[drawn.tone]
                    }`}
                >
                    <Icon name={drawn.icon} className="size-icon-lg" />
                </span>

                <div className="flex min-w-0 flex-1 flex-col gap-0.75 pt-px">
                    <h2
                        id={names}
                        className={`text-lg/snug font-semibold ${plain ? 'text-text' : senderMarkInk[drawn.tone]}`}
                    >
                        {translate(drawn.name)}
                    </h2>

                    {verification.author === null ? null : (
                        <p className="text-sm/natural wrap-anywhere text-muted">{verification.author}</p>
                    )}

                    <p className="pt-0.75 text-base/normal text-pretty text-text-soft">
                        {say(translate, verification.why)}
                    </p>
                </div>

                {close}
            </div>

            <div className="flex min-h-0 flex-1 flex-col gap-3.5 overflow-y-auto px-4.5 pt-3.5 pb-5.5 desktop:pb-4.5">
                {verification.groups.map((group) => (
                    <section key={group.name} aria-label={translate(group.name)} className="flex flex-col gap-1.5">
                        <h3 aria-hidden="true" className="text-xs/natural font-semibold text-muted">
                            {translate(group.name)}
                        </h3>

                        <ul className="flex flex-col rounded-lg border border-line bg-sunken px-3.5">
                            {group.steps.map((step) => (
                                <Step key={step.name} step={step} keyDetails={verification.keyDetails} />
                            ))}
                        </ul>
                    </section>
                ))}

                {act}
            </div>
        </>
    );
}

/** Where the act of adding the author stands within one opening of the details. */
type Addition =
    { readonly phase: 'offered' | 'asking' | 'adding' | 'added' } | { readonly phase: 'failed'; readonly reason: Said };

const addButton =
    'flex h-8.5 items-center justify-center gap-1.5 rounded-md text-base whitespace-nowrap transition aria-disabled:cursor-progress aria-disabled:opacity-70';

// A write outlives the opening that sent it: the details can be closed while it is in flight and opened again, and what
// they then show is that write still going rather than the act offered over it. Held per transport, which is one per
// signed-in client, and by the author's address, which is what a second write would be about.
const additionsInFlight = new WeakMap<MailFathomTransport, Map<string, Promise<Addition>>>();

function inFlightOver(transport: MailFathomTransport): Map<string, Promise<Addition>> {
    const held = additionsInFlight.get(transport) ?? new Map<string, Promise<Addition>>();

    additionsInFlight.set(transport, held);

    return held;
}

async function additionOf(written: ReturnType<typeof addAuthorToContacts>): Promise<Addition> {
    const answer = await written;

    if (answer.outcome === 'failed') {
        return { phase: 'failed', reason: { key: failureLabels[answer.failure.reason] } };
    }

    return answer.value.outcome === 'Written'
        ? { phase: 'added' }
        : { phase: 'failed', reason: { key: 'senderMark.add.changed' } };
}

// The author written down in the reader's own book, offered where the service said it may be and said in full before
// it is done. The mark is left as it stands: what the addition changes is the next judgment of the mail, not this one.
function AddToContacts({
    session,
    transport,
    message,
    added = false,
    onAdded,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly message: MailMessage;

    /** Whether this author was added in an earlier opening, which leaves nothing to offer. */
    readonly added?: boolean;
    readonly onAdded?: () => void;
}) {
    const { translate } = useLocalization();
    const offered = message.sender.addToContacts;
    const author = message.headers.participants.find((participant) => participant.role === 'From') ?? null;
    const [followed] = useState(() => (author === null ? undefined : inFlightOver(transport).get(author.address)));
    const [addition, setAddition] = useState<Addition>({ phase: followed === undefined ? 'offered' : 'adding' });
    const offer = useRef<HTMLButtonElement>(null);
    const confirm = useRef<HTMLButtonElement>(null);
    const done = useRef<HTMLParagraphElement>(null);
    const titleId = useId();

    // A write an earlier opening sent is followed to its answer here, as if this opening had sent it.
    useEffect(() => {
        if (followed === undefined) {
            return undefined;
        }

        let listening = true;

        void followed.then((next) => {
            if (listening) {
                setAddition(next);
            }
        });

        return () => {
            listening = false;
        };
    }, [followed]);

    if (offered === null || author === null || (added && addition.phase !== 'added')) {
        return null;
    }

    const { address } = author;
    const { restatedContactId } = offered;
    const name = author.displayName ?? address;
    const mark = senderMarkOf(listedSenderOf(message));
    const warning = mark === 'signatureMismatch' || mark === 'authenticationFailed';
    const domain = address.slice(address.lastIndexOf('@') + 1);

    function move(next: Addition, focused: RefObject<HTMLElement | null>): void {
        flushSync(() => {
            setAddition(next);
        });
        focused.current?.focus();
    }

    async function add(): Promise<void> {
        move({ phase: 'adding' }, confirm);

        const inFlight = inFlightOver(transport);
        const pending = additionOf(
            addAuthorToContacts(session, transport, { displayName: name, address, restatedContactId }),
        );

        inFlight.set(address, pending);

        const next = await pending.finally(() => {
            inFlight.delete(address);
        });

        move(next, next.phase === 'added' ? done : confirm);

        if (next.phase === 'added') {
            onAdded?.();
        }
    }

    if (addition.phase === 'added') {
        return (
            <p
                ref={done}
                role="status"
                tabIndex={-1}
                className="flex items-start gap-2 rounded-lg bg-healthy-soft px-3.5 py-2.5 text-base/normal text-pretty text-healthy-text outline-none"
            >
                <Icon name="check_circle" className="size-icon-sm shrink-0" />
                <span>{translate('senderMark.add.done', { name })}</span>
            </p>
        );
    }

    if (addition.phase === 'offered') {
        return (
            <button
                ref={offer}
                type="button"
                className={`${addButton} self-start border border-line-strong px-3.75 text-text-soft hover:bg-hover`}
                onClick={() => {
                    move({ phase: 'asking' }, confirm);
                }}
            >
                <Icon name="person_add" className="size-icon-sm" />
                {translate('senderMark.add.offer')}
            </button>
        );
    }

    const busy = addition.phase === 'adding';

    return (
        <div
            role="group"
            aria-labelledby={titleId}
            className="flex flex-col gap-2.5 rounded-lg border border-line bg-sunken px-3.5 py-3"
        >
            <p id={titleId} className="text-base font-semibold text-text">
                {translate('senderMark.add.title', { name })}
            </p>

            <p className="text-base/normal text-pretty wrap-anywhere text-text-soft">
                {[
                    restatedContactId === null ? null : translate('senderMark.add.restated', { address }),
                    translate('senderMark.add.what', { address }),
                    warning ? translate('senderMark.add.warning', { domain }) : null,
                ]
                    .filter((sentence) => sentence !== null)
                    .join(' ')}
            </p>

            {addition.phase === 'failed' ? (
                <p role="alert" className="text-base/normal text-pretty text-error-text">
                    {translate('senderMark.add.failed', { name, reason: say(translate, addition.reason) })}
                </p>
            ) : null}

            <div className="flex flex-wrap gap-2">
                <button
                    ref={confirm}
                    type="button"
                    aria-disabled={busy ? true : undefined}
                    className={`${addButton} bg-accent px-4 font-semibold text-on-accent`}
                    onClick={() => {
                        if (!busy) {
                            void add();
                        }
                    }}
                >
                    {translate(
                        busy
                            ? 'senderMark.add.adding'
                            : addition.phase === 'failed'
                              ? 'senderMark.add.retry'
                              : 'senderMark.add.confirm',
                    )}
                </button>

                {/* A write already sent is not taken back, so the card is not left while it is in flight. */}
                <button
                    type="button"
                    aria-disabled={busy ? true : undefined}
                    className={`${addButton} border border-line-strong px-3.75 text-text-soft hover:bg-hover`}
                    onClick={() => {
                        if (!busy) {
                            move({ phase: 'offered' }, offer);
                        }
                    }}
                >
                    {translate('senderMark.add.cancel')}
                </button>
            </div>
        </div>
    );
}

function say(translate: ReturnType<typeof useLocalization>['translate'], said: Said): string {
    return translate(said.key, said.values);
}

function Step({ step, keyDetails }: { readonly step: VerificationStep; readonly keyDetails: boolean }) {
    const { translate } = useLocalization();
    const [unfolded, setUnfolded] = useState(false);
    const result = resultDrawn[step.result];
    const facts = step.facts ?? [];
    const factsId = useId();

    return (
        <li className="flex flex-col gap-0.75 border-t border-line-soft py-2.5 first:border-t-0">
            <div className="flex min-w-0 items-center gap-2.5">
                <span className="min-w-0 flex-1 text-base/natural font-semibold text-text">{translate(step.name)}</span>

                <span
                    className={`flex shrink-0 items-center gap-1 text-sm/natural font-semibold whitespace-nowrap ${result.ink}`}
                >
                    <Icon name={result.icon} className="size-icon-sm" />
                    {translate(result.word)}
                </span>
            </div>

            <p className="text-base/normal text-pretty wrap-anywhere text-text-soft">{say(translate, step.detail)}</p>

            {facts.length === 0 ? null : (
                <>
                    <button
                        type="button"
                        aria-expanded={unfolded}
                        aria-controls={factsId}
                        className="-ms-1.5 flex items-center gap-0.75 self-start rounded-md px-1.5 py-0.75 text-sm text-accent-deep transition hover:bg-hover"
                        onClick={() => {
                            setUnfolded(!unfolded);
                        }}
                    >
                        <Icon name={unfolded ? 'expand_less' : 'expand_more'} className="size-icon-sm" />
                        {translate(
                            unfolded
                                ? 'senderMark.more.hide'
                                : keyDetails
                                  ? 'senderMark.more.key'
                                  : 'senderMark.more.certificate',
                        )}
                    </button>

                    {unfolded ? (
                        <dl
                            id={factsId}
                            className="mt-1 flex flex-col gap-1 rounded-md border border-line bg-panel px-2.75 py-2.25 text-sm/snug"
                        >
                            {facts.map((fact) => (
                                <div key={fact.label} className="flex gap-3.5">
                                    <dt className="w-24 shrink-0 text-muted">{translate(fact.label)}</dt>
                                    <dd className="min-w-0 font-mono wrap-anywhere text-text">{fact.value}</dd>
                                </div>
                            ))}
                        </dl>
                    ) : null}
                </>
            )}
        </li>
    );
}
