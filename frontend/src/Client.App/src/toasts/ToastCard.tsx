// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useRef, useState, type PointerEvent } from 'react';
import { Confirmation } from '../confirmation/Confirmation';
import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import type { IconName } from '../controls/icons';
import { swipeCarriesTo, swipeCarry, swipeSoFar } from '../controls/swipeAcross';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { toastLeaving, type StandingToast, type ToastAction, type ToastKind } from './useToasts';

// One toast as it stands on the screen: what happened, what it was about, at most one thing to do about it, and the
// close control that is on every card in every composition. The bar along the bottom edge is the lifetime running out,
// which is why the toast that is still following an operation has none — nothing is running out for it.
//
// A toast that stands for something that can be opened — a notification raised while the client is open — is that
// thing on the screen, so the card itself is what is pressed to open it and it draws no button. Its words are one
// button stretched over the whole card, with the close control standing above it, so a press anywhere but the close
// control opens and a press on the close control only closes.
//
// Two ways to get rid of one, and both are here because both have to be. The close control is the design's and
// it never gives way to the gesture: a card whose only dismissal is a drag is a card a keyboard cannot reach. The drag
// is beside it for every pointer, a mouse included — the close control is always there, so nothing is reachable only by
// the gesture. The card follows the pointer across, and it answers to the threshold and the vertical cancellation the
// rest of the client's swipes answer to, stated once in `controls/swipeAcross.ts`. The pointer is captured only once
// the card is moving, so a press that lands on the close control is still the close control's.
//
// Neither way is quieter than the other. Where the card is following an operation, both ask the same question and stop
// it only on the same answer — a gesture that aborted something a button would have asked about would be the fastest
// way in the client to lose work by accident.

/** What each kind is drawn as: its symbol, the tint behind it, the bar it runs out on, and what it is called. */
interface ToastMark {
    readonly icon: IconName;
    readonly tint: string;
    readonly bar: string;
    readonly said: MessageKey;
}

// A lookup declared once rather than a chain inside the markup, exhaustive by its own type, so the six kinds the
// design draws are one table a reader sees the whole of. Colour reaches the symbol and the bar and nothing
// else: a card that took the colour of what it says would be six cards rather than one surface.
const toastMarks: Readonly<Record<ToastKind | 'running', ToastMark>> = {
    neutral: { icon: 'info', tint: 'bg-hover text-text-soft', bar: 'bg-line-strong', said: 'toast.neutral' },
    success: {
        icon: 'check_circle',
        tint: 'bg-healthy-soft text-healthy-text',
        bar: 'bg-healthy',
        said: 'toast.success',
    },
    error: { icon: 'error', tint: 'bg-error-soft text-error-text', bar: 'bg-error', said: 'toast.error' },
    warning: {
        icon: 'warning',
        tint: 'bg-warning-soft text-warning-text',
        bar: 'bg-warning',
        said: 'toast.warning',
    },
    info: { icon: 'campaign', tint: 'bg-accent-soft text-accent-strong', bar: 'bg-accent', said: 'toast.info' },
    running: {
        icon: 'progress_activity',
        tint: 'bg-accent-soft text-accent-strong',
        bar: 'bg-accent',
        said: 'toast.running',
    },
};

