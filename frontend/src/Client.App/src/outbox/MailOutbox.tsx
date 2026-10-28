// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useRef, useState, type ReactNode } from 'react';
import {
    dismissOutgoingMail,
    offerOutgoingMailAgain,
    readMailOutbox,
    reopenOutgoingMailAsDraft,
    wentUnanswered,
    withdrawOutgoingMail,
    type ClientFailure,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailOutboxDecision,
    type MailOutboxPage,
    type OutgoingMail,
} from '@mailfathom/client-backend';
import { useComposing } from '../composer/useComposing';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { useSignalledChanges } from '../signals/signalledChanges';
import { useToasts, type Toast } from '../toasts/useToasts';
import { useWorkspace } from '../workspace/useWorkspace';
import { addresseeOf, standingOf, type OutboxWording } from './outboxStanding';
import { readSentFolder } from './sentFolder';
import {
    MailOutboxContext,
    type MailOutbox,
    type OutboxDecisionAsked,
    type OutboxDecisionUnderWay,
    type OutboxPage,
} from './useMailOutbox';

// The one reader of the open account's Outbox. It reads the newest page while an Outbox is open and nothing while none
// is, reads it again whenever the deployment says that account's Outbox moved, and performs the three decisions a
// person makes about a send — each answered by a toast and followed by a read, because what a decision did is what the
// deployment now says rather than what this client guessed.
//
// Two things are this provider's rather than either surface's. A send that was being sent and has left is said once,
// as the design says it, which needs the page before the one that just arrived. And a dismissal waits: the deployment
// keeps no way back from one, so the row goes at once and the request goes when the toast offering it back does,
// unless the person took it back first.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
    missing: 'failure.missing',
};

// What a decision the deployment did not take says, by what it answered instead. Only the first is a decision taken.
const notTaken: Readonly<Record<Exclude<MailOutboxDecision, 'accepted'>, MessageKey>> = {
    alreadyBeingSent: 'compose.alreadyBeingSent',
    pastRecall: 'outbox.movedOn',
    noSuchSend: 'compose.noSuchSend',
    refusalNotRestated: 'outbox.movedOn',
};

const notReopened: Readonly<Record<Exclude<MailOutboxDecision, 'accepted'> | 'noDraftToReopen', MessageKey>> = {
    ...notTaken,
    noDraftToReopen: 'outbox.noDraftToReopen',
};

interface Answered {
    readonly account: string;
    readonly result: ClientResult<MailOutboxPage>;
}

