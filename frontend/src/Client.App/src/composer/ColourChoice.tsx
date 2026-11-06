// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type KeyboardEvent, type MouseEvent, type PointerEvent } from 'react';
import { Icon } from '../controls/Icon';
import { useTip } from '../controls/useTip';
import { useLocalization } from '../localization/useLocalization';
import { FormattingChoice } from './FormattingMenu';
import {
    colourAt,
    nameOfColour,
    ownColourOf,
    readableColourNear,
    readsOnBothThemes,
    textColourHues,
    textColourShades,
    type OwnColour,
} from './textColours';

// What the colour menu holds, as the design draws it: *Automatic*, then the palette as a grid of small discs — a column
// per hue and a row per shade, no names written — then the colours of somebody's own chosen while the composer is
// open, and a disc that opens the picker for another. The picker stands in the menu's place until it is applied or left.

/** Where a colour of somebody's own starts when nothing chosen says where: a mid blue, half way through the band. */
const firstOwnColour: OwnColour = { hue: 215, saturation: 0.7, shade: 0.5 };

export function ColourChoice({
    chosen,
    own,
    onAutomatic,
    onChoose,
}: {
    /** The colour the control holds, as the value it writes; null is *Automatic*. */
    readonly chosen: string | null;

    /** The colours of somebody's own chosen since the composer opened, newest first. */
    readonly own: readonly string[];

    readonly onAutomatic: () => void;
    readonly onChoose: (value: string) => void;
}) {
    const { translate } = useLocalization();
    const [picking, setPicking] = useState(false);

    if (picking) {
        return (
            <OwnColourPicker
                from={chosen}
                onApply={onChoose}
                onBack={() => {
                    setPicking(false);
                }}
            />
        );
    }

    return (
        <>
            <FormattingChoice
                named={translate('compose.colourAutomatic')}
                chosen={chosen === null}
                onChoose={onAutomatic}
            >
                <Icon name="format_color_reset" className="size-icon-lg text-text-soft" />
                <span>{translate('compose.colourAutomatic')}</span>
            </FormattingChoice>

            <div className="grid w-fit grid-cols-11 gap-1 px-2.5 pt-1 pb-1.5 pointer-coarse:gap-0.5">
                {textColourShades.flatMap((_, shade) =>
                    textColourHues.map((hue) => {
                        const value = hue.shades[shade] ?? '';

                        return (
                            <Swatch
                                key={value}
                                value={value}
                                named={nameOfColour(value, translate)}
                                chosen={chosen === value}
                                onChoose={() => {
                                    onChoose(value);
                                }}
                            />
                        );
                    }),
                )}
            </div>

            <p aria-hidden="true" className="px-2.5 pt-1 text-sm text-muted">
                {translate('compose.colourOwnHeading')}
            </p>
            <div className="flex flex-wrap gap-1 px-2.5 pt-1 pb-1.5 pointer-coarse:gap-0.5">
                {own.map((value) => (
                    <Swatch
                        key={value}
                        value={value}
                        named={nameOfColour(value, translate)}
                        chosen={chosen === value}
                        onChoose={() => {
                            onChoose(value);
                        }}
                    />
                ))}
                <AddOwnColour
                    named={translate('compose.colourOwnAdd')}
                    onPress={() => {
                        setPicking(true);
                    }}
                />
            </div>
        </>
    );
}

/** One colour as a small disc in the colour it writes, named for whoever cannot tell it by sight, ringed when held. */
function Swatch({
    value,
    named,
    chosen,
    onChoose,
}: {
    readonly value: string;
    readonly named: string;
    readonly chosen: boolean;
    readonly onChoose: () => void;
}) {
    const { anchor, tip } = useTip({ says: named, onPress: onChoose });

    return (
        <>
            <button
                {...anchor}
                type="button"
                role="menuitemradio"
                aria-checked={chosen}
                aria-label={named}
                tabIndex={-1}
                style={{ backgroundColor: value }}
                className={`dense-target size-4.5 rounded-full transition hover:scale-110 ${
                    chosen ? 'ring-2 ring-accent ring-offset-1 ring-offset-panel' : ''
                }`}
                onMouseDown={keepTheSelection}
            />
            {tip}
        </>
    );
}

/** The disc after a person's own colours, which opens the picker for another. */
function AddOwnColour({ named, onPress }: { readonly named: string; readonly onPress: () => void }) {
    const { anchor, tip } = useTip({ says: named, onPress });

    return (
        <>
            <button
                {...anchor}
                type="button"
                role="menuitem"
                aria-label={named}
                tabIndex={-1}
                className="dense-target flex size-4.5 items-center justify-center rounded-full border border-dashed border-line-strong text-text-soft transition hover:bg-hover"
                onMouseDown={keepTheSelection}
            >
                <Icon name="add" className="size-icon-3xs" />
            </button>
            {tip}
        </>
    );
}

/**
 * A colour of somebody's own, picked the way a paint program offers one: a field of hue across and saturation down, a
 * lightness slider, and the value as hex. Lightness runs across the readable band only, and a typed value outside it
 * is moved to its nearer edge and said to have been, so whatever is applied reads on both themes.
 */
