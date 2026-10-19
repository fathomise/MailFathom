// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, type ReactNode, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { SkeletonLines } from '../controls/Skeleton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { AnswerBlockState } from './answerBlocks';

// The one shape a block of an answer is drawn in, and therefore the one place its six states are written. A renderer
// says which state it is in and what its body holds; everything around that — the name over it, the waiting, the
// emptiness, the failure and the way out of one — is here, so that nine renderers cannot come to disagree about what a
// block that returned nothing looks like.
//
// It is a region a reader lands on rather than a control they press: the card takes focus so the keyboard moves from
// block to block without walking every citation inside one, and it carries the block's own name so that landing on it
// says which block it is.

// The lines a block of prose stands as while it is being composed, which is the design's own raggedness rather
// than three bars of one length.
const waitingLines = [
    { fills: 92, height: 'h-3.25 mb-2' },
    { fills: 78, height: 'h-3.25 mb-2' },
    { fills: 55, height: 'h-3.25' },
];

// The three states that draw a sentence and an icon where a body would be. Named once because three things read
// it: which icon, which sentence, and whether a way out is on the screen to be left focused on.
type NothingDrawn = 'empty' | 'error' | 'offline';

const stateIcons: Readonly<Record<NothingDrawn, IconName>> = {
    empty: 'inbox',
    error: 'error',
    offline: 'cloud_off',
};

const stateNotes: Readonly<Record<NothingDrawn, MessageKey>> = {
    empty: 'answerBlock.empty',
    error: 'answerBlock.error',
    offline: 'answerBlock.offline',
};

function nothingDrawn(state: AnswerBlockState): state is NothingDrawn {
    return state === 'empty' || state === 'error' || state === 'offline';
}

/**
 * How a card stands against the thread: settled like any block, lifted while it waits on somebody's decision, and
 * outlined only once it was declined, so a turned-down proposal reads as what the history kept rather than as an answer.
 */
export type BlockCardTone = 'settled' | 'awaiting' | 'declined';

// The design's one block chrome, shared by the Agent, Discover and the thread's answer: a card on the panel, its edge
// in the accent while it waits on a decision, and a dashed outline with nothing else once it was declined.
const tones: Readonly<Record<BlockCardTone, string>> = {
    settled: 'border-line bg-panel shadow-raised',
    awaiting: 'border-accent-line bg-panel shadow-raised',
    declined: 'border-dashed border-line-strong bg-transparent',
};

// The tile the block's symbol stands in, which takes the card's tone.
const tiles: Readonly<Record<BlockCardTone, string>> = {
    settled: 'bg-sunken text-text-soft ring-line-soft',
    awaiting: 'bg-accent-soft text-accent-deep ring-accent-line',
    declined: 'bg-transparent text-faint ring-line',
};

/**
 * The tray at the foot of a block that holds its controls and its way out, flush with the card's edges — in the
 * accent while the block waits on a decision.
 */
const blockTray: Readonly<Record<BlockCardTone, string>> = {
    settled:
        '-mx-3.5 -mb-3.5 mt-0.5 rounded-b-lg border-t border-line-soft px-3.5 py-3 workspace:-mx-4.5 workspace:-mb-4 workspace:px-4.5',
    awaiting:
        '-mx-3.5 -mb-3.5 mt-0.5 rounded-b-lg border-t border-accent-line bg-accent-soft px-3.5 py-3 workspace:-mx-4.5 workspace:-mb-4 workspace:px-4.5',
    declined:
        '-mx-3.5 -mb-3.5 mt-0.5 rounded-b-lg border-t border-line-soft px-3.5 py-3 workspace:-mx-4.5 workspace:-mb-4 workspace:px-4.5',
};

/**
 * One block of an answer, in whichever of its six states it is.
 *
 * @param label What this block is called, already in the reader's language.
 * @param icon The symbol the block's kind is drawn with in its header tile.
 * @param meta What the block says about itself beside its name — how much it holds, or that it is still working.
 * @param state What the block is doing, which decides everything below the name.
 * @param note What the failing or empty state says, where the caller knows more about it than the state does, and
 * nothing where the state's own sentence is the whole of it.
 * @param onRetry What the way out of a failure does, and nothing where this block has none to offer.
 * @param retryName What the way out of a failure is called to a screen reader, where the block can name what it retries.
 * @param emptyIcon What an empty block draws above its sentence, where the block's own type has a better one.
 * @param tone How the card stands against the thread, which only a proposal changes.
 * @param chip What the block says about where it stands, drawn at the end of the line its name is on.
 * @param tray The controls and the ways out the block offers, drawn in the tray at its foot once it has a body.
 * @param children The body, which is drawn for a block that is ready and for one that is partly so.
 */
