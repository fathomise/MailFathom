// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useEffectEvent, useState, type ReactNode } from 'react';
import {
    mostRecordsPerRead,
    readMailMutationRecords,
    wentUnanswered,
    type ClientSession,
    type MailFathomTransport,
    type MailMutationOutcome,
} from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { useListedMail, type DrawnMail } from '../messageList/useListedMail';
import { disconnected, saidByTheStatus, useConnectionStatus } from '../shell/connectionStatus';
import { useSignalledChanges } from '../signals/signalledChanges';
import { useToasts } from '../toasts/useToasts';
import { failureLabels, refusalReasons, refusalTitles, standingReasons } from './changeWording';
import {
    landedAs,
    readSubmission,
    standingOf,
    type ChangeAct,
    type ChangeSubmission,
    type PendingChange,
} from './changeStandings';
import {
    followedChangeInterval,
    mostFollowingAttempts,
    PendingChangesContext,
    type AbandonedChange,
    type ChangeResolution,
    type PendingChanges,
    type UndecidedChange,
} from './usePendingChanges';

// What this client has asked its mailbox for since it was opened, followed until each change lands or turns into a
// question. It holds nothing durable, which is
// [ADR 0028](../../../../../docs/decisions/0028-no-mail-on-the-device-and-an-honest-client-with-no-route-to-its-deployment.md)'s
// decision rather than this module's: a change reaches the deployment or it does not happen, so what is followed here
// is always a record the deployment already wrote down, and this goes when the tab does.
//
// Following one is worth doing because writing it down is not the same as making it. The deployment answers a
// submission the moment the record exists, and the account's own reconciliation pass is what tells the mail server
// minutes later — so between those two moments a person has changed something and nobody has told them whether it took.
// `changeStandings.ts` holds the rule deciding which of those endings anybody hears about.
//
// The rule's other half is at submission time and is stated here because it is about an answer rather than a record: a
// message the deployment refused wrote nothing down, so it never enters the queue, and what it is owed is one sentence
// per reason rather than a card each. `already-in-destination` is the one refusal nobody hears, because the mailbox
// already says what was asked for — two clients asking for the same thing is not a collision, and the screen was right.

/** What is held, and whose it is, so one person's changes never outlive the credential they were asked for under. */
interface Held {
    readonly session: ClientSession | null;
    readonly followed: readonly FollowedChange[];
    readonly undecided: readonly HeldUndecided[];
    readonly unknown: readonly UnknownChange[];
}

/**
 * A change whose write nobody answered, held until the read made once the server answers again says where it stands.
 *
 * It is drawn as asked until then, because once an act is committed on arrival a write that went out has most likely
 * landed and only its answer was lost. It has no record to follow — none was answered — so it is held against what the
 * lists draw instead: the first page asked for after it went unanswered that draws the message settles it.
 */
interface UnknownChange {
    readonly act: ChangeAct;
    readonly storedEmailId: string;

    /** The folder the act was asked in, which a filing act's landing is read against. */
    readonly askedIn: string | null;

    /** When the write ended unanswered; a page asked for before it cannot say what became of the write. */
    readonly since: number;

    /** Stops drawing the act, so what the server holds is drawn instead. */
    readonly letGo: () => void;
}

/** A change being followed, beside what its producer said asking again and letting go mean for it. */
interface FollowedChange extends PendingChange {
    readonly askAgain: () => void;
    readonly letGo: () => void;
}

interface HeldUndecided extends UndecidedChange {
    readonly askAgain: () => void;
    readonly letGo: () => void;
}

const nothingHeld: Held = { session: null, followed: [], undecided: [], unknown: [] };

const noneAbandoned: readonly AbandonedChange[] = [];

/** How many reads running the deployment has not answered, and whose credential it did not answer them under. */
interface Failures {
    readonly session: ClientSession | null;
    readonly count: number;
}

const noFailures: Failures = { session: null, count: 0 };

