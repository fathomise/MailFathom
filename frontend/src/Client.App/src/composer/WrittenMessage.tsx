// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { createElement, Fragment, useId, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useCoarsePointer } from '../shell/useWideWorkspace';
import { edit, insertWritten } from './editing';
import { FormattingChoice, FormattingMenu } from './FormattingMenu';
import {
    drawnAlignment,
    followable,
    fontSizeOf,
    pastedAddress,
    writtenIn,
    type WrittenAlignment,
    type WrittenNode,
    type WrittenSize,
} from './writtenText';

// The words of a message being written, as the design draws them: an editable region over a row of formatting
// controls, with the row replaced by a toggle where a finger is what drives the screen.
//
// **The region is the document's rather than React's.** What is inside a `contenteditable` is written by the browser
// as somebody types, so re-rendering it from state would put the caret back at the start on every keystroke. The tree
// it opens holding is therefore rendered once and never again — `opening` freezes it at the first render — and every
// later reading goes the other way, out of the document and into the closed set `writtenText.ts` states. Nothing here
// writes markup and nothing parses any, which is what lets an editor exist at all under ADR 0024's rule.
//
// **The editing is `editing.ts`'s**, and what it is told first is to compose with elements rather than with inline
// styles, which is what keeps its output inside the set above.
//
// **A colour and a size are the engine's `<font>` in the region and a `span` in the tree.** `foreColor` and `fontSize`
// write `<font color>` and `<font size>` with styles off, and the region is drawn back in that same spelling, so what
// the commands find already there is what they wrote themselves and they merge with it rather than nesting around it.
// `writtenIn` reads each into the closed value and drops whatever is off the closed set; the stylesheet's
// `written-region` draws them by the design's rules rather than the browser's.

type FormattingMenuKind = 'colour' | 'size' | 'alignment';

/** One control on the formatting bar: what it is called, what it does, and how the design draws it. */
interface FormattingControl {
    readonly key: string;

    /** The editing command it performs, and the argument the command takes where it takes one. */
    readonly command?: string;
    readonly argument?: string;

    readonly named: MessageKey;

    /** The typographic mark the design draws it as, for the six that are read as letters rather than as symbols. */
    readonly mark?: string;

    /** How that mark is drawn, which is what makes the bold control look bold. */
    readonly marked?: string;

    /** The symbol the design draws it as, for those that are not letters. */
    readonly icon?: IconName;

    /** Whether it asks for an address before it acts, which only the link does. */
    readonly asksForAnAddress?: true;

    /** The menu of choices it opens instead of acting, for the three that take one. */
    readonly opens?: FormattingMenuKind;
}

// The design's own controls, in the design's own six groups, which a fine pointer wraps whole and never splits.
const formattingGroups: readonly (readonly FormattingControl[])[] = [
    [
        { key: 'bold', command: 'bold', named: 'compose.formatBold', mark: 'B', marked: 'font-semibold' },
        { key: 'italic', command: 'italic', named: 'compose.formatItalic', mark: 'I', marked: 'font-serif italic' },
        { key: 'underline', command: 'underline', named: 'compose.formatUnderline', mark: 'U', marked: 'underline' },
        {
            key: 'strikethrough',
            command: 'strikeThrough',
            named: 'compose.formatStrikethrough',
            mark: 'S',
            marked: 'line-through',
        },
    ],
    [
        { key: 'colour', named: 'compose.formatColour', icon: 'format_color_text', opens: 'colour' },
        { key: 'size', named: 'compose.formatSize', icon: 'format_size', opens: 'size' },
    ],
    [{ key: 'alignment', named: 'compose.formatAlignment', opens: 'alignment' }],
    [
        { key: 'bulleted', command: 'insertUnorderedList', named: 'compose.formatBulletedList', mark: '•—' },
        { key: 'numbered', command: 'insertOrderedList', named: 'compose.formatNumberedList', mark: '1.' },
        {
            key: 'quote',
            command: 'formatBlock',
            argument: 'blockquote',
            named: 'compose.formatQuote',
            icon: 'format_quote',
        },
    ],
    [{ key: 'link', command: 'createLink', named: 'compose.formatLink', icon: 'link', asksForAnAddress: true }],
    [{ key: 'clear', command: 'removeFormat', named: 'compose.formatClear', icon: 'format_clear' }],
];