function OwnColourPicker({
    from,
    onApply,
    onBack,
}: {
    readonly from: string | null;
    readonly onApply: (value: string) => void;
    readonly onBack: () => void;
}) {
    const { translate } = useLocalization();
    const [own, setOwn] = useState<OwnColour>(() => (from === null ? null : ownColourOf(from)) ?? firstOwnColour);
    const [typed, setTyped] = useState(() => colourAt(own));
    const field = useRef<HTMLDivElement>(null);
    const hexId = useId();
    const shadeId = useId();
    const typedOwn = ownColourOf(typed);
    const written = readableColourNear(typed) ?? colourAt(own);

    // The picker replaces the grid the keyboard stood on, so focus goes to its first control rather than nowhere.
    useEffect(() => {
        field.current?.focus();
    }, []);

    function move(next: OwnColour): void {
        setOwn(next);
        setTyped(colourAt(next));
    }

    function pointAt(event: PointerEvent<HTMLDivElement>): void {
        const box = event.currentTarget.getBoundingClientRect();

        move({
            ...own,
            hue: clamped((event.clientX - box.left) / box.width) * 360,
            saturation: 1 - clamped((event.clientY - box.top) / box.height),
        });
    }

    function stepField(event: KeyboardEvent<HTMLDivElement>): void {
        const steps: Readonly<Record<string, Partial<OwnColour>>> = {
            ArrowLeft: { hue: (own.hue + 355) % 360 },
            ArrowRight: { hue: (own.hue + 5) % 360 },
            ArrowUp: { saturation: clamped(own.saturation + 0.05) },
            ArrowDown: { saturation: clamped(own.saturation - 0.05) },
        };
        const step = steps[event.key];

        if (step !== undefined) {
            event.preventDefault();
            event.stopPropagation();
            move({ ...own, ...step });
        }
    }

    return (
        <div role="group" aria-label={translate('compose.colourPicker')} className="flex flex-col gap-2 px-2.5 pb-1.5">
            <div
                ref={field}
                role="slider"
                tabIndex={0}
                aria-label={translate('compose.colourPickerField')}
                aria-valuemin={0}
                aria-valuemax={360}
                aria-valuenow={Math.round(own.hue)}
                aria-valuetext={translate('compose.colourPickerFieldValue', {
                    hue: String(Math.round(own.hue)),
                    saturation: String(Math.round(own.saturation * 100)),
                })}
                className="colour-field relative h-28 w-full touch-none rounded-md"
                onPointerDown={(event) => {
                    event.currentTarget.setPointerCapture(event.pointerId);
                    pointAt(event);
                }}
                onPointerMove={(event) => {
                    if (event.buttons === 1) {
                        pointAt(event);
                    }
                }}
                onKeyDown={stepField}
            >
                <span
                    aria-hidden="true"
                    style={{ left: `${(own.hue / 360) * 100}%`, top: `${(1 - own.saturation) * 100}%` }}
                    className="absolute size-3.5 -translate-1/2 rounded-full border-2 border-on-accent shadow-overlay"
                />
            </div>

            <label htmlFor={shadeId} className="text-sm text-muted">
                {translate('compose.colourPickerShade')}
            </label>
            <input
                id={shadeId}
                type="range"
                min={0}
                max={100}
                value={Math.round(own.shade * 100)}
                style={{
                    background: `linear-gradient(to right, ${colourAt({ ...own, shade: 0 })}, ${colourAt({ ...own, shade: 1 })})`,
                }}
                className="h-2 w-full cursor-pointer appearance-none rounded-full"
                onChange={(event) => {
                    move({ ...own, shade: Number(event.target.value) / 100 });
                }}
            />

            <div className="flex items-center gap-2.5">
                <span
                    aria-hidden="true"
                    style={{ backgroundColor: written }}
                    className="size-8 shrink-0 rounded-full"
                />
                <label htmlFor={hexId} className="text-sm text-muted">
                    {translate('compose.colourPickerHex')}
                </label>
                <input
                    id={hexId}
                    value={typed}
                    spellCheck={false}
                    autoComplete="off"
                    maxLength={7}
                    className="h-8 w-24 min-w-0 rounded-md border border-line bg-panel px-2 font-mono text-base text-text"
                    onChange={(event) => {
                        const next = event.target.value;
                        const nextOwn = ownColourOf(next);

                        setTyped(next);

                        if (nextOwn !== null) {
                            setOwn(nextOwn);
                        }
                    }}
                />
            </div>

            {typedOwn === null ? (
                <p role="alert" className="text-sm text-error-text">
                    {translate('compose.colourPickerInvalid')}
                </p>
            ) : readsOnBothThemes(typed) ? null : (
                <p role="status" className="text-sm text-muted">
                    {translate('compose.colourPickerAdjusted', { value: written })}
                </p>
            )}

            <div className="flex justify-end gap-2">
                <button
                    type="button"
                    className="h-8 rounded-md px-3 text-base text-text-soft transition hover:bg-hover"
                    onMouseDown={keepTheSelection}
                    onClick={onBack}
                >
                    {translate('compose.colourPickerBack')}
                </button>
                <button
                    type="button"
                    aria-disabled={typedOwn === null ? true : undefined}
                    className={`h-8 rounded-md bg-accent px-3 text-base font-semibold text-on-accent transition hover:bg-accent-strong ${
                        typedOwn === null ? 'cursor-not-allowed opacity-40' : ''
                    }`}
                    onMouseDown={keepTheSelection}
                    onClick={() => {
                        if (typedOwn !== null) {
                            onApply(written);
                        }
                    }}
                >
                    {translate('compose.colourPickerApply')}
                </button>
            </div>
        </div>
    );
}

// The selection a choice acts on is the editor's, and a press that reached the editor first would collapse it.
function keepTheSelection(event: MouseEvent): void {
    event.preventDefault();
}

function clamped(value: number): number {
    return Math.min(1, Math.max(0, value));
}
