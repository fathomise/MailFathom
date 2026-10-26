// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState } from 'react';
import { mailSpamSubcategories, type MailSpamSubcategory } from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import { mannerDrawn } from '../confirmation/wayOutShapes';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { useTopLayerSurface } from '../shell/screenLayers';
import { isDangerous, subcategoryHints, subcategoryLabels } from './spamWords';

// The two questions the spam control stands in front of an act, drawn once for every surface that offers the control:
// the chooser of the six subcategories, which *Spam* opens, and the confirmation *Not spam* asks only where it will move
// mail out of the Spam folder. A move into the Spam folder opens the chooser too, after the fact, to name the kind of
// spam the move already marked.
//
// **The chooser is a choice rather than a confirmation**, which is why it is not `Confirmation`: picking a kind *is* the
// act, one kind applies to every message, and closing it — its close control, Escape, the scrim, or back — changes
// nothing. The not-spam question does confirm an act, so it is the client's one confirmation like every other.

/** What the spam control is asking about, counted the way its sentences need. */
export type SpamAsking =
    | {
          readonly kind: 'spam';

          /** Distinguishes one asking from the next, so the chooser starts with nothing picked every time. */
          readonly serial: number;
          readonly messages: number;

          /** How many of them sit outside the Spam folder, which is what a mark could move. */
          readonly outside: number;

          /** How many of those are on an account whose switch moves spam. */
          readonly movers: number;

          /** Whether a move into the Spam folder already marked them, which leaves only the kind to name. */
          readonly moved: boolean;
      }
    | {
          readonly kind: 'notSpam';
          readonly serial: number;
          readonly messages: number;

          /** How many of them the mark moves from the Spam folder to the inbox. */
          readonly moving: number;
      };

function oneOrOther(locale: string, count: number): 'one' | 'other' {
    return new Intl.PluralRules(locale).select(count) === 'one' ? 'one' : 'other';
}

export function SpamQuestions({
    asking,
    onSpam,
    onNotSpam,
}: {
    readonly asking: SpamAsking | null;

    /** Marks the messages asked about spam of the kind picked, once the chooser has closed on it. */
    readonly onSpam: (subcategory: MailSpamSubcategory) => void;

    /** Marks the messages asked about not spam, once the confirmation has closed on it. */
    readonly onNotSpam: () => void;
}) {
    const choosing = useRef<HTMLDialogElement>(null);
    const confirming = useRef<HTMLDialogElement>(null);

    // The dialogs are the platform's own, so opening one is an imperative call on the element; it follows the asking
    // that a press produced, once that asking has been drawn into the dialog it opens.
    useEffect(() => {
        if (asking === null) {
            return;
        }

        const dialog = asking.kind === 'spam' ? choosing.current : confirming.current;

        if (dialog !== null && !dialog.open) {
            dialog.showModal();
        }
    }, [asking]);

    return (
        <>
            <SpamChooser
                key={asking?.kind === 'spam' ? asking.serial : 'none'}
                asked={choosing}
                asking={asking?.kind === 'spam' ? asking : null}
                onChosen={onSpam}
            />

            <NotSpamQuestion
                asked={confirming}
                asking={asking?.kind === 'notSpam' ? asking : null}
                onConfirmed={onNotSpam}
            />
        </>
    );
}

function chooserText(asking: Extract<SpamAsking, { kind: 'spam' }>, locale: string, translate: Translate): string {
    const form = oneOrOther(locale, asking.messages);
    const count = new Intl.NumberFormat(locale).format(asking.messages);

    if (asking.moved) {
        return translate(`spam.moved.${form}`);
    }

    const pick = translate(`spam.pick.${form}`, { count });

    if (asking.outside === 0) {
        return pick;
    }

    const where: string =
        asking.movers === asking.outside
            ? translate(`spam.moves.${form}`)
            : asking.movers > 0
              ? translate('spam.someMove', { count: new Intl.NumberFormat(locale).format(asking.movers) })
              : translate(`spam.stays.${form}`);

    return `${pick} ${where}`;
}