/** One swatch of the palette ADR 0047 states: the value a message is sent with, and the token it is drawn in here. */
interface PaletteColour {
    readonly value: string;
    readonly named: MessageKey;
    readonly ink: string;
}

const palette: readonly PaletteColour[] = [
    { value: '#b42318', named: 'compose.colourRed', ink: 'bg-ink-red' },
    { value: '#b54708', named: 'compose.colourOrange', ink: 'bg-ink-orange' },
    { value: '#8a6100', named: 'compose.colourAmber', ink: 'bg-ink-amber' },
    { value: '#067647', named: 'compose.colourGreen', ink: 'bg-ink-green' },
    { value: '#0e7490', named: 'compose.colourTeal', ink: 'bg-ink-teal' },
    { value: '#175cd3', named: 'compose.colourBlue', ink: 'bg-ink-blue' },
    { value: '#6941c6', named: 'compose.colourPurple', ink: 'bg-ink-purple' },
    { value: '#c11574', named: 'compose.colourPink', ink: 'bg-ink-pink' },
    { value: '#667085', named: 'compose.colourGrey', ink: 'bg-ink-grey' },
];

/** One step of the size scale as the menu offers it, normal being the absence of a step. */
interface SizeStep {
    readonly size: WrittenSize | null;
    readonly named: MessageKey;

    /** The step the name is drawn at, so each choice shows the size it gives. */
    readonly drawn: string;
}

const normalStep: SizeStep = { size: null, named: 'compose.sizeNormal', drawn: '' };

const sizeSteps: readonly SizeStep[] = [
    { size: 'small', named: 'compose.sizeSmall', drawn: 'text-step-small' },
    normalStep,
    { size: 'large', named: 'compose.sizeLarge', drawn: 'text-step-large' },
    { size: 'larger', named: 'compose.sizeLarger', drawn: 'text-step-larger' },
    { size: 'largest', named: 'compose.sizeLargest', drawn: 'text-step-largest' },
];

// `fontSize` takes `<font size>`'s own scale, and normal is its 3 — which the tree reads as no size at all.
const normalFontSize = '3';

/** One alignment as the menu offers it, with the command that sets it. */
interface AlignmentChoice {
    readonly alignment: WrittenAlignment;
    readonly named: MessageKey;
    readonly icon: IconName;
    readonly command: string;
}

const startAlignment: AlignmentChoice = {
    alignment: 'start',
    named: 'compose.alignStart',
    icon: 'format_align_left',
    command: 'justifyLeft',
};

const alignmentChoices: readonly AlignmentChoice[] = [
    startAlignment,
    { alignment: 'center', named: 'compose.alignCenter', icon: 'format_align_center', command: 'justifyCenter' },
    { alignment: 'end', named: 'compose.alignEnd', icon: 'format_align_right', command: 'justifyRight' },
    { alignment: 'justify', named: 'compose.alignJustify', icon: 'format_align_justify', command: 'justifyFull' },
];

/** The choice each of the three controls holds, which is the one last chosen — what the design names it by. */
interface Chosen {
    readonly colour: PaletteColour | null;
    readonly size: SizeStep;
    readonly alignment: AlignmentChoice;
}

/** The menu standing under the bar, where it stands, and the control it opened from. */
interface OpenMenu {
    readonly kind: FormattingMenuKind;
    readonly under: number;
    readonly openedFrom: HTMLElement;
}

