// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { useEffect, useId, useRef, useState, type CSSProperties, type KeyboardEvent, type RefObject } from 'react';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { useCoarsePointer, useWideWorkspace } from '../shell/useWideWorkspace';
import { presetShapes, rewritePresets } from './rewritePresets';
import type { RewriteHow } from './usePassageChange';
import type { SelectedPassage, SelectionBox } from './useSelectedPassage';

// What a selection in a draft raises: a small AI button by the selection, and on its press the bar that says how to
// rewrite it. The button alone comes first, so a right press on the passage still finds the system's own menu.
//
// **On a fine pointer both float by the passage. Under a finger the button stands below the selection's last line,
// beside its end handle — the system's selection menu stands above the passage — and the bar docks on what the surface
// names**: the Agent's field, or the composer's foot, across its width. Docked, it is a tool for the selection rather
// than a sheet, so it takes no grabber and no pull: clearing the selection closes it as × does.
//
// Both are drawn in the surface's own tree rather than at the end of the document, so the button is the next stop the
// keyboard reaches after the words it is about.

// Where a floating bar keeps from the window's edge, how wide it is, and how far from the passage it stands.
const edge = 12;
const floatingWidth = 440;
const apart = 10;

/** Where the button stands, by the pointer: above the selection's end under a mouse, below it under a finger. */
function buttonPlace(box: SelectionBox, coarse: boolean, dockTop: number): CSSProperties {
    if (coarse) {
        const size = 44;
        const beside =
            box.endRight + 16 + size <= window.innerWidth - 10 ? box.endRight + 16 : box.endRight - 16 - size;
        const below = box.endBottom + 6 + size <= dockTop - 8 ? box.endBottom + 6 : box.top - size - 8;

        return { left: Math.max(10, beside), top: below };
    }

    const size = 28;
    const above = box.endTop - size - 4;

    return {
        left: Math.max(edge, Math.min(box.endRight - size / 2, window.innerWidth - size - edge)),
        top: above < edge ? box.bottom + 4 : above,
    };
}

/**
 * Keeps Tab inside the bar while it is open, from its last control round to its first and back. It is a dialog the
 * page stays usable beside — the passage has to stay selected under it — so nothing makes the page inert, and the
 * keyboard is kept by hand instead.
 */
function keepTheKeyboardInside(bar: HTMLElement, event: KeyboardEvent<HTMLElement>): void {
    const controls = [...bar.querySelectorAll<HTMLElement>('button, input')];
    const first = controls.at(0);
    const last = controls.at(-1);
    const leaving = event.shiftKey ? document.activeElement === first : document.activeElement === last;

    if (leaving) {
        event.preventDefault();
        (event.shiftKey ? last : first)?.focus();
    }
}

/** Where the bar stands: over the passage, above or below it by where it is, or docked across the surface's width. */
function barPlace(box: SelectionBox, coarse: boolean, dock: DOMRect | null): CSSProperties {
    if (coarse) {
        return dock === null
            ? { left: 10, right: 10, bottom: 10 }
            : { left: dock.left, width: dock.width, bottom: window.innerHeight - dock.top + 8 };
    }

    const left = Math.max(edge, Math.min(box.left, window.innerWidth - floatingWidth - edge));

    return box.top > 300
        ? { left, top: box.top - apart, transform: 'translateY(-100%)' }
        : { left, top: box.bottom + apart };
}

