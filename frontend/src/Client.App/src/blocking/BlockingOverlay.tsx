// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { StopConfirmation } from './StopConfirmation';
import type { BlockingOperation } from './useBlocking';

// The one surface that covers the whole client, for the one class of operation a stray press behind it must not
// interrupt. Everything else the client says about what it is doing says it beside the work and leaves the application
// live; this says it in front of everything and takes the application away, which is a cost paid only where being
// interrupted halfway would leave data half-written.
//
// It is the platform's own modal dialog, so five things are the browser's rather than this file's: the top layer, the
// inertness of the page behind it, the focus moving into it, the focus trapped there while it is open, and the focus
// returning to wherever it came from when it closes. That is why the surface is a `dialog` opened with `showModal`
// rather than a fixed element with a scrim drawn under it — the scrim is what a reader sees of it, and the four
// invisible parts are what an operation actually needs.
//
// Two of the platform's own behaviours are deliberately refused. **Escape does not close it**, because dismissing this
// surface is stopping the operation and that goes through the control and its question; the `cancel` event is where a
// close request arrives and defaulting it away is how a dialog declines one. **A press beside it does nothing**, which
// is the platform's own default for a modal dialog rather than something written here — the scrim is the `::backdrop`
// of this element, so a press on it lands on no control at all. Nothing in this file listens for one, and that absence
// is the behaviour.

/**
 * How far along the operation says it is, as the whole number of percent both the bar's own value and its width are
 * stated in.
 *
 * Held inside the range the bar declares, because an operation counting its own work can report more than all of it —
 * a migration told to move three thousand messages that finds three thousand and four — and a bar drawn past its track
 * or a value announced above its own maximum is a screen saying something that is not true rather than a rounding
 * error. One clamp and one rounding, here, so the words and the bar can never disagree about the same operation.
 */
function percent(progress: number): number {
    return Math.min(100, Math.max(0, Math.round(progress * 100)));
}

/** The same reading as the reader's own locale writes a share of one. */
function reading(percentage: number, locale: string): string {
    return new Intl.NumberFormat(locale, { style: 'percent' }).format(percentage / 100);
}

export function BlockingOverlay({
    operation,
}: {
    /** What the client is blocked on, or `null` where it is not blocked and this surface draws nothing. */
    readonly operation: BlockingOperation | null;
}) {
    const { locale, translate } = useLocalization();
    const surface = useRef<HTMLDialogElement>(null);
    const named = useId();
    const blocked = operation !== null;

    // The operation last drawn, kept once it has stopped so the card fades out with what it said rather than as an
    // empty box: the dialog plays its departure after `close()`, and its content would otherwise be gone already.
    const [shown, setShown] = useState(operation);

    if (operation !== null && operation !== shown) {
        setShown(operation);
    }

    // An effect, because opening a modal dialog is an imperative browser API and that is the whole of what this is for.
    // The `open` attribute is not the same thing: it draws a dialog without the top layer, the inertness, the focus
    // trap, or the backdrop, which are the four reasons this surface is a dialog at all.
    useEffect(() => {
        const dialog = surface.current;

        if (dialog === null) {
            return;
        }

        if (blocked && !dialog.open) {
            dialog.showModal();
        } else if (!blocked && dialog.open) {
            // The question about stopping stays drawn with the rest of the card while it leaves, so a question still
            // asked is put away here: left open, it would hold the page inert behind a card nobody can see.
            for (const asked of dialog.querySelectorAll('dialog')) {
                asked.close();
            }

            dialog.close();
        }
    }, [blocked]);

    return (
        <dialog
            ref={surface}
            aria-labelledby={named}
            className="surface-pop m-auto w-105 max-w-full rounded-xl border border-line bg-panel px-7.5 pt-8 pb-6 text-text shadow-dialog backdrop:bg-scrim backdrop:backdrop-blur-xs"
            onCancel={(event) => {
                // A close request — Escape, and whatever else a platform decides is one. Leaving this surface is
                // stopping the operation, so it happens through the control below and the question in front of it.
                event.preventDefault();
            }}
        >
            {shown === null ? null : (
                <div className="flex flex-col items-center gap-3.5">
                    {/* The animation is removed under `prefers-reduced-motion` by the one rule in `styles.css` that
                    removes every animation, rather than by anything stated here: motion is decided once, and a
                    surface opting out of it in its own way is the same drift as one writing a colour. What is left
                    without the movement is the symbol, the bar, and the words, which are what actually say the
                    operation is running. */}
                    <Icon name="progress_activity" className="size-icon-4xl animate-spin text-accent-strong" />

                    <h2 id={named} className="text-2xl font-semibold text-balance">
                        {shown.title}
                    </h2>

                    <p className="text-md text-text-soft text-pretty">{shown.explanation}</p>

                    {/* The one place in the client where ARIA stands in for an element the platform has, and it is a
                    measurement rather than a preference: `progress` was drawn first, the way `readingPane/Attachment.tsx`
                    draws a download, and its indeterminate state renders as a flat line in the engine both heads run
                    on — a bar that says nothing is happening, which is the opposite of what this variant exists to
                    report. What the platform draws for the determinate state is its own colour and its own square
                    ends, and neither is reachable without styling parts only some engines publish. So the bar is drawn
                    from tokens and carries the role the platform element would have carried, with no reading at all
                    where the operation has none — which is how ARIA states an indeterminate progress bar. */}
                    <div
                        role="progressbar"
                        aria-label={translate('blocking.progress')}
                        aria-valuemin={0}
                        aria-valuemax={100}
                        aria-valuenow={shown.progress === undefined ? undefined : percent(shown.progress)}
                        className="mt-0.5 h-1.5 w-full overflow-hidden rounded-full bg-hover"
                    >
                        {shown.progress === undefined ? (
                            <div className="h-full rounded-full bg-accent progress-unknown" />
                        ) : (
                            <div
                                className="h-full rounded-full bg-accent transition-all"
                                style={{ width: `${String(percent(shown.progress))}%` }}
                            />
                        )}
                    </div>

                    <p className="text-sm text-muted">
                        {shown.progress === undefined
                            ? translate('blocking.noKnownFinish')
                            : translate('blocking.progressReading', {
                                  percentage: reading(percent(shown.progress), locale),
                              })}
                    </p>

                    <StopConfirmation leavesBehind={shown.stoppingLeavesBehind} onStop={shown.stop} />

                    <p className="text-xs text-faint text-pretty">{translate('blocking.doNotClose')}</p>
                </div>
            )}
        </dialog>
    );
}