export function WrittenMessage({
    opened,
    onWritten,
    onSendAsked,
    beforeTheWords,
}: {
    /** What the region opens holding, read once: a message this tab was already writing, or nothing. */
    readonly opened: readonly WrittenNode[];

    readonly onWritten: (written: readonly WrittenNode[]) => void;

    /** What the design's own shortcut asks for, which is the send question rather than the send. */
    readonly onSendAsked: () => void;

    /**
     * What stands between the formatting bar and the words, which is where the design draws what became of a drafting.
     *
     * A slot rather than a component of its own, because the bar and the region are one element's children here: the
     * row the design puts between them has no other way in, and the alternative is the composer reaching into this
     * component's markup.
     */
    readonly beforeTheWords?: ReactNode;
}) {
    const { translate } = useLocalization();
    const coarse = useCoarsePointer();
    const [opening] = useState(opened);
    const [barShown, setBarShown] = useState(false);
    const [address, setAddress] = useState<string | null>(null);
    const [menu, setMenu] = useState<OpenMenu | null>(null);
    const [chosen, setChosen] = useState<Chosen>({ colour: null, size: normalStep, alignment: startAlignment });
    const words = useRef<HTMLDivElement>(null);
    const bar = useRef<HTMLDivElement>(null);
    const holding = useRef<Range | null>(null);
    const pastingPlain = useRef(false);
    const addressId = useId();

    // What the design decides about the bar: drawn outright where a pointer is fine, and behind a toggle where a
    // finger is what drives the screen — the keyboard and the message needing the room a bar would take.
    const drawTheBar = !coarse || barShown;

    function written(): void {
        if (words.current !== null) {
            onWritten(writtenIn(words.current));
        }
    }

    // The selection a control is about to take the focus away from — to an address field or to a menu — held so the
    // command it ends in acts on it rather than on wherever the focus went.
    function holdTheSelection(): void {
        const selection = window.getSelection();
        const range = selection !== null && selection.rangeCount > 0 ? selection.getRangeAt(0) : null;

        holding.current =
            range !== null && words.current?.contains(range.commonAncestorContainer) === true ? range : null;
    }

    function apply(command: string, argument?: string): void {
        words.current?.focus();

        // The selection a control took the focus away from, put back before the command reads it. Only the link and
        // the three menus ask for anything, so this is null for the rest and they act on what the region still holds.
        const held = holding.current;

        if (held !== null) {
            const selection = window.getSelection();

            selection?.removeAllRanges();
            selection?.addRange(held);
            holding.current = null;
        }

        try {
            // Elements rather than inline styles, so what the commands below compose stays inside the closed set
            // `writtenText.ts` states rather than arriving as a span this client would unwrap and lose.
            edit('styleWithCSS', 'false');
            edit(command, argument);
        } catch {
            // A command an engine does not carry leaves the message exactly as it was, which is the whole of what it
            // costs: nothing here reads a return value, and refusing to draw the control would be worse.
        }

        written();
    }

    // ADR 0047's paste order as far as the closed set reaches today: Ctrl+Shift+V lands the words alone, one address
    // makes a link, and anything else is pasted as text.
    function paste(pasted: string): void {
        const address = pastingPlain.current ? null : pastedAddress(pasted);

        pastingPlain.current = false;

        if (address === null) {
            apply('insertText', pasted);
        } else if (window.getSelection()?.isCollapsed === false) {
            // The selected words become the link and keep their own formatting, and a link they sat in is replaced.
            apply('createLink', address);
        } else {
            // A space after the link, so what is typed next is not part of it.
            insertWritten([{ element: 'a', address, holds: [{ text: address }] }, { text: '\u00a0' }]);
            written();
        }
    }

    function askForAnAddress(): void {
        holdTheSelection();
        setAddress('');
    }

    // Giving the address up, which is both what escape does and what an address this client would not follow leaves.
    // The held selection goes with it: kept, it would be put back over whatever was selected next, and the control
    // pressed after a link somebody thought better of would act somewhere nobody was looking.
    function giveUpTheAddress(): void {
        holding.current = null;
        setAddress(null);
        words.current?.focus();
    }

    // The same control again closes its menu; any other opens in its place, under the control that asked for it.
    function openTheMenu(kind: FormattingMenuKind, control: HTMLElement): void {
        if (menu?.kind === kind) {
            closeTheMenu();

            return;
        }

        const from = bar.current?.getBoundingClientRect().left ?? 0;

        holdTheSelection();
        setMenu({ kind, under: control.getBoundingClientRect().left - from, openedFrom: control });
    }

    // A menu left without a choice gives its held selection up, for the reason giving up an address does.
    function closeTheMenu(): void {
        holding.current = null;
        setMenu(null);
    }

    function choose(next: Partial<Chosen>, command: string, argument?: string): void {
        setMenu(null);
        setChosen({ ...chosen, ...next });
        apply(command, argument);
    }

    // *Automatic* takes the colour off, which no editing command names. Asked for the colour the words around it are
    // already drawn in, an engine finds nothing to change and removes the `<font color>` it wrote, and one that wrote
    // a `<font>` anyway wrote a colour `writtenIn` drops, since the region's own is no `#rrggbb`.
    function takeTheColourOff(): void {
        const surrounding = words.current === null ? '' : getComputedStyle(words.current).color;

        choose({ colour: null }, 'foreColor', surrounding);
    }

    function controlName(control: FormattingControl): string {
        if (control.opens === undefined) {
            return translate(control.named);
        }

        const holds: Readonly<Record<FormattingMenuKind, Readonly<Record<string, string>>>> = {
            colour: { colour: translate(chosen.colour?.named ?? 'compose.colourAutomatic') },
            size: { size: translate(chosen.size.named) },
            alignment: { alignment: translate(chosen.alignment.named) },
        };

        return translate(control.named, holds[control.opens]);
    }

    function press(control: FormattingControl, element: HTMLElement): void {
        if (control.opens !== undefined) {
            openTheMenu(control.opens, element);
        } else if (control.asksForAnAddress === true) {
            askForAnAddress();
        } else if (control.command !== undefined) {
            apply(control.command, control.argument);
        }
    }

    function insertTheLink(): void {
        const written = address ?? '';

        if (followable(written)) {
            setAddress(null);
            apply('createLink', written);
        } else {
            giveUpTheAddress();
        }
    }

    return (
        <>
            {drawTheBar ? (
                <div ref={bar} className="relative shrink-0">
                    <div
                        role="toolbar"
                        aria-label={translate('compose.formattingBar')}
                        className="flex touch-strip flex-wrap items-center gap-y-0.5 border-b border-line-soft px-3 py-1.5 pointer-coarse:gap-1.5 pointer-coarse:py-2.25"
                    >
                        {formattingGroups.map((group, position) => (
                            <Fragment key={group[0]?.key ?? position}>
                                {position === 0 ? null : (
                                    <span
                                        aria-hidden="true"
                                        className="mx-1.25 h-4.5 w-px shrink-0 bg-line pointer-coarse:mx-1 pointer-coarse:h-7"
                                    />
                                )}
                                <span className="flex shrink-0 items-center gap-0.5 pointer-coarse:gap-1.5">
                                    {group.map((control) => (
                                        <FormattingControl
                                            key={control.key}
                                            name={controlName(control)}
                                            expanded={expandedFor(control, address !== null, menu?.kind ?? null)}
                                            opensAMenu={control.opens !== undefined}
                                            marked={control.marked}
                                            onPress={(element) => {
                                                press(control, element);
                                            }}
                                        >
                                            <ControlFace control={control} chosen={chosen} />
                                        </FormattingControl>
                                    ))}
                                </span>
                            </Fragment>
                        ))}

                        {coarse ? null : (
                            <p className="ms-auto ps-3 text-xs text-faint">{translate('compose.formattingHint')}</p>
                        )}
                    </div>

                    {menu === null ? null : (
                        <FormattingMenu
                            key={menu.kind}
                            named={translate(menuNames[menu.kind])}
                            broad={menu.kind === 'colour'}
                            under={menu.under}
                            openedFrom={menu.openedFrom}
                            onClose={closeTheMenu}
                        >
                            {menu.kind === 'colour' ? (
                                <>
                                    <FormattingChoice
                                        named={translate('compose.colourAutomatic')}
                                        chosen={chosen.colour === null}
                                        onChoose={takeTheColourOff}
                                    >
                                        <Icon name="format_color_reset" className="size-icon-lg text-text-soft" />
                                        <span>{translate('compose.colourAutomatic')}</span>
                                        <span className="text-sm whitespace-nowrap text-muted">
                                            {translate('compose.colourAutomaticNote')}
                                        </span>
                                    </FormattingChoice>
                                    <div className="grid grid-cols-2 gap-0.5">
                                        {palette.map((colour) => (
                                            <FormattingChoice
                                                key={colour.value}
                                                named={translate(colour.named)}
                                                chosen={chosen.colour === colour}
                                                onChoose={() => {
                                                    choose({ colour }, 'foreColor', colour.value);
                                                }}
                                            >
                                                <span
                                                    aria-hidden="true"
                                                    className={`size-4.5 shrink-0 rounded-full ring-1 ring-line ring-inset ${colour.ink}`}
                                                />
                                                <span>{translate(colour.named)}</span>
                                            </FormattingChoice>
                                        ))}
                                    </div>
                                </>
                            ) : null}

                            {menu.kind === 'size'
                                ? sizeSteps.map((step) => (
                                      <FormattingChoice
                                          key={step.named}
                                          named={translate(step.named)}
                                          chosen={chosen.size === step}
                                          onChoose={() => {
                                              choose(
                                                  { size: step },
                                                  'fontSize',
                                                  step.size === null ? normalFontSize : fontSizeOf[step.size],
                                              );
                                          }}
                                      >
                                          <span className="text-lg">
                                              <span className={step.drawn}>{translate(step.named)}</span>
                                          </span>
                                      </FormattingChoice>
                                  ))
                                : null}

                            {menu.kind === 'alignment'
                                ? alignmentChoices.map((choice) => (
                                      <FormattingChoice
                                          key={choice.alignment}
                                          named={translate(choice.named)}
                                          chosen={chosen.alignment === choice}
                                          onChoose={() => {
                                              choose({ alignment: choice }, choice.command);
                                          }}
                                      >
                                          <Icon name={choice.icon} className="size-icon-lg text-text-soft" />
                                          <span>{translate(choice.named)}</span>
                                      </FormattingChoice>
                                  ))
                                : null}
                        </FormattingMenu>
                    )}
                </div>
            ) : null}

            {address === null ? null : (
                <div className="flex shrink-0 flex-wrap items-center gap-2.5 border-b border-line-soft px-3.75 py-2.25 focus-within:border-b-accent">
                    <label htmlFor={addressId} className="w-14.5 shrink-0 text-sm text-muted workspace:w-22">
                        {translate('compose.linkAddress')}
                    </label>

                    <input
                        id={addressId}
                        autoFocus
                        value={address}
                        inputMode="url"
                        placeholder={translate('compose.linkAddressPlaceholder')}
                        className="min-w-40 flex-1 border-none bg-transparent text-base text-text outline-none placeholder:text-faint"
                        onChange={(event) => {
                            setAddress(event.target.value);
                        }}
                        onKeyDown={(event) => {
                            if (event.key === 'Enter') {
                                event.preventDefault();
                                insertTheLink();
                            }

                            if (event.key === 'Escape') {
                                event.preventDefault();
                                giveUpTheAddress();
                            }
                        }}
                    />

                    <button
                        type="button"
                        className="shrink-0 rounded-md px-2 py-1 text-sm font-semibold text-accent-deep transition hover:bg-hover"
                        onClick={insertTheLink}
                    >
                        {translate('compose.insertLink')}
                    </button>
                </div>
            )}

            {coarse ? (
                <button
                    type="button"
                    aria-expanded={barShown}
                    className="flex shrink-0 items-center gap-2 border-b border-line-soft px-3.25 py-2 text-base text-text-soft transition hover:bg-hover"
                    onClick={() => {
                        setBarShown(!barShown);
                    }}
                >
                    <Icon name="text_format" className="size-icon-xl" />
                    <span className="min-w-0 flex-1 text-start">
                        {translate(barShown ? 'compose.formattingHide' : 'compose.formatting')}
                    </span>
                    <Icon name={barShown ? 'expand_less' : 'expand_more'} className="size-icon-lg text-muted" />
                </button>
            ) : null}

            {beforeTheWords}

            <div
                ref={words}
                role="textbox"
                aria-multiline="true"
                aria-label={translate('compose.words')}
                data-placeholder={translate('compose.wordsPlaceholder')}
                contentEditable
                suppressContentEditableWarning
                tabIndex={0}
                // A low window lowers the floor so the foot carrying *Send* stays on the screen above a keyboard.
                className="min-h-42.5 flex-1 written-region overflow-auto border-s-2 border-transparent px-4.25 py-4 text-lg text-text-soft outline-none focus-visible:border-accent low:min-h-18"
                onInput={written}
                onPaste={(event) => {
                    // What arrives from somewhere else arrives as text or as a link this client writes itself. A
                    // message carries the formatting its author gave it, and nothing this client did not write reaches
                    // the tree at all.
                    event.preventDefault();
                    closeTheMenu();
                    paste(event.clipboardData.getData('text/plain'));
                }}
                onKeyDown={(event: KeyboardEvent<HTMLDivElement>) => {
                    // The paste event cannot tell Ctrl+Shift+V from Ctrl+V, so the key that raises it says which.
                    pastingPlain.current =
                        (event.ctrlKey || event.metaKey) && event.shiftKey && event.key.toLowerCase() === 'v';

                    // The design's own shortcut, and it opens the confirmation rather than sending: what a keyboard
                    // saves is reaching for the control, never the reading of who the message is for.
                    if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) {
                        event.preventDefault();
                        onSendAsked();
                    }
                }}
            >
                <WrittenTree written={opening} />
            </div>
        </>
    );
}