export function ToastCard({
    toast,
    onDismiss,
    onStop,
}: {
    readonly toast: StandingToast;

    /** Takes the card away, which is what closing one that is not following an operation means. */
    readonly onDismiss: () => void;

    /** Stops the operation the card is following, once somebody has confirmed that is what they meant. */
    readonly onStop: () => void;
}) {
    const { translate } = useLocalization();
    const asked = useRef<HTMLDialogElement>(null);

    // The gesture in flight: which pointer started it, where it landed, and whether the card has started following it.
    // Where the card is drawn is state, because it is on the screen; the rest is not.
    const swiping = useRef<{ pointer: number; from: number; top: number; engaged: boolean } | null>(null);
    const [carried, setCarried] = useState(0);
    const [following, setFollowing] = useState(false);
    const [flung, setFlung] = useState<-1 | 1 | null>(null);

    // Whether the last pointer gesture on the card travelled, so the press its release ends is not also taken as a
    // press on the card. A key pressing the card has travelled nowhere, which is what `detail` being `0` says.
    const travelled = useRef(false);

    // What stopping would leave behind, kept here from the moment the question is asked rather than read off the toast
    // while it stands: the operation goes on running while somebody decides, so it may settle mid-question and take
    // `stands` with it, and the question would then be drawn from something that has already changed.
    const [leavesBehind, setLeavesBehind] = useState('');

    // Bound once rather than read inside the handler: the handler runs after this render, and a property read there
    // is no longer the one the markup decided to draw a control for.
    const action = toast.action;
    const opens = toast.opens;
    const running = 'operation' in toast.stands;
    const mark = running ? toastMarks.running : toastMarks[toast.stands.kind];
    const closing = running ? translate('toast.stopOperation') : translate('toast.close');
    const closerName = useTip({
        says: closing,
        onPress: () => {
            close();
        },
    });

    // An operation that settles while the question stands closes it through the dialog's own path, which is what puts
    // focus back on the control that opened it. Taking the element away instead would drop the focus `showModal`
    // trapped inside it, and the answer is worth nothing by then anyway — there is nothing left to stop.
    useEffect(() => {
        if (!running) {
            asked.current?.close();
        }
    }, [running]);

    function close(): void {
        if ('operation' in toast.stands) {
            setLeavesBehind(toast.stands.operation.stoppingLeavesBehind);
            asked.current?.showModal();
        } else {
            onDismiss();
        }
    }

    function take(action: ToastAction): void {
        onDismiss();
        action.take();
    }

    function open(opening: ToastAction, pressedBy: number): void {
        if (travelled.current && pressedBy !== 0) {
            return;
        }

        take(opening);
    }

    function springBack(): void {
        swiping.current = null;
        setFollowing(false);
        setCarried(0);
    }

    function beginSwipe(event: PointerEvent<HTMLDivElement>): void {
        // Only the primary button: a right-click opens what a right-click opens, never a drag.
        if (swiping.current !== null || event.button > 0 || flung !== null) {
            return;
        }

        travelled.current = false;
        swiping.current = { pointer: event.pointerId, from: event.clientX, top: event.clientY, engaged: false };
    }

    function followSwipe(event: PointerEvent<HTMLDivElement>): void {
        const swipe = swiping.current;

        if (swipe?.pointer !== event.pointerId) {
            return;
        }

        const across = event.clientX - swipe.from;

        if (swipeSoFar(across, event.clientY - swipe.top) === 'cancelled') {
            travelled.current = true;
            springBack();

            return;
        }

        const carry = swipeCarry(across);

        if (carry === 0) {
            return;
        }

        if (!swipe.engaged) {
            swipe.engaged = true;
            travelled.current = true;
            setFollowing(true);

            try {
                event.currentTarget.setPointerCapture(event.pointerId);
            } catch {
                // A runtime not tracking this pointer refuses to hand its capture over, which is what a synthesised
                // event is. The gesture then follows the handlers on the card instead, which is everything but the part
                // where it keeps following a pointer that has left it.
            }
        }

        setCarried(carry);
    }

    function endSwipe(event: PointerEvent<HTMLDivElement>): void {
        const swipe = swiping.current;

        if (swipe?.pointer !== event.pointerId) {
            return;
        }

        const across = event.clientX - swipe.from;
        const committed = swipe.engaged && swipeSoFar(across, event.clientY - swipe.top) === 'committed';

        springBack();

        if (!committed) {
            return;
        }

        // A card following an operation asks first, exactly as its close control does, so it comes back to be asked
        // about; any other leaves the way it was carried.
        if (!running) {
            setFlung(across < 0 ? -1 : 1);
        }

        close();
    }

    function abandonSwipe(): void {
        if (swiping.current !== null) {
            springBack();
        }
    }

    // A pointer that leaves the card before the card has started following it is not captured, so its release lands
    // somewhere else and would leave the gesture standing for the next pointer that merely passes over the card. A
    // gesture already engaged holds the capture and never leaves.
    function leaveSwipe(): void {
        if (swiping.current?.engaged === false) {
            swiping.current = null;
        }
    }

    // How far the card is drawn from where it stands, and how faint, while a pointer carries it and once it has been
    // carried far enough to go. `translate` rather than `transform`, so the leaving animation, which moves the card by
    // `transform`, composes with it instead of throwing it back to where it started.
    const carriedTo = flung === null ? (carried === 0 ? undefined : `${String(carried)}px`) : `${String(flung * 110)}%`;
    const carriedOpacity =
        flung === null ? (carried === 0 ? undefined : 1 - (Math.abs(carried) / swipeCarriesTo) * 0.6) : 0;

    const words = (
        <>
            <span className="block text-md/snug font-semibold text-text text-pretty wide:text-lg">
                {/* What the symbol and its colour say to everybody else. Said before the title rather than after it, so
                    somebody hearing the card knows what kind of news it is before they hear the news. */}
                <span className="sr-only">{translate(mark.said)}</span> {toast.title}
            </span>

            {/* Spaces between the lines, so the name a card pressed as a whole is given reads as words rather than
                as one run of text. */}
            {toast.body === undefined ? null : ' '}
            {toast.body === undefined ? null : (
                <span className="line-clamp-2 block text-base/snug text-text-soft text-pretty wide:line-clamp-none wide:text-base">
                    {toast.body}
                </span>
            )}
        </>
    );

    return (
        <div
            // An error is the one thing said here that cannot wait for a gap in what a screen reader is already
            // reading, so it announces where it stands rather than politely with the rest of the surface.
            role={!running && toast.stands.kind === 'error' ? 'alert' : undefined}
            className={`pointer-events-auto relative flex touch-pan-y items-center select-none gap-2.5 overflow-hidden rounded-lg border border-line bg-panel py-1.5 ps-2 pe-1 shadow-overlay wide:items-start wide:gap-3 wide:ps-3.25 wide:pe-3 wide:pt-3.25 wide:pb-3.75 wide:opacity-80 wide:hover:opacity-100 ${
                following ? 'transition-none' : 'motion-spring'
            } ${toast.leaving ? 'animate-toast-leaving' : 'animate-toast-arriving'}`}
            // How long leaving takes is how long the surface waits before the card is gone, which is one number in
            // `useToasts.ts` and is handed to the animation here rather than written into the stylesheet a second time.
            style={{
                animationDuration: toast.leaving ? `${String(toastLeaving)}ms` : undefined,
                translate: carriedTo,
                opacity: carriedOpacity,
            }}
            onPointerDown={beginSwipe}
            onPointerMove={followSwipe}
            onPointerUp={endSwipe}
            onPointerCancel={abandonSwipe}
            onPointerLeave={leaveSwipe}
        >
            <span
                className={`flex size-7 shrink-0 items-center justify-center rounded-md border border-line wide:size-8 ${mark.tint}`}
            >
                <Icon name={mark.icon} className={`size-icon-md wide:size-icon-xl ${running ? 'animate-spin' : ''}`} />
            </span>

            {/* On a phone or a fold the card is one slim row with its action beside the words; wider, the action
                stands under them. One control either way, moved by the layout rather than drawn twice. */}
            <div className="flex min-w-0 flex-1 items-center gap-2.5 wide:flex-col wide:items-stretch wide:gap-1">
                {opens === undefined ? (
                    <div className="flex min-w-0 flex-1 flex-col gap-px py-0.5 wide:gap-1 wide:pt-0.5 wide:pb-0">
                        {words}
                    </div>
                ) : (
                    <button
                        type="button"
                        className="flex min-w-0 flex-1 cursor-pointer flex-col gap-px py-0.5 text-start after:absolute after:inset-0 after:content-[''] wide:gap-1 wide:pt-0.5 wide:pb-0"
                        onClick={(event) => {
                            open(opens, event.detail);
                        }}
                    >
                        {words} <span className="sr-only">{opens.label}</span>
                    </button>
                )}

                {action === undefined ? null : (
                    <button
                        type="button"
                        className="min-h-9 shrink-0 self-center rounded-md border border-line-strong px-3 text-base font-semibold whitespace-nowrap text-text transition hover:bg-hover wide:mt-1.25 wide:min-h-0 wide:self-start wide:px-3.25 wide:py-1.5"
                        onClick={() => {
                            // The card goes as the action is taken, which is the design's own behaviour and
                            // the honest one: a toast still offering to undo something already undone is a control
                            // somebody presses twice.
                            take(action);
                        }}
                    >
                        {action.label}
                    </button>
                )}
            </div>

            <button
                {...closerName.anchor}
                type="button"
                aria-label={closing}
                className="relative flex size-7.5 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
            >
                <Icon name="close" className="size-icon-lg" />
            </button>
            {closerName.tip}

            {running || toast.lasting === true ? null : (
                // The lifetime, drawn. Its duration is the one the toast is actually held for rather than a second
                // copy of that number written into the stylesheet, which is why it arrives here as a value.
                <span
                    aria-hidden="true"
                    className={`absolute inset-x-0 bottom-0 h-0.5 origin-left animate-toast-lifetime opacity-55 ${mark.bar}`}
                    style={{ animationDuration: `${String(toast.standFor)}ms` }}
                />
            )}

            {!running && leavesBehind === '' ? null : (
                // The client's one confirmation, asked here as every other act asks it. It stays mounted once it has
                // been asked, even after the operation settles, because closing it is the dialog's own act and an
                // element taken away mid-question closes nothing.
                <Confirmation
                    asked={asked}
                    mark="cancel"
                    question={translate('toast.stopQuestion')}
                    consequence={<p className="text-base text-muted text-pretty">{leavesBehind}</p>}
                    reversal={{ kind: 'permanent', said: translate('toast.stopIsFinal') }}
                    ways={[
                        { said: translate('toast.keepGoing'), manner: 'back' },
                        { said: translate('toast.stopOperation'), manner: 'act', run: onStop },
                    ]}
                />
            )}
        </div>
    );
}