export function MailOutboxProvider({
    session,
    transport,
    online,
    asked: askedElsewhere = null,
    onAskedTaken,
    children,
}: {
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;
    readonly online: boolean;

    /** A decision asked for from a notification about a send, taken here once that send is on the open page. */
    readonly asked?: OutboxDecisionAsked | null;

    /** Told that the decision asked for has been taken, or dropped because its send is no longer in the Outbox. */
    readonly onAskedTaken?: () => void;

    readonly children: ReactNode;
}) {
    const composing = useComposing();
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const { workspace, revise } = useWorkspace();
    const toasts = useToasts();
    const signalledChanges = useSignalledChanges();
    const account = workspace.outbox?.account ?? null;
    const [answered, setAnswered] = useState<Answered | null>(null);
    const [asked, setAsked] = useState(0);
    const [hidden, setHidden] = useState<readonly { readonly account: string; readonly id: string }[]>([]);
    const [underWay, setUnderWay] = useState<readonly OutboxDecisionUnderWay[]>([]);

    // The page before the one arriving, which is what says a send that was being sent has left.
    const lastSends = useRef<{ readonly account: string; readonly sends: readonly OutgoingMail[] } | null>(null);
    const entry = workspace.outbox?.entry ?? null;

    const [now] = useState(() => Date.now());
    const wording: OutboxWording = { locale, timeZone, translate, now };

    // A newer page arrived: say once what left while it was being sent, and stop holding open a send that has gone.
    const arrived = useEffectEvent((sends: readonly OutgoingMail[]) => {
        const before = lastSends.current?.account === account ? lastSends.current.sends : null;
        lastSends.current = account === null ? null : { account, sends };
        const still = new Set(sends.map((send) => send.outgoingEmailId));

        (before ?? [])
            .filter((send) => standingOf(send) === 'sending' && !still.has(send.outgoingEmailId))
            .forEach((send) => {
                toasts.raise({
                    kind: 'success',
                    title: translate('compose.sentTitle'),
                    body:
                        send.subject === null
                            ? translate('outbox.sentUntitled')
                            : translate('outbox.sent', { subject: send.subject }),
                });
            });

        if (account !== null && entry !== null && !still.has(entry)) {
            revise({ outbox: { account, entry: null } });
        }
    });

    // A decision asked from a notification is taken where the send is drawn, so it is held until the page that holds
    // the send has been read, and dropped with a sentence when that page no longer holds it.
    const takeTheAskedDecision = useEffectEvent((decision: OutboxDecisionAsked, sends: readonly OutgoingMail[]) => {
        onAskedTaken?.();

        const send = sends.find((listed) => listed.outgoingEmailId === decision.outgoingEmailId);

        if (send === undefined) {
            toasts.raise({
                kind: 'warning',
                title: translate('outbox.notDoneTitle'),
                body: translate('compose.noSuchSend'),
            });

            return;
        }

        decision.onTaken();

        if (decision.decision === 'again') {
            sendAgain(send);
        } else {
            edit(send);
        }
    });

    useEffect(() => {
        if (account === null || session === null || !online) {
            return;
        }

        let listening = true;

        void readMailOutbox(session, transport, account).then((result) => {
            if (!listening) {
                return;
            }

            // A read under a page already drawn that the deployment did not answer leaves the page standing, for the
            // reason the folder tree keeps its tree: what is drawn is still the truest thing anybody has.
            setAnswered((current) =>
                result.outcome === 'failed' && current?.account === account && current.result.outcome === 'read'
                    ? current
                    : { account, result },
            );

            if (result.outcome === 'read') {
                // A send stays hidden for as long as its own account's page still lists it, so one the deployment took
                // a dismissal of is not drawn again between that answer and the read that no longer holds it.
                const listed = new Set(result.value.sends.map((send) => send.outgoingEmailId));

                setHidden((current) => current.filter((held) => held.account !== account || listed.has(held.id)));
                arrived(result.value.sends);

                // A decision asked from a notification is taken once the page holding its send has been read.
                if (askedElsewhere?.account === account) {
                    takeTheAskedDecision(askedElsewhere, result.value.sends);
                }
            }
        });

        return () => {
            listening = false;
        };
    }, [account, session, transport, online, asked, askedElsewhere]);

    // An Outbox has no folder to signal about, so its own signal names the account whose Outbox moved, and only the
    // one open is read again. A refresh reads it again for the reason it reads everything again.
    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (signal.kind === 'refresh' || (signal.kind === 'outbox.changed' && signal.account === account)) {
                    setAsked((token) => token + 1);
                }
            }),
        [signalledChanges, account],
    );

    function readAgain(): void {
        setAsked((token) => token + 1);
    }

    async function goToSent(): Promise<void> {
        if (session === null || account === null) {
            return;
        }

        const leaving = account;
        const sent = await readSentFolder(session, transport, leaving);

        revise(({ outbox: shown }) =>
            shown?.account !== leaving ? {} : sent === null ? { outbox: null } : { scope: sent },
        );
    }

    // A decision nobody answered is not known to have been refused: it may have been taken, so what is said is that it is
    // not known, and the Outbox — read again here — is what shows where the send stands once the server answers.
    function failedDecision(failure: ClientFailure): Toast {
        return wentUnanswered(failure)
            ? { kind: 'warning', title: translate('outbox.unknownTitle'), body: translate('outbox.decisionUnknown') }
            : {
                  kind: 'error',
                  title: translate('outbox.notDoneTitle'),
                  body: translate('outbox.decisionFailed', { reason: translate(failureLabels[failure.reason]) }),
              };
    }

    function decided(result: ClientResult<MailOutboxDecision>, taken: Toast): void {
        readAgain();

        if (result.outcome === 'failed') {
            toasts.raise(failedDecision(result.failure));

            return;
        }

        toasts.raise(
            result.value === 'accepted'
                ? taken
                : {
                      kind: 'warning',
                      title: translate('outbox.notDoneTitle'),
                      body: translate(notTaken[result.value]),
                  },
        );
    }

    function heldByDecision(send: OutgoingMail): boolean {
        return underWay.some((held) => held.outgoingEmailId === send.outgoingEmailId);
    }

    // A second press while the first is unanswered would ask again, and the deployment would answer the repeat as a
    // decision it cannot take beside the first one's success.
    function holdWhileAsking<T>(
        send: OutgoingMail,
        decision: OutboxDecisionUnderWay['decision'],
        asked: Promise<T>,
    ): Promise<T> {
        const outgoingEmailId = send.outgoingEmailId;

        setUnderWay((current) => [...current, { outgoingEmailId, decision }]);

        return asked.finally(() => {
            setUnderWay((current) => current.filter((held) => held.outgoingEmailId !== outgoingEmailId));
        });
    }

    function cancel(send: OutgoingMail): void {
        if (session === null || heldByDecision(send)) {
            return;
        }

        void holdWhileAsking(send, 'cancel', withdrawOutgoingMail(session, transport, send.outgoingEmailId)).then(
            (result) => {
                decided(
                    result.outcome === 'failed'
                        ? result
                        : { outcome: 'read', value: result.value === 'withdrawn' ? 'accepted' : result.value },
                    {
                        kind: 'success',
                        title: translate('outbox.cancelledTitle'),
                        body: translate('outbox.cancelled', {
                            names: addresseeOf(send.recipients, send.otherRecipientCount, wording),
                        }),
                    },
                );
            },
        );
    }

    function sendAgain(send: OutgoingMail): void {
        if (session === null || heldByDecision(send)) {
            return;
        }

        // A refused send is offered again only by somebody who has seen the refusal, which this press is: the preview
        // it is pressed in states the server's reason above it.
        const refusalRestated = standingOf(send) === 'refused';

        void holdWhileAsking(
            send,
            'again',
            offerOutgoingMailAgain(session, transport, send.outgoingEmailId, refusalRestated),
        ).then((result) => {
            decided(result, {
                kind: 'neutral',
                title: translate('outbox.againTitle'),
                body: translate('outbox.again'),
            });
        });
    }

    function dismiss(send: OutgoingMail): void {
        if (session === null || account === null || heldByDecision(send)) {
            return;
        }

        const id = send.outgoingEmailId;
        let undone = false;

        setHidden((current) => [...current, { account, id }]);

        if (entry === id) {
            revise({ outbox: { account, entry: null } });
        }

        toasts.raise({
            kind: 'neutral',
            title: translate('outbox.dismissedTitle'),
            body:
                send.subject === null
                    ? translate('outbox.dismissedUntitled')
                    : translate('outbox.dismissed', { subject: send.subject }),
            action: {
                label: translate('act.undo'),
                take: () => {
                    undone = true;
                    setHidden((current) => current.filter((held) => held.id !== id));
                },
            },
            whenGone: () => {
                if (undone) {
                    return;
                }

                void dismissOutgoingMail(session, transport, id).then((result) => {
                    if (result.outcome === 'failed' || result.value !== 'accepted') {
                        setHidden((current) => current.filter((held) => held.id !== id));
                        decided(result, { kind: 'neutral', title: translate('outbox.dismissedTitle') });
                    }

                    readAgain();
                });
            },
        });
    }

    function edit(send: OutgoingMail): void {
        if (session === null || heldByDecision(send)) {
            return;
        }

        const id = send.outgoingEmailId;

        void holdWhileAsking(send, 'edit', reopenOutgoingMailAsDraft(session, transport, id)).then((result) => {
            readAgain();

            if (result.outcome === 'failed') {
                toasts.raise(failedDecision(result.failure));

                return;
            }

            if (!result.value.reopened) {
                toasts.raise({
                    kind: 'warning',
                    title: translate('outbox.notDoneTitle'),
                    body: translate(notReopened[result.value.decision]),
                });

                return;
            }

            revise(({ outbox }) => (outbox?.entry === id ? { outbox: { ...outbox, entry: null } } : {}));
            composing.compose({ kind: 'held', draftId: result.value.draftId });
            toasts.raise({
                kind: 'neutral',
                title: translate('outbox.backInDraftsTitle'),
                body:
                    send.subject === null
                        ? translate('outbox.backInDraftsUntitled')
                        : translate('outbox.backInDrafts', { subject: send.subject }),
            });
        });
    }

    const page: OutboxPage = !online
        ? { kind: 'offline' }
        : account === null || answered?.account !== account
          ? { kind: 'reading' }
          : answered.result.outcome === 'failed'
            ? { kind: 'failed', reason: answered.result.failure.reason }
            : {
                  kind: 'read',
                  sends: answered.result.value.sends.filter(
                      (send) => !hidden.some((held) => held.id === send.outgoingEmailId),
                  ),
              };

    const outbox: MailOutbox = { page, underWay, readAgain, goToSent, cancel, sendAgain, dismiss, edit };

    return <MailOutboxContext value={outbox}>{children}</MailOutboxContext>;
}
