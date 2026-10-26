// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useRef, type PointerEvent } from 'react';
import type { ClientNotification } from '@mailfathom/client-backend';
import type { MenuPoint } from '../contextMenu/menuPlacement';
import { pressedByFinger, useRowPress } from '../contextMenu/rowPress';
import { Icon } from '../controls/Icon';
import { useRowSwipe } from '../controls/rowSwipe';
import { useTip } from '../controls/useTip';
import { wordInstant } from '../localization/instants';
import { useLocalization } from '../localization/useLocalization';
import { useReadingZone } from '../localization/useReadingZone';
import { wordNotificationAge } from './notificationAge';
import type { FailureDecision } from './failures';
import { failureDecisionShapes, notificationKindLabels, toneOf } from './notificationKinds';
import { wordNotification } from './notificationWords';

// One thing that happened, as the design draws it: the kind's own symbol and tint, the headline — heavier and
// darker while it is unread — how long ago it was, what it says, where it came from, the unread mark, and the control
// that changes the read state without opening anything.
//
// **Two controls rather than one row that is a control.** What the row is about and what the toggle does are different
// acts, so each is a button with a name of its own — which is what makes both reachable from a keyboard and both
// assertable by name. A row is never a button with a button inside it.
//
// **A merged arrival carries a third control, *Summarize*, under what it says.** It is a button beside the row's own
// rather than inside it, for the reason the toggle is: it hands the messages the row stands for to the agent, which is
// a different act from opening the mail list the row leads to.
//
// **A failure carries its decisions the same way**, as pills under the source line at every width so a finger reaches
// them: a refused send is sent again or taken back to edit, and a change the mailbox did not take is asked again or let
// go. Each is a decision taken where the row stands rather than a way into the thing it is about.
//
// **A row of its own, swiped aside to change its read state.** Each notification is raised off the list the way a
// message row is, so one ends where the next begins. A finger carried either way across it toggles the read state,
// exactly as the toggle does — the toggle and the row's menu stay, because a swipe is a second route for a finger and
// never the only one.
//
// **How long ago is drawn and when is read.** The relative wording is what somebody scanning the list needs; the
// instant behind it is what the `time` element carries and what a pointer resting on it says, so nothing about the
// row's own wording has to be precise enough to act on.

/** One decision a row offers, and what taking it does. */
export interface RowDecision {
    readonly decision: FailureDecision;
    readonly take: () => void;

    /** Whether it is already being taken, which draws it as unavailable until it is. */
    readonly underWay?: boolean;
}

// The pill a row's own acts are drawn as, which is the design's Summarize shape for every one of them.
const pill =
    'flex h-7.5 cursor-pointer items-center gap-1.75 rounded-field border border-accent-line bg-accent-soft px-2.75 text-base font-semibold text-accent-deep transition hover:bg-accent-line pointer-coarse:px-3.5 pointer-coarse:text-md';