// What was already written, drawn as ordinary elements once. It is rendered from the closed set rather than from
// markup, which is what keeps a parser off this path: the region's first content is React's, and every reading after
// that goes the other way.
function WrittenTree({ written }: { readonly written: readonly WrittenNode[] }): ReactNode {
    return (
        <>
            {written.map((node, position) => (
                <WrittenPart key={position} node={node} />
            ))}
        </>
    );
}

function WrittenPart({ node }: { readonly node: WrittenNode }): ReactNode {
    if ('text' in node) {
        return node.text;
    }

    if (node.element === 'br') {
        return <br />;
    }

    const holds = <WrittenTree written={node.holds} />;

    // A run's colour and size go back into the region in the editing commands' own spelling, which is what lets the
    // next command merge with them — the module comment above says why.
    if (node.element === 'span') {
        return createElement(
            'font',
            {
                ...(node.colour === undefined ? {} : { color: node.colour }),
                ...(node.size === undefined ? {} : { size: fontSizeOf[node.size] }),
            },
            holds,
        );
    }

    return createElement(
        node.element,
        {
            ...(node.address === null ? {} : { href: node.address }),
            ...(node.alignment === undefined ? {} : { style: { textAlign: drawnAlignment(node.alignment) } }),
        },
        holds,
    );
}