export function PassageRewriteBar({
    selected,
    open,
    inMessage,
    dock,
    onOpen,
    onClose,
    onRewrite,
}: {
    readonly selected: SelectedPassage;
    readonly open: boolean;

    /** Whether the passage is in the composer's message rather than in the Agent's draft, which names the bar. */
    readonly inMessage: boolean;

    /** What the bar docks on under a finger, or `null` to dock across the foot of the window. */
    readonly dock: RefObject<HTMLElement | null> | null;
    readonly onOpen: () => void;
    readonly onClose: () => void;
    readonly onRewrite: (how: RewriteHow) => void;
}) {
    const { translate } = useLocalization();
    const coarse = useCoarsePointer();
    const wide = useWideWorkspace();
    const [said, setSaid] = useState('');
    const presets = useRef<HTMLDivElement>(null);
    const quoteId = useId();

    // Opening the bar is a view change, so the keyboard goes into it, onto its first preset.
    useEffect(() => {
        if (open) {
            presets.current?.querySelector<HTMLElement>('button')?.focus();
        }
    }, [open]);

    const dockBox = dock?.current?.getBoundingClientRect() ?? null;

    if (!open) {
        return (
            <button
                type="button"
                aria-label={translate('rewrite.button')}
                aria-haspopup="dialog"
                style={buttonPlace(selected.box, coarse, dockBox?.top ?? window.innerHeight)}
                className="fixed z-40 flex size-7 animate-pop-in items-center justify-center rounded-md bg-accent text-on-accent shadow-overlay transition hover:brightness-110 pointer-coarse:size-11 pointer-coarse:rounded-lg"
                // Pressing it keeps the selection it is about rather than collapsing it onto the button.
                onMouseDown={(event) => {
                    event.preventDefault();
                }}
                onKeyDown={(event) => {
                    if (event.key === 'Escape') {
                        onClose();
                    }
                }}
                onClick={onOpen}
            >
                <Icon name="auto_awesome" className="size-icon-sm pointer-coarse:size-icon-lg" />
            </button>
        );
    }

    function askSaid(): void {
        const words = said.trim();

        if (words !== '') {
            onRewrite({ said: words });
        }
    }

    return (
        <div
            role="dialog"
            aria-label={translate(inMessage ? 'rewrite.barMessage' : 'rewrite.barDraft')}
            aria-describedby={quoteId}
            style={barPlace(selected.box, coarse, dockBox)}
            className="fixed z-40 flex w-110 max-w-full animate-pop-in flex-col gap-2.5 rounded-xl border border-line bg-panel py-3 ps-3.5 pe-3 shadow-dialog pointer-coarse:w-auto pointer-coarse:max-w-none"
            // A press inside the bar keeps the passage selected, except in its field, which has to take the caret.
            onMouseDown={(event) => {
                if (!(event.target instanceof HTMLInputElement)) {
                    event.preventDefault();
                }
            }}
            onKeyDown={(event) => {
                if (event.key === 'Escape') {
                    event.stopPropagation();
                    onClose();
                }

                if (event.key === 'Tab') {
                    keepTheKeyboardInside(event.currentTarget, event);
                }
            }}
        >
            <div className="flex items-center gap-2">
                <Icon name="edit_note" className="size-icon-md text-accent-deep" />
                <p className="min-w-0 flex-1 text-base font-semibold text-text">{translate('rewrite.title')}</p>
                <button
                    type="button"
                    aria-label={translate('rewrite.close')}
                    className="flex size-7 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text pointer-coarse:-my-2.5 pointer-coarse:-me-2 pointer-coarse:size-11"
                    onClick={onClose}
                >
                    <Icon name="close" className="size-icon-md" />
                </button>
            </div>

            <p id={quoteId} lang="" className="line-clamp-2 border-s-2 border-accent-line ps-2.5 text-sm text-muted">
                {translate('rewrite.quoted', { words: selected.words })}
            </p>

            <div
                ref={presets}
                className={`flex gap-1.5 pointer-coarse:gap-2 ${coarse && !wide ? 'touch-strip' : 'flex-wrap'}`}
            >
                {rewritePresets.map((preset) => {
                    const shape = presetShapes[preset];
                    const label = translate(shape.said);

                    return (
                        <button
                            key={preset}
                            type="button"
                            aria-label={translate('rewrite.presetName', { preset: label.toLocaleLowerCase() })}
                            className="inline-flex shrink-0 touch-extended items-center gap-1.5 rounded-full border border-line-strong bg-panel px-2.75 py-1.5 text-base whitespace-nowrap text-text-soft transition hover:border-accent hover:text-accent-deep pointer-coarse:min-h-9 pointer-coarse:px-3.25 pointer-coarse:py-0"
                            onClick={() => {
                                onRewrite({ preset });
                            }}
                        >
                            <Icon name={shape.icon} className="size-icon-sm text-muted" />
                            {label}
                        </button>
                    );
                })}
            </div>

            <div className="flex items-center gap-2 rounded-field border border-line-soft bg-sunken py-1 ps-2.75 pe-1 pointer-coarse:ps-3">
                <input
                    type="text"
                    value={said}
                    aria-label={translate('rewrite.say')}
                    placeholder={translate(coarse && !wide ? 'rewrite.sayPlaceholderShort' : 'rewrite.sayPlaceholder')}
                    className="min-w-0 flex-1 bg-transparent text-base text-text outline-none"
                    onChange={(event) => {
                        setSaid(event.target.value);
                    }}
                    onKeyDown={(event) => {
                        if (event.key === 'Enter' && !event.nativeEvent.isComposing) {
                            event.preventDefault();
                            askSaid();
                        }
                    }}
                />
                <button
                    type="button"
                    className="inline-flex items-center rounded-md bg-accent px-3 py-1.5 text-base font-semibold whitespace-nowrap text-on-accent transition hover:opacity-90 pointer-coarse:min-h-11 pointer-coarse:px-3.5"
                    onClick={askSaid}
                >
                    {translate('rewrite.run')}
                </button>
            </div>

            <p className="text-sm text-faint text-pretty">{translate('rewrite.wholeSentences')}</p>
        </div>
    );
}
