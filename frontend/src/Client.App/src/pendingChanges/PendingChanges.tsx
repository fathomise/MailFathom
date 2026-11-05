// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useState, type ReactNode } from 'react';
import { wentUnanswered, type ClientSession, type MailMutationOutcome } from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { disconnected, useConnectionStatus } from '../shell/connectionStatus';
import { useToasts } from '../toasts/useToasts';
import { failureLabels, refusalReasons, refusalTitles } from './changeWording';
import { refusalsIn, type ChangeSubmission } from './changeSubmission';
import {
    PendingChangesContext,
    unansweredWriteRetryDelay,
    type AbandonedChange,
    type PendingChanges,
} from './usePendingChanges';

// What becomes of a change this client asked for, read off the deployment's answer and nothing after it. The
// deployment answers a mailbox act with the change already made in what MailFathom holds, so the screen keeps drawing
// it and nobody is told anything more: what has not reached the mail server yet is the account freshness line's to
// say, and a change the mail server refuses later is undone by the deployment and comes back as a notification.
//
// What an answer can still say here is a refusal, and the one thing it can fail to say is anything at all:
//
// - A refusal the server stated wrote nothing down, so the screen stops claiming the change and the sentence says
//   why — one per reason rather than a card each. `already-in-destination` is the one refusal nobody hears, because the
//   mailbox already says what was asked for.
// - A write nobody answered most likely landed, since an act commits on arrival. So it stays drawn as asked, is held
//   in this tab, and is sent again under the key it first went with once the server answers — the deployment answers a
//   repeat under that key with the first attempt's outcome, so it lands once however often it is sent. Its answer then
//   settles it like any other. It is held in memory and nowhere else, which is
//   [ADR 0028](../../../../../docs/decisions/0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md)'s
//   decision: closing the tab drops it, and the toast says so.

/** The writes nobody answered, and whose they are, so one person's writes are never sent under the next one's credential. */
interface Held {
    readonly session: ClientSession | null;
    readonly unanswered: readonly ChangeSubmission[];
}

const nothingHeld: Held = { session: null, unanswered: [] };

const noneAbandoned: readonly AbandonedChange[] = [];

export function PendingChangesProvider({
    session,
    abandoned = noneAbandoned,
    onDecided,
    children,
}: {
    /** Who is asking and where, or `null` where there is nobody to hold a change for. */
    readonly session: ClientSession | null;

    /** The changes the deployment undid that are still waiting on the person, as the notification centre holds them. */
    readonly abandoned?: readonly AbandonedChange[];

    /** Told when the person settles a change, wherever it was settled, which is what marks its notification read. */
    readonly onDecided?: (recordId: string) => void;

    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const lostServer = disconnected(useConnectionStatus());
    const [held, setHeld] = useState<Held>(nothingHeld);

    // Derived rather than cleared, for the reason `readMarking/ReadMarking.tsx` gives: signing out and back in on one
    // tab keeps this component mounted, and the previous person's writes would otherwise be sent under the next
    // person's credential.
    const unanswered = held.session === session ? held.unanswered : nothingHeld.unanswered;

    // Sends every held write again once the server answers, which is a timer and so an effect's. Each is taken out of
    // what is held as it goes, and comes back through `follow` with its answer — held again if nobody answered that
    // either, so the wait re-arms rather than repeating a request in the same instant.
    useEffect(() => {
        if (session === null || unanswered.length === 0 || lostServer) {
            return;
        }

        const sending = unanswered;
        const waiting = setTimeout(() => {
            setHeld((current) =>
                current.session === session
                    ? { session, unanswered: current.unanswered.filter((one) => !sending.includes(one)) }
                    : current,
            );

            for (const submission of sending) {
                submission.sendAgain();
            }
        }, unansweredWriteRetryDelay);

        return () => {
            clearTimeout(waiting);
        };
    }, [session, unanswered, lostServer]);

    function report(submission: ChangeSubmission, outcome: MailMutationOutcome, storedEmailIds: readonly string[]) {
        const reason = refusalReasons[outcome];

        if (reason === null) {
            return;
        }

        // The screen stops claiming a change the deployment wrote nothing down for, before the sentence saying so: a
        // row that goes on showing what a mailbox refused is the silent loss this whole feature exists against.
        submission.letGo(storedEmailIds);

        toasts.raise({
            kind: 'warning',
            title: translate(
                refusalTitles[submission.act][new Intl.PluralRules(locale).select(storedEmailIds.length)],
                {
                    count: new Intl.NumberFormat(locale).format(storedEmailIds.length),
                },
            ),
            body: translate(reason),
        });
    }

    function follow(submission: ChangeSubmission): void {
        // The server answered with a refusal it stated, so nothing was written down and the mailbox is untouched: the
        // screen stops claiming the change, and what is owed is the sentence and the way back, which is a new act.
        if (submission.results === null && submission.failure !== null && !wentUnanswered(submission.failure)) {
            const reason = submission.failure.reason;

            submission.letGo(submission.asked);
            toasts.raise({
                kind: 'error',
                title: translate('pendingChange.refused'),
                body: translate('pendingChange.refusedBody', { reason: translate(failureLabels[reason]) }),
                action: {
                    label: translate('pendingChange.retry'),
                    take: () => {
                        submission.askAgain(submission.asked);
                    },
                },
            });

            return;
        }

        // Nothing answered — or a gateway did, which proves nothing about whether the request was handed on. The act
        // stays drawn as asked and the write is held to be sent again; nothing offers to ask for it afresh, which could
        // record it twice. It is said once, when it first goes unanswered, rather than on every repeat that meets the
        // same silence.
        if (submission.results === null) {
            setHeld((current) => {
                const carried = current.session === session ? current.unanswered : [];

                return { session, unanswered: [...carried, submission] };
            });

            if (!submission.resent) {
                toasts.raise({
                    kind: 'warning',
                    title: translate('pendingChange.outcomeUnknown'),
                    body: translate('pendingChange.outcomeUnknownBody'),
                });
            }

            return;
        }

        for (const [outcome, storedEmailIds] of refusalsIn(submission.results)) {
            report(submission, outcome, storedEmailIds);
        }
    }

    const value: PendingChanges = {
        unanswered: unanswered.reduce((count, submission) => count + submission.asked.length, 0),
        abandoned,
        follow,
        settle: (recordId) => {
            onDecided?.(recordId);
        },
    };

    return <PendingChangesContext value={value}>{children}</PendingChangesContext>;
}
