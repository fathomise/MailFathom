// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useCallback, useEffect, useRef, useState } from 'react';
import {
    deleteNotifications,
    markAllNotificationsRead,
    readNotifications,
    readUnreadNotificationCount,
    setNotificationRead,
    type ClientFailureReason,
    type ClientNotification,
    type ClientSession,
    type MailAccount,
    type MailFathomTransport,
    type NotificationTarget,
} from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { chooseSystemNotifications, systemNotificationsChosen } from '../preferences/systemNotifications';
import { useSystemNotifier } from '../shellOperations/systemNotifier';
import { useSignalledChanges } from '../signals/signalledChanges';
import { arrivalCounts } from './arrivalCounts';
import { failureOf } from './failures';
import { systemNotificationCounts, toastKindOf } from './notificationKinds';
import { wordNotificationToast } from './notificationWords';
import { useToasts } from '../toasts/useToasts';

// What the client knows about the person's notification centre, and everything that changes it. It is one hook rather
// than a store beside the frame because all of it belongs to one credential and goes with it: the count on the bell,
// the page the panel draws, and the two ways of marking something read are one reading of one thing.
//
// **The count is asked for on an interval, and again whenever anything says it should be.** The interval is the floor:
// a deployment that raised a notification says so over the signal channel and the count is read then, and the window
// coming back to the front reads it as well — the moment a person is most likely to be looking at the bell and the
// least likely to have had a poll land. A deployment serving no channel, or one this client cannot reach, therefore
// behaves exactly as it did before there was one. The list is asked for when there is somewhere to draw it and when
// the count says something has arrived, never on the interval: a badge that cost a screenful of rows to draw would be
// the most expensive thing on a polling client's schedule.
//
// **A read state change goes out as it is made.** The row is redrawn from what was asked for and the badge from what
// the deployment answered, so one exchange settles both; a change that failed is said out loud and taken back rather
// than left on the screen as something that did not happen.

/** How long the client waits between asking how much stands unread. */
export const unreadCountInterval = 60_000;

/** How many notifications the panel asks for, which is the window it draws rather than the whole centre. */
export const notificationsShown = 50;

/** The most toasts one arrival raises, so a burst that landed between two polls does not bury the screen. */
export const mostArrivalsAnnounced = 3;

/** Which of the two tabs the panel is filtering by. */
export type NotificationFilter = 'all' | 'unread';

export interface NotificationCentre {
    /** How many of the person's notifications stand unread, which is the whole of what the bell draws. */
    readonly unreadCount: number;

    readonly shown: boolean;

    /** The window of the centre the panel draws, newest first. */
    readonly notifications: readonly ClientNotification[];

    /**
     * The rows that were not there the last time this client read the centre, which is what a row opening itself is
     * drawn from. On the first read of a credential's centre it holds only the rows the count rose by, because
     * everything else is what the client already had rather than what arrived.
     */
    readonly arrived: ReadonlySet<string>;

    /** Whether the page behind the panel is being read for the first time, which is what the panel says while it waits. */
    readonly reading: boolean;

    /** Why the page could not be read, or `null` where it was. */
    readonly failure: ClientFailureReason | null;

    readonly show: () => void;
    readonly hide: () => void;

    /** Puts one notification into the read state stated, which is what the row's own control does. */
    readonly markRead: (ids: readonly string[], read: boolean) => void;

    /** Marks every unread notification read, in one request. */
    readonly markAllRead: () => void;

    /** Removes the named notifications from the centre for good, in one request. */
    readonly remove: (ids: readonly string[]) => void;

    /**
     * Reads one notification and goes where it leads, which is what a click on the row does.
     *
     * A change the mailbox did not take is the one row following does not mark read: it leads to the message so the
     * change can be decided there, and the decision is what marks it read — a row read on the way would take the
     * question off the message before anybody answered it.
     */
    readonly follow: (notification: ClientNotification) => void;