export function PendingChangesProvider({
    session,
    transport,
    abandoned = noneAbandoned,
    onFollowed,
    onDecided,
    children,
}: {
    /** Who is asking and where, or `null` where there is nobody to follow a change for. */
    readonly session: ClientSession | null;
    readonly transport: MailFathomTransport;

    /** The changes the deployment gave up on that are still waiting on the person, as the notification centre holds them. */
    readonly abandoned?: readonly AbandonedChange[];

    /**
     * Told the records this tab starts following, which is what keeps the notification a given-up one raises from
     * speaking a second time over the toast this queue raises for it.
     */
    readonly onFollowed?: (recordIds: readonly string[]) => void;

    /** Told when the person settles a change, wherever it was settled, which is what marks its notification read. */
    readonly onDecided?: (recordId: string) => void;

    readonly children: ReactNode;
}) {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const status = useConnectionStatus();
    const listed = useListedMail();
    const changes = useSignalledChanges();

    // While the server is out of reach the status already says so, and asking where changes stand would only count
    // failures towards giving up and then say so a second time. Following stops without counting, and starts again —
    // from the attempts it had — the moment the server answers.
    const lostServer = disconnected(status);
    const [held, setHeld] = useState<Held>(nothingHeld);
    const [failures, setFailures] = useState<Failures>(noFailures);
    const [round, setRound] = useState(0);

    // Derived rather than cleared, for the reason `readMarking/ReadMarking.tsx` gives: signing out and back in on one
    // tab keeps this component mounted, and the previous person's changes would otherwise be followed under the next
    // person's credential. The failure count is derived the same way and for the same reason: a deployment that
    // stopped answering one credential has said nothing about the next, and the next would otherwise arrive at a
    // client that had already given up without ever asking for them.
    const inForce = held.session === session ? held : nothingHeld;
    const attempts = failures.session === session ? failures.count : 0;
    const following = inForce.followed;
    const stoppedFollowing = attempts >= mostFollowingAttempts;

    // What re-arms the wait is a round of its own rather than the queue it read, and that is the whole reason `round`
    // exists. Adding a change gives `followed` a new array, so an effect depending on the queue would cancel its own
    // pending wait every time somebody opened another message — and a person reading faster than the interval would
    // defer the read indefinitely, which is exactly the bounded following this module promises not to do.
    const follows = following.length > 0;

    // Reads the queue, the credential, and the failure count as they are when the wait ends rather than as they were
    // when it was armed, which is what lets the wait be armed once and left alone. Adding a change while one is in
    // flight changes what the next read asks about; it never changes when that read happens.
    const readWhereChangesStand = useEffectEvent((stillWanted: () => boolean) => {
        if (session === null) {
            return;
        }

        const asking = session;

        // The oldest first and a page at a time, which is what replaying the queue in order means here: the route
        // names each record in the request line and refuses a longer read whole.
        const page = following.slice(0, mostRecordsPerRead);

        void (async () => {
            const answer = await readMailMutationRecords(
                asking,
                transport,
                page.map((change) => change.recordId),
            );

            if (!stillWanted()) {
                return;
            }

            setRound((current) => current + 1);

            if (answer.outcome === 'failed' && saidByTheStatus(status.disconnectedNow, answer.failure.reason)) {
                return;
            }

            if (answer.outcome === 'failed') {
                // Only a deployment that did not answer is worth asking again. A credential it refused, or an answer
                // this client could not read, is the same on the fifth attempt as on the first — so following stops
                // there and then, and says which of the two happened rather than claiming nothing answered.
                if (answer.failure.reason !== 'unavailable') {
                    setFailures({ session: asking, count: mostFollowingAttempts });
                    toasts.raise({
                        kind: 'error',
                        title: translate('pendingChange.stoppedFollowing'),
                        body: translate('pendingChange.stoppedFollowingReason', {
                            reason: translate(failureLabels[answer.failure.reason]),
                        }),
                    });

                    return;
                }

                setFailures({ session: asking, count: attempts + 1 });

                // Said where the budget runs out rather than from a render reading that it has: the same sentence
                // raised by every later render would be the client telling somebody once a frame.
                if (attempts + 1 >= mostFollowingAttempts) {
                    toasts.raise({
                        kind: 'warning',
                        title: translate('pendingChange.stoppedFollowing'),
                        body: translate('pendingChange.stoppedFollowingBody', {
                            total: new Intl.NumberFormat(locale).format(mostFollowingAttempts),
                        }),
                        action: {
                            label: translate('pendingChange.followAgain'),
                            take: () => {
                                setFailures({ session: asking, count: 0 });
                            },
                        },
                    });
                }

                return;
            }

            const standings = new Map(answer.value.map((record) => [record.recordId, standingOf(record)]));
            const stillWaiting: FollowedChange[] = [];
            const undecided: HeldUndecided[] = [];

            for (const change of page) {
                const standing = standings.get(change.recordId);

                // A record the deployment no longer answers for is absent from the answer rather than refused, which
                // is what a folder this credential may no longer read looks like from here. There is nothing left to
                // follow and nothing that failed, so it leaves the queue in silence.
                if (standing === undefined || standing === 'converged') {
                    continue;
                }

                if (standing === 'waiting') {
                    stillWaiting.push(change);
                } else {
                    undecided.push({ ...change, standing });
                }
            }

            setHeld((current) =>
                current.session === asking
                    ? {
                          session: asking,
                          followed: [...stillWaiting, ...current.followed.slice(page.length)],
                          undecided: [...current.undecided, ...undecided],
                          unknown: current.unknown,
                      }
                    : current,
            );
            setFailures({ session: asking, count: 0 });

            for (const change of undecided) {
                toasts.raise({
                    kind: change.standing === 'exhausted' ? 'error' : 'warning',
                    title: translate('pendingChange.undecided'),
                    body: translate(standingReasons[change.standing]),
                    action: { label: translate('pendingChange.askAgain'), take: change.askAgain },
                });
            }
        })();
    });

    useEffect(() => {
        if (session === null || !follows || attempts >= mostFollowingAttempts || lostServer) {
            return;
        }

        let abandoned = false;
        const waiting = setTimeout(() => {
            readWhereChangesStand(() => !abandoned);
        }, followedChangeInterval);

        return () => {
            abandoned = true;
            clearTimeout(waiting);
        };
    }, [session, follows, attempts, round, lostServer]);

    // What an unknown write became, read off the first page that draws its message after it went unanswered. One whose
    // act the server shows is let be — what it draws is what the server holds. One the server does not show is let go
    // of, so the server's own state is drawn, and that is said once for every such write the page settles.
    const settleUnknown = useEffectEvent((emails: readonly DrawnMail[], askedAt: number) => {
        const drawn = new Map(emails.map((email) => [email.id, email]));
        const settled = inForce.unknown.filter((change) => askedAt >= change.since && drawn.has(change.storedEmailId));

        if (settled.length === 0) {
            return;
        }

        const settledNow = new Set(settled);
        const notMade = settled.filter((change) => {
            const email = drawn.get(change.storedEmailId);

            return email !== undefined && !landedAs(change.act, change.askedIn, email);
        });

        setHeld((current) =>
            current.session === inForce.session
                ? { ...current, unknown: current.unknown.filter((change) => !settledNow.has(change)) }
                : current,
        );

        for (const change of notMade) {
            change.letGo();
        }

        if (notMade.length > 0) {
            toasts.raise({
                kind: 'info',
                title: translate('pendingChange.reread'),
                body: translate('pendingChange.rereadBody'),
            });
        }
    });

    useEffect(
        () =>
            listed.observe((emails, askedAt) => {
                settleUnknown(emails, askedAt);
            }),
        [listed],
    );

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
        // screen stops claiming the change, and what is owed is the sentence and the way back.
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

        // Nothing answered — or a gateway did, which proves nothing about whether the request was handed on. A client
        // that heard nothing cannot know the change did not land, and once acts commit on arrival it most likely did.
        // So the act stays drawn as asked, nothing offers to send it again, and everything is read again: the read made
        // once the server answers settles each message, through `settleUnknown` above.
        if (submission.results === null) {
            const since = Date.now();
            const arriving = submission.asked.map<UnknownChange>((storedEmailId) => ({
                act: submission.act,
                storedEmailId,
                askedIn: submission.askedIn.get(storedEmailId) ?? null,
                since,
                letGo: () => {
                    submission.letGo([storedEmailId]);
                },
            }));

            setHeld((current) => {
                const carried = current.session === session ? current : nothingHeld;

                return { ...carried, session, unknown: [...carried.unknown, ...arriving] };
            });
            toasts.raise({
                kind: 'warning',
                title: translate('pendingChange.outcomeUnknown'),
                body: translate('pendingChange.outcomeUnknownBody'),
            });

            // Read again at once where the server is answering; where it is not, the status reads every screen again
            // the moment it does.
            if (!status.disconnectedNow()) {
                changes.refresh();
            }

            return;
        }

        const reading = readSubmission(submission);

        for (const [outcome, storedEmailIds] of reading.refused) {
            report(submission, outcome, storedEmailIds);
        }

        if (reading.followed.length === 0) {
            return;
        }

        const arriving = reading.followed.map<FollowedChange>((change) => ({
            ...change,
            askAgain: () => {
                submission.askAgain([change.storedEmailId]);
            },
            letGo: () => {
                submission.letGo([change.storedEmailId]);
            },
        }));

        // The failure count is the poll's own and is cleared by the poll alone. A change arriving says nothing about
        // whether the deployment has started answering, and clearing it here would let somebody marking mail read
        // hold the client at four failures for ever — never stopping, and never saying it had.
        setHeld((current) => {
            const carried = current.session === session ? current : nothingHeld;

            return { ...carried, session, followed: [...carried.followed, ...arriving] };
        });
        onFollowed?.(arriving.map((change) => change.recordId));
    }

    function settle(recordId: string, resolution: ChangeResolution): void {
        const change = inForce.undecided.find((undecided) => undecided.recordId === recordId);

        onDecided?.(recordId);

        if (change === undefined) {
            return;
        }

        setHeld((current) => ({
            ...current,
            undecided: current.undecided.filter((undecided) => undecided.recordId !== recordId),
        }));

        // Asking again is the same act submitted afresh rather than a record revived, so it travels the confirmation
        // and convergence path its first attempt did and arrives back here as a new change to follow.
        if (resolution === 'askAgain') {
            change.askAgain();
        } else {
            change.letGo();
        }
    }

    // One change is one question, so a given-up record this tab is still following or already asking about is asked
    // about once, here: the queue decides it, and its notification never stands beside it as a second question.
    const askedHere = new Set([...inForce.followed, ...inForce.undecided].map((change) => change.recordId));

    const value: PendingChanges = {
        waiting: inForce.followed,
        undecided: inForce.undecided,
        abandoned: abandoned.filter((change) => !askedHere.has(change.recordId)),
        stoppedFollowing,
        follow,
        settle,
        followAgain: () => {
            setFailures({ session, count: 0 });
        },
    };

    return <PendingChangesContext value={value}>{children}</PendingChangesContext>;
}