export function AnswerBlockCard({
    label,
    icon,
    meta,
    state,
    note,
    onRetry,
    retryName,
    emptyIcon,
    tone = 'settled',
    chip,
    tray,
    children,
}: {
    readonly label: string;
    readonly icon: IconName;
    readonly meta?: string | undefined;
    readonly state: AnswerBlockState;
    readonly note?: string | undefined;
    readonly onRetry?: (() => void) | undefined;
    readonly retryName?: string | undefined;
    readonly emptyIcon?: IconName | undefined;
    readonly tone?: BlockCardTone;
    readonly chip?: ReactNode;
    readonly tray?: ReactNode;
    readonly children?: ReactNode;
}) {
    const { translate } = useLocalization();
    const named = useId();

    const region = useRef<HTMLElement>(null);
    const offeredAWayOut = useRef(false);
    const offersAWayOut = onRetry !== undefined && nothingDrawn(state);

    // The control that brought the block back has gone with the state it stood on, so the keyboard goes to the card
    // rather than being left on nothing. It is the move `containment/Containment.tsx` makes when a region recovers,
    // and for its reason: a block that has just been asked for again is often still reading, so what is inside it a
    // moment later may be nothing at all.
    useEffect(() => {
        if (offeredAWayOut.current && !offersAWayOut) {
            region.current?.focus();
        }

        offeredAWayOut.current = offersAWayOut;
    }, [offersAWayOut]);

    return (
        <BlockCard chip={chip} icon={icon} label={label} labelId={named} meta={meta} ref={region} tone={tone}>
            {state === 'loading' ? (
                <>
                    <p className="sr-only" role="status">
                        {translate('answerBlock.loading', { block: label })}
                    </p>

                    <SkeletonLines lines={waitingLines} />
                </>
            ) : null}

            {state === 'ready' || state === 'partial' ? children : null}

            {state === 'partial' ? (
                <p className="flex items-start gap-1.75 rounded-md bg-warning-soft px-2.75 py-2 text-sm text-warning-text">
                    <Icon className="mt-0.25 size-icon-sm" name="hourglass_top" />
                    {translate('answerBlock.partial')}
                </p>
            ) : null}

            {(state === 'ready' || state === 'partial') && tray !== undefined && tray !== null ? (
                <div className={`flex flex-col gap-2.5 ${blockTray[tone]}`}>{tray}</div>
            ) : null}

            {nothingDrawn(state) ? (
                <div className="flex flex-col items-center gap-1.75 px-2.5 py-5.5 text-center">
                    <Icon
                        className={`size-icon-2xl ${state === 'error' ? 'text-warning' : 'text-faint'}`}
                        name={state === 'empty' && emptyIcon !== undefined ? emptyIcon : stateIcons[state]}
                    />

                    <p className="max-w-95 text-sm text-text-soft text-pretty">
                        {note ?? translate(stateNotes[state])}
                    </p>

                    {onRetry === undefined ? null : (
                        <button
                            className="mt-0.5 rounded-md bg-accent px-3.5 py-1.75 text-sm font-medium text-on-accent transition hover:bg-accent-strong"
                            type="button"
                            aria-label={retryName}
                            onClick={onRetry}
                        >
                            {translate('connection.retry')}
                        </button>
                    )}
                </div>
            ) : null}
        </BlockCard>
    );
}

/**
 * A block whose type this build cannot draw, which is both a type the contract does not carry and one whose renderer
 * has not been written yet.
 *
 * It is a card rather than a gap, because what the reader is owed is that the answer is missing a part and which part:
 * a block silently dropped is an answer that looks complete and is not.
 */
export function UnrecognisedAnswerBlock({ named }: { readonly named: string }) {
    const { translate } = useLocalization();
    const labelled = useId();

    return (
        <BlockCard
            icon="extension_off"
            label={translate('answerBlock.unrecognised')}
            labelId={labelled}
            meta={translate('answerBlock.unrecognisedType', { named })}
        >
            <div className="flex items-start gap-2.75">
                <Icon className="mt-0.5 size-icon-2xl shrink-0 text-faint" name="extension_off" />

                <div className="flex flex-col gap-1.25">
                    <p className="text-sm text-text-soft text-pretty">
                        {translate('answerBlock.unrecognisedBody', { named })}
                    </p>

                    <p className="text-xs text-faint">{translate('answerBlock.unrecognisedRemedy')}</p>
                </div>
            </div>
        </BlockCard>
    );
}

/**
 * The chrome every block stands in: the card, a header of the block's symbol in its tile, its name, and what it says
 * about itself, with where it stands at the end of that line.
 */
function BlockCard({
    label,
    icon,
    labelId,
    meta,
    ref,
    tone = 'settled',
    chip,
    children,
}: {
    readonly label: string;
    readonly icon: IconName;
    readonly labelId: string;
    readonly meta?: string | undefined;
    readonly ref?: RefObject<HTMLElement | null>;
    readonly tone?: BlockCardTone;
    readonly chip?: ReactNode;
    readonly children: ReactNode;
}) {
    return (
        <article
            aria-labelledby={labelId}
            className={`flex min-w-0 flex-col gap-3 rounded-lg border px-3.5 py-3.5 workspace:px-4.5 workspace:py-4 ${tones[tone]}`}
            ref={ref}
            tabIndex={0}
        >
            <div className="flex min-h-6.5 flex-wrap items-center gap-2.5">
                <span
                    className={`flex size-6.5 shrink-0 items-center justify-center rounded-md ring-1 ring-inset ${tiles[tone]}`}
                >
                    <Icon className="size-icon-sm" name={icon} />
                </span>

                <h3 className="text-base font-semibold tracking-tight whitespace-nowrap text-text" id={labelId}>
                    {label}
                </h3>

                {meta === undefined ? null : <p className="min-w-0 text-sm text-faint">{meta}</p>}

                {chip}
            </div>

            {children}
        </article>
    );
}