    /**
     * Marks read every row about one change, which deciding it does wherever it is decided.
     *
     * A row the panel has not read yet is marked once it arrives, because a change decided in this tab is a change
     * nobody has to be told about again — however the durable row and the decision crossed on the way.
     */
    readonly changeDecided: (recordId: string) => void;
}

/**
 * Whether this tab is already following a change, which is what keeps a failure from being said twice.
 *
 * A change this tab asked for is followed by the pending-changes queue, which puts its own line and toast in front of
 * the person when the mailbox does not take it. The durable row that same failure raises is still listed in the centre,
 * but its toast is not raised: one failure, one voice.
 */
export type FollowedHere = (recordId: string) => boolean;

/** Which notifications have already been drawn or announced, and whose reading of the centre that was. */
interface Seen {
    readonly session: ClientSession;
    readonly ids: ReadonlySet<string>;
}

/** A count of unread notifications, and whose it was. */
interface Counted {
    readonly session: ClientSession;
    readonly count: number;
}

/**
 * What the count says arrived since the page was last read: how far it rose, and how many raises the deployment
 * signalled. Each undercounts in its own case — a merge lowers the count, and several rows written together are one
 * signal — so what arrived is the larger of the two rather than their sum, which would count one arrival twice once a
 * poll and its signal both saw it.
 */
interface Risen {
    readonly session: ClientSession;
    readonly measured: number;
    readonly raised: number;
}

/**
 * Holds the centre for as long as one credential does.
 *
 * @param session Who is asking, or `null` where nobody is signed in or the credential may not read mail.
 * @param transport How a request reaches the deployment.
 * @param online Whether this machine has a network, which is what stops the interval rather than a failing read.
 * @param onFollow Where a notification leads, which is the frame's answer rather than this hook's.
 * @param accounts The person's mail accounts, which is what a failure's toast names its account by.
 * @param followedHere Whether this tab already follows a change, which is what silences its durable row's toast.
 */