const menuNames: Readonly<Record<FormattingMenuKind, MessageKey>> = {
    colour: 'compose.colourMenu',
    size: 'compose.sizeMenu',
    alignment: 'compose.alignmentMenu',
};

// Whether a control says it has something open: the link its address row, and a menu control its menu. The rest
// carry no such state and say nothing.
function expandedFor(
    control: FormattingControl,
    addressAsked: boolean,
    open: FormattingMenuKind | null,
): boolean | undefined {
    if (control.asksForAnAddress === true) {
        return addressAsked;
    }

    return control.opens === undefined ? undefined : open === control.opens;
}

/**
 * What a control is drawn as: its mark, its symbol, or — for the three that hold a choice — the choice it holds, which
 * is a bar in the colour last chosen under the colour's symbol and the symbol of the alignment last chosen.
 */
function ControlFace({ control, chosen }: { readonly control: FormattingControl; readonly chosen: Chosen }) {
    if (control.opens === 'alignment') {
        return <Icon name={chosen.alignment.icon} className="size-icon-md" />;
    }

    if (control.icon === undefined) {
        return control.mark;
    }

    return (
        <>
            <Icon name={control.icon} className="size-icon-md" />
            {control.opens === 'colour' ? (
                <span
                    aria-hidden="true"
                    className={`absolute bottom-1 left-1/2 h-0.75 w-3.5 -translate-x-1/2 rounded-xs pointer-coarse:bottom-2 pointer-coarse:w-4.5 ${
                        chosen.colour?.ink ?? 'bg-text-soft'
                    }`}
                />
            ) : null}
        </>
    );
}