function SpamChooser({
    asked,
    asking,
    onChosen,
}: {
    readonly asked: React.RefObject<HTMLDialogElement | null>;
    readonly asking: Extract<SpamAsking, { kind: 'spam' }> | null;
    readonly onChosen: (subcategory: MailSpamSubcategory) => void;
}) {
    const { locale, translate } = useLocalization();
    const closesOnBack = useTopLayerSurface();
    const names = useId();
    const group = useId();
    const [picked, setPicked] = useState<MailSpamSubcategory | null>(null);

    const confirmLabel = translate(asking?.moved === true ? 'spam.setKind' : 'spam.confirm');

    // A confirm with nothing picked cannot act, and says why on a press rather than doing nothing silently.
    const { anchor, tip } = useTip({
        says: translate('spam.pickFirst'),
        describes: true,
        onPress: () => {
            if (picked !== null) {
                asked.current?.close(picked);
            }
        },
    });

    const title =
        asking === null
            ? ''
            : asking.moved
              ? translate('spam.chooserMovedTitle')
              : translate(`spam.chooserTitle.${oneOrOther(locale, asking.messages)}`, {
                    count: new Intl.NumberFormat(locale).format(asking.messages),
                });

    return (
        <dialog
            ref={asked}
            aria-labelledby={names}
            data-pull="down"
            onToggle={closesOnBack}
            className="max-workspace:surface-sheet workspace:surface-pop m-0 h-full max-h-full w-full max-w-full rounded-none border-0 bg-panel px-4 pt-safe-top pb-safe-bottom text-text open:flex open:flex-col backdrop:bg-scrim workspace:m-auto workspace:h-auto workspace:max-h-dialog-tall workspace:w-dialog-wide workspace:max-w-dialog-narrow workspace:rounded-lg workspace:border workspace:border-line workspace:p-4 workspace:shadow-dialog"
            onClose={(closing) => {
                const dialog = closing.currentTarget;
                const answer = dialog.returnValue;

                // Emptied rather than left, because a return value outlives the dialog it was set on.
                dialog.returnValue = '';

                const chosen = mailSpamSubcategories.find((subcategory) => subcategory === answer);

                if (chosen !== undefined) {
                    onChosen(chosen);
                }
            }}
        >
            <div aria-hidden="true" className="flex h-5 shrink-0 items-center justify-center workspace:hidden">
                <span className="h-1 w-9.5 rounded-full bg-line-strong" />
            </div>

            <div className="flex min-h-0 flex-1 flex-col gap-3 overflow-y-auto workspace:flex-none">
                <div className="flex items-center gap-2.25">
                    <Icon name="report" className="size-icon-lg shrink-0 text-muted" />

                    <h2 id={names} className="min-w-0 flex-1 text-xl font-semibold">
                        {title}
                    </h2>

                    <SurfaceControl
                        label={translate('spam.close')}
                        icon="close"
                        onActivate={() => {
                            asked.current?.close();
                        }}
                    />
                </div>

                <p className="text-base/normal text-muted text-pretty">
                    {asking === null ? '' : chooserText(asking, locale, translate)}
                </p>

                <fieldset id={group} className="flex flex-col gap-0.5">
                    <legend className="sr-only">{translate('spam.kindGroup')}</legend>

                    {mailSpamSubcategories.map((subcategory) => (
                        <SpamKind
                            key={subcategory}
                            subcategory={subcategory}
                            group={group}
                            picked={picked === subcategory}
                            onPick={() => {
                                setPicked(subcategory);
                            }}
                        />
                    ))}
                </fieldset>

                <div className="mt-0.5 flex flex-wrap justify-end gap-2.25">
                    <button
                        type="button"
                        className={mannerDrawn.back}
                        onClick={() => {
                            asked.current?.close();
                        }}
                    >
                        {translate(asking?.moved === true ? 'spam.noKind' : 'spam.cancel')}
                    </button>

                    <button
                        {...anchor}
                        type="button"
                        aria-disabled={picked === null}
                        className={picked === null ? spamConfirmOff : mannerDrawn.act}
                    >
                        {confirmLabel}
                    </button>
                    {picked === null ? tip : null}
                </div>
            </div>
        </dialog>
    );
}

const spamConfirmOff = 'rounded-md bg-rail px-4 py-2 text-base font-semibold text-faint';

function SpamKind({
    subcategory,
    group,
    picked,
    onPick,
}: {
    readonly subcategory: MailSpamSubcategory;
    readonly group: string;
    readonly picked: boolean;
    readonly onPick: () => void;
}) {
    const { translate } = useLocalization();
    const danger = isDangerous(subcategory);

    return (
        <label
            className={`flex min-h-10 cursor-pointer items-start gap-2.75 rounded-md px-2.5 py-2 transition hover:bg-hover pointer-coarse:min-h-13 pointer-coarse:px-3 pointer-coarse:py-2.75 ${
                picked ? 'bg-accent-soft ring-1 ring-accent-line ring-inset' : ''
            }`}
        >
            <input
                type="radio"
                name={group}
                value={subcategory}
                checked={picked}
                onChange={onPick}
                className="mt-px size-4.5 shrink-0 cursor-pointer appearance-none rounded-full border-2 border-line-strong bg-panel checked:border-5 checked:border-accent"
            />

            <span className="flex min-w-0 flex-1 flex-col gap-0.5">
                <span
                    className={`flex items-center gap-1.25 text-md font-semibold ${danger ? 'text-error-text' : 'text-text'}`}
                >
                    {danger ? <Icon name="warning" className="size-icon-sm shrink-0" /> : null}
                    {translate(subcategoryLabels[subcategory])}
                </span>

                <span className="text-base/snug text-muted text-pretty">
                    {translate(subcategoryHints[subcategory])}
                </span>
            </span>
        </label>
    );
}

function NotSpamQuestion({
    asked,
    asking,
    onConfirmed,
}: {
    readonly asked: React.RefObject<HTMLDialogElement | null>;
    readonly asking: Extract<SpamAsking, { kind: 'notSpam' }> | null;
    readonly onConfirmed: () => void;
}) {
    const { locale, translate } = useLocalization();
    const messages = asking?.messages ?? 1;
    const moving = asking?.moving ?? 0;
    const form = oneOrOther(locale, messages);
    const count = new Intl.NumberFormat(locale).format(messages);

    const consequence: MessageKey = moving === messages ? `spam.notSpamMoves.${form}` : 'spam.notSpamSomeMove';

    return (
        <Confirmation
            asked={asked}
            mark="report_off"
            question={translate(`spam.notSpamQuestion.${form}`, { count })}
            consequence={
                <p className="text-base text-muted text-pretty">
                    {translate(consequence, { count, moving: new Intl.NumberFormat(locale).format(moving) })}
                </p>
            }
            reversal={{ kind: 'recallable', said: translate('spam.notSpamReversal') }}
            ways={[
                { said: translate('act.cancel'), manner: 'back' },
                { said: translate('spam.notSpamConfirm'), manner: 'act', run: onConfirmed },
            ]}
        />
    );
}