export function useNotificationCentre(
    session: ClientSession | null,
    transport: MailFathomTransport,
    online: boolean,
    onFollow: (target: NotificationTarget) => void,
    accounts: readonly MailAccount[] = noAccounts,
    followedHere: FollowedHere = followsNothing,
): NotificationCentre {
    const { locale, translate } = useLocalization();
    const toasts = useToasts();
    const signalledChanges = useSignalledChanges();
    const notifier = useSystemNotifier();
    const [unreadCount, setUnreadCount] = useState(0);
    const [shown, setShown] = useState(false);
    const [notifications, setNotifications] = useState<readonly ClientNotification[]>([]);
    const [arrived, setArrived] = useState<ReadonlySet<string>>(new Set());
    const [reading, setReading] = useState(false);
    const [failure, setFailure] = useState<ClientFailureReason | null>(null);

    // What a page read is asked for by, so that opening the panel and something having arrived are one mechanism
    // rather than two effects racing to read the same route.
    const [asked, setAsked] = useState(0);

    // Whether the page read the token asks for next is a refresh's, handed from the listener that asks to the read it
    // starts. A refresh the deployment did not answer says nothing and leaves what is drawn — an empty centre stays the
    // empty state rather than becoming a failure — for the next one to try again. A ref because nothing is drawn from it
    // and setting it must not start a read of its own.
    const quietly = useRef(false);

    // What arrived since the page was last asked for, handed from the count that saw it to the read it asks
    // for. It is what an arrival is measured by where this client has drawn no page to compare against — which is every
    // session until somebody opens the panel — and it is a ref for the reason the flag above is.
    const risen = useRef<Risen | null>(null);

    // What has already been drawn or announced, so an arrival is a notification this client has not seen rather than
    // one it has stopped showing. A ref because nothing on the screen is drawn from it, and it must not restart the
    // reads below when it grows.
    const known = useRef<Seen | null>(null);

    // What the last count read answered, so a rise is measurable without the count itself being a dependency of the
    // effect that reads it — which would restart the interval on every poll that changed anything.
    const counted = useRef<Counted | null>(null);

    // The changes decided in this tab, so a row about one of them that a later page brings is read on arrival rather
    // than asking a second time about something already answered. A ref for the reason `known` is one.
    const decided = useRef<Seen | null>(null);

    // Everything held belongs to the credential that read it, so a sign-out and a sign-in as somebody else start from
    // nothing rather than showing the previous person's centre until the first read lands. It is adjusted while
    // rendering rather than from an effect, because a screen drawn once from the previous person's centre is the whole
    // of what this prevents — and the refs above carry whose they are for the same reason, which is what makes them
    // nothing to reset.
    const [credential, setCredential] = useState(session);

    // The same rule for a write that is still in flight. A read is cancelled by the controller its effect owns, but a
    // write's continuation belongs to the render that started it and cannot be cancelled — so it is asked whose it is
    // when it lands, against a ref that always holds the credential in force rather than the one that render closed
    // over. Without it, a mark-read answered after somebody signed out and somebody else signed in would write the
    // previous account's counted state over what the new one is being shown.
    const inForce = useRef(session);

    useEffect(() => {
        inForce.current = session;
    }, [session]);

    if (credential !== session) {
        setCredential(session);
        setUnreadCount(0);
        setNotifications([]);
        setArrived(new Set());
        setShown(false);
        setFailure(null);
    }

    useEffect(() => {
        if (session === null || !online) {
            return;
        }

        const attempted = new AbortController();

        // `asksForThePage` is false for a count somebody asked for beside a page they are already asking for: the rise
        // below is what turns an unattended count into a page read, and a hand-made refresh has bumped the token
        // itself, so leaving it armed would ask the route twice — the second answer arriving against a `known` the
        // first had already filled, which is a row that arrived and is marked as though it had always been there.
        //
        // `raised` is how many rows the deployment said it wrote, which a rise alone cannot measure: an arrival that
        // merged several standing rows into one leaves the count lower than it was, and one replacing a merged row
        // leaves it where it was, while both are a row nobody has seen yet.
        async function count(asksForThePage: boolean, raised = 0): Promise<void> {
            if (session === null) {
                return;
            }

            const answer = await readUnreadNotificationCount(session, transport);

            if (attempted.signal.aborted || answer.outcome === 'failed') {
                return;
            }

            const before = counted.current?.session === session ? counted.current.count : null;
            const measured = before === null ? 0 : Math.max(0, answer.value - before);
            const rose = measured > 0 || raised > 0;

            counted.current = { session, count: answer.value };
            setUnreadCount(answer.value);

            // Only a rise or a raise asks for the page. A count that fell on its own is this client's own marking
            // landing, and a count that did not move is the ordinary poll — neither is anything a reader has to be told
            // about.
            if (rose && asksForThePage) {
                const standing = risen.current?.session === session ? risen.current : null;

                risen.current = {
                    session,
                    measured: (standing?.measured ?? 0) + measured,
                    raised: (standing?.raised ?? 0) + raised,
                };
                setAsked((token) => token + 1);
            }
        }

        void count(true);

        const polling = window.setInterval(() => {
            void count(true);
        }, unreadCountInterval);

        // Coming back to the window is when somebody looks at the bell, and it is the moment a poll is least likely to
        // have just landed — a machine that was asleep ran no interval at all.
        function returned(): void {
            if (document.visibilityState === 'visible') {
                void count(true);
            }
        }

        document.addEventListener('visibilitychange', returned);

        // Subscribed here rather than in an effect of its own, because what it does is exactly what the interval does
        // and the two are cleaned up by the same credential going away. Only the count is read: what the signal
        // carries is that something was raised, and how many stand unread is the deployment's answer rather than a
        // number this client keeps in step on its own.
        const listening = signalledChanges.listen((signal) => {
            if (signal.kind === 'notification.raised') {
                void count(true, 1);
            }

            // A refresh asks for the page and for the count, and for the count as a count alone: the page is asked for
            // here already, so a rise it finds must not ask for it a second time — the second answer would arrive
            // against a centre the first had filled, and an arrival would be drawn as though it had always been there.
            // The count is read rather than left to the interval because the badge is what a reader who does not open
            // the panel is looking at.
            if (signal.kind === 'refresh') {
                quietly.current = true;
                setAsked((token) => token + 1);
                void count(false);
            }
        });

        return () => {
            attempted.abort();
            window.clearInterval(polling);
            document.removeEventListener('visibilitychange', returned);
            listening();
        };
    }, [session, transport, online, signalledChanges]);

    // Held steady, because the gestures that drive the panel on a phone subscribe to the document with these as their
    // dependencies: a pair rebuilt every render would resubscribe on every one of them.
    const show = useCallback((): void => {
        setShown(true);
    }, []);

    const hide = useCallback((): void => {
        setShown(false);
    }, []);

    // Coming back through the notification the operating system showed is the same act as reaching for the bell, so it
    // lands in the same place: the window the shell brought forward, with the centre open on it. Subscribed
    // unconditionally rather than behind `notifier.offered`, because a head that raises no notification offers a
    // subscription that subscribes to nothing — which is one question fewer for this hook to ask about a head.
    useEffect(() => notifier.whenActedOn(show), [notifier, show]);

    const follow = useCallback(
        (notification: ClientNotification): void => {
            setShown(false);
            onFollow(notification.target);

            if (notification.read || failureOf(notification)?.kind === 'change') {
                return;
            }

            setNotifications((held) => held.map((row) => (row.id === notification.id ? { ...row, read: true } : row)));
            setUnreadCount((standing) => Math.max(0, standing - 1));

            if (session !== null) {
                void setNotificationRead(session, transport, notification.id, true);
            }
        },
        [session, transport, onFollow],
    );

    // What the operating system is told, which is the whole of an arrival's half outside the window. Three things
    // decide whether anything is said at all, and each of them is a different question: whether this head offered the
    // operation, whether this machine was left raising them, and whether somebody is already looking at the window —
    // a notification raised over a window somebody is reading is the client interrupting itself. None of the three is
    // a question about which head this is: a desktop shell and a browser both answer the first, and everything below
    // reads the same way in either.
    //
    // `document.hasFocus()` rather than `visibilityState`, and the difference is the case this exists for: a desktop
    // window standing behind another is visible and unfocused, which is exactly when nobody is looking at it.
    //
    // What is said is one sentence carrying how many arrived and of what kind, and never anything a message held —
    // `arrivalCounts.ts` is where that is enforced and where it is proven. `Intl.ListFormat` is what joins two kinds
    // into one sentence, so a language decides the conjunction rather than this line.
    const raiseWithTheSystem = useCallback(
        (arrived: readonly ClientNotification[]): void => {
            if (!notifier.offered || arrived.length === 0 || document.hasFocus() || !systemNotificationsChosen()) {
                return;
            }

            const counted = new Intl.PluralRules(locale);
            const said = arrivalCounts(arrived).map(({ kind, count }) =>
                translate(systemNotificationCounts[kind][counted.select(count)], {
                    count: new Intl.NumberFormat(locale).format(count),
                }),
            );

            void notifier.raise(new Intl.ListFormat(locale, { type: 'conjunction' }).format(said)).then((raised) => {
                // A refusal is permanent rather than something to ask about again: the operating system has answered,
                // and a client that kept asking would put its dialog in front of somebody once per arrival. Written on
                // the device, so the switch reads what the machine decided and the next start honours it.
                //
                // Only a refusal. An operation this head does not carry answers `unavailable`, which nobody decided —
                // writing *off* for that would leave somebody a switch they have to find and undo on a machine that
                // never asked them anything.
                if (raised === 'refused') {
                    chooseSystemNotifications(false);
                }
            });
        },
        [notifier, locale, translate],
    );

    // Held steady so the page effect below does not restart on every render, and so the toast an arrival raises can
    // open the notification it is about.
    const announce = useCallback(
        (arrived: readonly ClientNotification[]): void => {
            const spoken = arrived.filter((notification) => {
                const failure = failureOf(notification);

                return failure?.kind !== 'change' || !followedHere(failure.recordId);
            });

            raiseWithTheSystem(spoken);

            for (const notification of spoken.slice(0, mostArrivalsAnnounced).reverse()) {
                const said = wordNotificationToast(notification, accounts, locale, translate);

                toasts.raise({
                    kind: toastKindOf(notification),
                    title: said.title,
                    body: said.body,
                    action: {
                        label: said.action,
                        take: () => {
                            follow(notification);
                        },
                    },
                });
            }
        },
        [toasts, accounts, followedHere, locale, translate, follow, raiseWithTheSystem],
    );

    // What an arrival is said with is held rather than depended on. The read below must be decided by the credential,
    // the panel, and what the count said — never by a caller that rebuilt its own callback while rendering, which
    // would otherwise read the whole page again on every render of the frame.
    const announcing = useRef(announce);

    useEffect(() => {
        announcing.current = announce;
    }, [announce]);

    useEffect(() => {
        if (session === null || !online || (!shown && asked === 0)) {
            return;
        }

        const attempted = new AbortController();

        // Taken by the read it was set for, so a read the panel opening or an arrival asks for afterwards is not quiet.
        const keepsWhatStands = quietly.current;
        quietly.current = false;

        // Spent only by a read that lands: one refused or superseded leaves it for the read after it.
        const taken = risen.current?.session === session ? risen.current : null;
        const risenBy = Math.max(taken?.measured ?? 0, taken?.raised ?? 0);

        setReading(known.current?.session !== session);

        void (async () => {
            const answer = await readNotifications(session, transport, notificationsShown);

            if (attempted.signal.aborted) {
                return;
            }

            setReading(false);

            if (answer.outcome === 'failed') {
                if (!keepsWhatStands) {
                    setFailure(answer.failure.reason);
                }

                return;
            }

            const page = answer.value.notifications;
            const decidedHere = decided.current?.session === session ? decided.current.ids : null;
            const answered =
                decidedHere === null
                    ? []
                    : page.filter((notification) => {
                          const failure = failureOf(notification);

                          return !notification.read && failure?.kind === 'change' && decidedHere.has(failure.recordId);
                      });
            const seen = known.current?.session === session ? known.current.ids : null;
            const standing = risen.current?.session === session ? risen.current : null;
            const stillMeasured = (standing?.measured ?? 0) - (taken?.measured ?? 0);
            const stillRaised = (standing?.raised ?? 0) - (taken?.raised ?? 0);

            risen.current =
                stillMeasured > 0 || stillRaised > 0
                    ? { session, measured: Math.max(0, stillMeasured), raised: Math.max(0, stillRaised) }
                    : null;

            // What arrived is what the last page did not hold. Where no page has been drawn yet, the count is the only
            // thing this client measured before, and it rose by exactly the rows that arrived — which a centre read
            // newest first holds at its head among the unread. A first read nothing rose for is what this client has
            // rather than what just happened, so nothing is announced from it: a person opening the client is not
            // being told about seven things arriving at once.
            const unseen =
                seen === null
                    ? page.filter((notification) => !notification.read).slice(0, risenBy)
                    : page.filter((notification) => !seen.has(notification.id));

            setFailure(null);
            setNotifications(
                answered.length === 0
                    ? page
                    : page.map((notification) =>
                          answered.includes(notification) ? { ...notification, read: true } : notification,
                      ),
            );
            setArrived(new Set(unseen.map((notification) => notification.id)));
            known.current = { session, ids: new Set(page.map((notification) => notification.id)) };
            announcing.current(unseen.filter((notification) => !notification.read && !answered.includes(notification)));

            if (answered.length > 0) {
                setUnreadCount((standing) => Math.max(0, standing - answered.length));

                for (const notification of answered) {
                    void setNotificationRead(session, transport, notification.id, true);
                }
            }
        })();

        return () => {
            attempted.abort();
        };
    }, [session, transport, online, shown, asked]);

    function markRead(ids: readonly string[], read: boolean): void {
        if (session === null || ids.length === 0) {
            return;
        }

        const changed = notifications.filter((row) => ids.includes(row.id) && row.read !== read);

        if (changed.length === 0) {
            return;
        }

        setNotifications((held) => held.map((row) => (ids.includes(row.id) ? { ...row, read } : row)));
        setUnreadCount((standing) => Math.max(0, standing + (read ? -changed.length : changed.length)));

        for (const row of changed) {
            void setNotificationRead(session, transport, row.id, read).then((answer) => {
                if (inForce.current !== session) {
                    return;
                }

                if (answer.outcome === 'read') {
                    counted.current = { session, count: answer.value.unreadCount };
                    setUnreadCount(answer.value.unreadCount);

                    return;
                }

                // Taken back rather than left on the screen as something that happened: the deployment still holds the
                // state it held, and a row drawn against a write that failed is the one thing worse than the failure.
                setNotifications((held) =>
                    held.map((held2) => (held2.id === row.id ? { ...held2, read: row.read } : held2)),
                );
                setUnreadCount((standing) => Math.max(0, standing + (read ? 1 : -1)));
                toasts.raise({ kind: 'error', title: translate('notifications.readStateNotChanged') });
            });
        }
    }

    function markAllRead(): void {
        if (session === null || unreadCount === 0) {
            return;
        }

        const held = notifications;

        setNotifications((rows) => rows.map((row) => ({ ...row, read: true })));
        setUnreadCount(0);

        void markAllNotificationsRead(session, transport).then((answer) => {
            if (inForce.current !== session) {
                return;
            }

            if (answer.outcome === 'read') {
                counted.current = { session, count: answer.value.unreadCount };
                setUnreadCount(answer.value.unreadCount);

                return;
            }

            setNotifications(held);
            setUnreadCount(held.filter((row) => !row.read).length);
            toasts.raise({ kind: 'error', title: translate('notifications.notAllMarkedRead') });
        });
    }

    // Removing rows is one request rather than one per row, which is what separates it from marking read above: a
    // deployment that erased some of what was named answers with the count it erased, and there is no per-row state
    // left to put back — a row that went is gone from the centre either way.
    function remove(ids: readonly string[]): void {
        if (session === null || ids.length === 0) {
            return;
        }

        const held = notifications;
        const going = held.filter((row) => ids.includes(row.id));

        if (going.length === 0) {
            return;
        }

        // The count moves by what these rows were worth rather than being recomputed from the page, because the bell
        // counts every notification the deployment holds and a page is only as much of the centre as was read.
        const goingUnread = going.filter((row) => !row.read).length;

        setNotifications((rows) => rows.filter((row) => !ids.includes(row.id)));
        setUnreadCount((standing) => Math.max(0, standing - goingUnread));

        // What has already been announced is left alone deliberately. An erased row never comes back in a page, so the
        // identifier costs nothing to keep; forgetting it would mean a delete that failed and put the rows back
        // announced them a second time as though they had just arrived.
        void deleteNotifications(session, transport, ids).then((answer) => {
            if (inForce.current !== session) {
                return;
            }

            if (answer.outcome === 'read') {
                counted.current = { session, count: answer.value.unreadCount };
                setUnreadCount(answer.value.unreadCount);

                return;
            }

            setNotifications(held);
            setUnreadCount((standing) => standing + goingUnread);
            toasts.raise({ kind: 'error', title: translate('notifications.notDeleted') });
        });
    }

    function changeDecided(recordId: string): void {
        if (session === null) {
            return;
        }

        const standing = decided.current?.session === session ? decided.current.ids : new Set<string>();

        decided.current = { session, ids: new Set([...standing, recordId]) };
        markRead(
            notifications
                .filter((notification) => {
                    const failure = failureOf(notification);

                    return failure?.kind === 'change' && failure.recordId === recordId;
                })
                .map((notification) => notification.id),
            true,
        );
    }

    return {
        unreadCount,
        shown,
        notifications,
        arrived,
        reading,
        failure,
        show,
        hide,
        markRead,
        markAllRead,
        remove,
        follow,
        changeDecided,
    };
}

const noAccounts: readonly MailAccount[] = [];

function followsNothing(): boolean {
    return false;
}