export function NotificationRow({
    notification,
    selected,
    selecting,
    arrived,
    now,
    onOpen,
    onSelect,
    onToggleRead,
    onSummarize,
    decisions,
    source,
    onPress,
}: {
    readonly notification: ClientNotification;

    readonly selected: boolean;

    /** Whether a selection is being held, which is what makes a plain press pick this row out rather than open it. */
    readonly selecting: boolean;

    /**
     * Whether this row arrived while the reader had the centre in front of them, which is what it opens itself for.
     * It is false for every row the client already had when it first read the centre: somebody opening the client is
     * not watching a hundred things arrive, they are being shown what is already there.
     */
    readonly arrived: boolean;

    /** What the current instant is, which is what the age is measured from and what a test pins. */
    readonly now: number;

    readonly onOpen: () => void;
    readonly onSelect: () => void;
    readonly onToggleRead: () => void;

    /** Hands the messages a merged arrival stands for to the agent, or `null` on every row that stands for none. */
    readonly onSummarize: (() => void) | null;

    /** What may be decided about the failure this row reports, and none on every other row. */
    readonly decisions: readonly RowDecision[];

    /** Where it came from, as the reader names it, or `null` where the kind is the whole of it. */
    readonly source: string | null;

    /** Opens this row's menu at the point the gesture happened. */
    readonly onPress: (at: MenuPoint) => void;
}) {
    const { locale, translate } = useLocalization();
    const timeZone = useReadingZone();
    const press = useRowPress(onPress);
    const swipe = useRowSwipe(press, { leftward: onToggleRead, rightward: onToggleRead });
    const primary = useRef<HTMLButtonElement>(null);
    const tone = toneOf(notification);
    const tucked = onSummarize === null && decisions.length === 0;
    const age = wordNotificationAge(notification.occurredAt, locale, now);
    const at = wordInstant(notification.occurredAt, locale, 'full', timeZone);
    const said = wordNotification(notification, locale, translate);
    // The exact instant behind the relative one, for a mouse resting on it; a finger finds it in the row's menu, since
    // a tap on the row already opens what it is about.
    const instantSaid = useTip({ says: at ?? '', tapReveals: false });
    const markName = useTip({
        says: translate(notification.read ? 'notifications.markUnread' : 'notifications.markRead'),
        onPress: onToggleRead,
    });
    const summarizeName = translate('notifications.summarize');

    // A modifier held under a pointer is what picks a row out where there is no selection yet, which is the one
    // gesture a finger has no equivalent of — the row's own menu is how it reaches the same thing.
    function pointed(event: PointerEvent<HTMLButtonElement>): void {
        if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
            onSelect();

            return;
        }

        onOpen();
    }

    return (
        <li
            className={`relative shrink-0 touch-pan-y overflow-hidden shadow-raised ${
                arrived ? 'animate-row-opening' : ''
            }`}
            // The swipe belongs to the whole row, wherever the finger lands on it — the toggle, a pill, the padding — so
            // it is bound here rather than on the button the press is. In the capture phase, because the lift that
            // finishes a swipe has to be decided before the button reads that same lift as a tap.
            onPointerDownCapture={swipe.onPointerDown}
            onPointerMoveCapture={swipe.onPointerMove}
            onPointerUpCapture={swipe.onPointerUp}
            onPointerCancelCapture={swipe.onPointerCancel}
        >
            {swipe.carried === 0 ? null : (
                // What lifting the finger will do, against the edge the row has uncovered. Hidden from the
                // accessibility tree because the same act is the toggle beside it, which is where a keyboard and a
                // screen reader meet it; faint until the swipe has gone far enough and full once it has.
                <span
                    aria-hidden="true"
                    className={`absolute inset-0 flex items-center bg-accent-soft text-accent-strong ${
                        swipe.carried < 0 ? 'justify-end pe-5.5' : 'justify-start ps-5.5'
                    } ${swipe.commits === null ? 'opacity-55' : 'opacity-100'}`}
                >
                    <Icon
                        name={notification.read ? 'mark_email_unread' : 'mark_email_read'}
                        className="me-2 size-icon-xl"
                    />

                    <span className="text-base font-semibold">
                        {translate(
                            notification.read ? 'notifications.swipeMarksUnread' : 'notifications.swipeMarksRead',
                        )}
                    </span>
                </span>
            )}

            <div
                className={`relative flex items-start gap-3 ps-4 pe-3 ${swipe.carried === 0 ? 'transition' : ''} ${
                    selected ? 'bg-accent-soft' : 'bg-panel hover:bg-hover'
                }`}
                // How far a finger has carried the row is a distance it decided rather than one the theme took.
                style={swipe.carried === 0 ? undefined : { transform: `translateX(${String(swipe.carried)}px)` }}
            >
                <span className="flex min-w-0 flex-1 flex-col">
                    <button
                        ref={primary}
                        type="button"
                        aria-pressed={selecting ? selected : undefined}
                        className={`flex cursor-pointer items-start gap-3 pt-3.25 text-start ${tucked ? 'pb-3.25' : 'pb-1.5'}`}
                        onContextMenu={press.onContextMenu}
                        onPointerDown={(event) => {
                            press.onPointerDown(event);

                            // A mouse acts as it goes down; a finger's press is not decided until it is lifted, because the
                            // same touch may become the press that opens this row's menu.
                            if (!pressedByFinger(event.pointerType) && event.button === 0) {
                                pointed(event);
                            }
                        }}
                        onPointerMove={press.onPointerMove}
                        onPointerUp={(event) => {
                            const tapped = pressedByFinger(event.pointerType) && !press.tapSuppressed();

                            press.onPointerUp();

                            // A lift that has just changed the read state is not also a tap opening what the row is about.
                            if (tapped && !swipe.tapSuppressed()) {
                                pointed(event);
                            }
                        }}
                        onPointerCancel={press.onPointerCancel}
                        // The keyboard reaches the row through its own activation rather than through a pointer, so the two
                        // are separate paths to the same two acts and neither is a simulation of the other.
                        onKeyDown={(event) => {
                            if (event.key === 'Enter' || event.key === ' ') {
                                event.preventDefault();

                                if (event.ctrlKey || event.metaKey || event.shiftKey || selecting) {
                                    onSelect();
                                } else {
                                    onOpen();
                                }
                            }
                        }}
                    >
                        <span
                            aria-hidden="true"
                            className={`flex size-8 shrink-0 items-center justify-center rounded-lg ${tone.mark}`}
                        >
                            <Icon name={tone.icon} className="size-icon-lg" />
                        </span>

                        <span className="flex min-w-0 flex-1 flex-col gap-0.75">
                            <span className="flex items-baseline gap-2.25">
                                {/* Unread is weight and colour, which is how the design draws it, and a word for a
                            reader who is looking at neither. */}
                                <span
                                    className={`min-w-0 flex-1 text-md text-pretty ${
                                        notification.read ? 'font-medium text-text-soft' : 'font-semibold text-text'
                                    }`}
                                >
                                    {said.title}
                                </span>

                                <time
                                    {...(age === null || at === null ? {} : instantSaid.anchor)}
                                    dateTime={notification.occurredAt}
                                    className="shrink-0 text-xs whitespace-nowrap text-faint"
                                >
                                    {age ?? at}
                                </time>
                                {instantSaid.tip}
                            </span>

                            <span className="text-base text-pretty text-text-soft">{said.body}</span>

                            {/* The kind is what the source line falls back to, because a row says where it came from either
                        way — and the kind is the one thing every notification has. */}
                            <span className="pt-0.25 text-xs text-muted">
                                {source ?? translate(notificationKindLabels[notification.kind])}
                            </span>
                        </span>

                        {notification.read ? null : (
                            <span className="sr-only">{translate('notifications.unreadMark')}</span>
                        )}
                    </button>

                    {/* Under the text rather than under the symbol, which is where the design hangs it: inset by the
                symbol's width and the gap beside it. */}
                    {onSummarize === null ? null : (
                        <button type="button" onClick={onSummarize} className={`ms-11 mb-3.25 self-start ${pill}`}>
                            <Icon name="auto_awesome" className="size-icon-md" />
                            {summarizeName}
                        </button>
                    )}

                    {decisions.length === 0 ? null : (
                        <span
                            role="group"
                            aria-label={translate('notifications.decisions')}
                            className="ms-11 mb-3.25 flex flex-wrap gap-2"
                        >
                            {/* A decision takes its pills away, so focus goes to the row's own control first rather than
                            falling to the document with the pill that held it. */}
                            {decisions.map(({ decision, take, underWay }) => (
                                <button
                                    key={decision}
                                    type="button"
                                    aria-disabled={underWay === true ? true : undefined}
                                    onClick={() => {
                                        if (underWay === true) {
                                            return;
                                        }

                                        primary.current?.focus();
                                        take();
                                    }}
                                    className={pill}
                                >
                                    <Icon name={failureDecisionShapes[decision].icon} className="size-icon-md" />
                                    {translate(failureDecisionShapes[decision].label)}
                                </button>
                            ))}
                        </span>
                    )}
                </span>

                <span className="flex shrink-0 items-center gap-1.75 py-3.25">
                    {notification.read ? null : (
                        <span aria-hidden="true" className="size-2 shrink-0 rounded-full bg-accent" />
                    )}

                    <button
                        {...markName.anchor}
                        type="button"
                        aria-label={translate(
                            notification.read ? 'notifications.markUnread' : 'notifications.markRead',
                        )}
                        className={`flex size-8 cursor-pointer items-center justify-center rounded-lg border transition pointer-coarse:size-11 ${
                            notification.read
                                ? 'border-line bg-panel text-muted'
                                : 'border-accent-line bg-accent-soft text-accent-deep'
                        }`}
                    >
                        <Icon
                            name={notification.read ? 'mark_email_unread' : 'mark_email_read'}
                            className="size-icon-lg"
                        />
                    </button>
                    {markName.tip}
                </span>
            </div>
        </li>
    );
}