/** One formatting command, drawn as its mark or its symbol, whose name a mouse resting on it and a finger holding it read. */
function FormattingControl({
    name,
    expanded,
    opensAMenu,
    marked,
    onPress,
    children,
}: {
    readonly name: string;
    readonly expanded: boolean | undefined;
    readonly opensAMenu: boolean;
    readonly marked: string | undefined;
    readonly onPress: (control: HTMLElement) => void;
    readonly children: ReactNode;
}) {
    const drawn = useRef<HTMLElement | null>(null);
    const { anchor, tip } = useTip({
        says: name,
        onPress: () => {
            if (drawn.current !== null) {
                onPress(drawn.current);
            }
        },
    });

    return (
        <>
            <button
                {...anchor}
                ref={(element) => {
                    drawn.current = element;
                    anchor.ref(element);
                }}
                type="button"
                aria-label={name}
                aria-expanded={expanded}
                aria-haspopup={opensAMenu ? 'menu' : undefined}
                className={`relative flex h-7.5 min-w-7.5 items-center justify-center rounded-md px-1.75 text-base transition hover:bg-hover hover:text-text pointer-coarse:border pointer-coarse:border-line ${
                    opensAMenu && expanded === true ? 'bg-accent-soft text-accent-deep' : 'text-text-soft'
                } ${marked ?? ''}`}
                onMouseDown={(event) => {
                    // The selection is what every one of these acts on, and a press that moved the focus would have
                    // collapsed it before the command ran.
                    event.preventDefault();
                }}
            >
                {children}
            </button>
            {tip}
        </>
    );
}
