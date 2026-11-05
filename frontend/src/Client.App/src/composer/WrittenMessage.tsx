// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import {
    createElement,
    Fragment,
    useEffect,
    useId,
    useRef,
    useState,
    type ClipboardEvent,
    type CSSProperties,
    type KeyboardEvent,
    type ReactNode,
} from 'react';
import { newIdentifier } from '../agent/newIdentifier';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { useTip } from '../controls/useTip';
import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';
import { sizeOf } from '../localization/octets';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { useCoarsePointer } from '../shell/useWideWorkspace';
import { edit, insertWritten } from './editing';
import { FormattingChoice, FormattingMenu } from './FormattingMenu';
import type { PictureAddresses } from './pictureAddresses';
import {
    mostPictureOctets,
    mostPictures,
    mostPicturesOctets,
    pictureIn,
    pictureTypeOf,
    refusalOfAdding,
    widestSentPicture,
    type HeldPicture,
    type PictureRefusal,
} from './pictures';
import { drawingOf } from './regionDrawing';
import { TableInsertion, TableStrip, type TableAct } from './TableControls';
import {
    cellAfter,
    cellBefore,
    newTable,
    withColumnInserted,
    withColumnRemoved,
    withRowAppended,
    withRowInserted,
    withRowRemoved,
    type TableCellPosition,
    type TableEdit,
} from './writtenTable';
import {
    cellDeclarationsOf,
    drawnAlignment,
    followable,
    fontSizeOf,
    htmlOf,
    pastedAddress,
    picturesNamedIn,
    plainTextOf,
    sentSizeOf,
    writtenIn,
    writtenTableIn,
    type PictureSource,
    type RunDrawing,
    type WrittenAlignment,
    type WrittenMarkup,
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
//
// **A table is edited as a tree, never in place.** `execCommand` has no table editing, so every table control — and
// Tab past the last cell — reads the caret's table into the closed set, asks `writtenTable.ts` for the table that
// replaces it, and writes that over the old one through `insertWritten`, which keeps the edit one step on the engine's
// own undo stack. ADR 0047's Question 5 is the decision.
//
// **A picture is pasted, dropped, chosen, and removed here**, and held by the composer above: this component reads it,
// judges it against ADR 0047's bounds, asks the composer to hold it, and inserts it through `insertWritten` drawn from
// the object URL `PictureAddresses` issued for it — the one source `writtenIn` reads back as a picture.

// The flavour a copy inside the message carries beside its HTML and its text: a token naming what this composer copied,
// so a paste back into it resolves the pictures it holds by name. Only the composer that copied knows the token, so a
// paste in another tab, or of anything else, matches nothing and is read as any other paste.
const writtenFlavour = 'application/x-mailfathom-written';

/** A picture refused at the paste, and what it was called, which is what the line saying so names. */
interface RefusedPicture {
    readonly refusal:
        | PictureRefusal
        | { readonly reason: 'inACell' }
        | { readonly reason: 'tooManyInThePaste'; readonly carried: number; readonly held: number };
    readonly name: string | null;
}

type FormattingMenuKind = 'colour' | 'size' | 'alignment' | 'table';

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

    /** The menu of choices it opens instead of acting, for the four that take one. */
    readonly opens?: FormattingMenuKind;

    /** Whether it makes a block a table cell cannot hold, so it stands unavailable while the caret is in one. */
    readonly notInACell?: true;
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
        {
            key: 'bulleted',
            command: 'insertUnorderedList',
            named: 'compose.formatBulletedList',
            mark: '•—',
            notInACell: true,
        },
        {
            key: 'numbered',
            command: 'insertOrderedList',
            named: 'compose.formatNumberedList',
            mark: '1.',
            notInACell: true,
        },
        {
            key: 'quote',
            command: 'formatBlock',
            argument: 'blockquote',
            named: 'compose.formatQuote',
            icon: 'format_quote',
            notInACell: true,
        },
    ],
    [
        { key: 'link', command: 'createLink', named: 'compose.formatLink', icon: 'link', asksForAnAddress: true },
        { key: 'table', named: 'compose.formatTable', icon: 'table', opens: 'table', notInACell: true },
    ],
    [{ key: 'clear', command: 'removeFormat', named: 'compose.formatClear', icon: 'format_clear' }],
];

/** One swatch of the palette ADR 0047 states: the value a message is sent with, and the token it is drawn in here. */
interface PaletteColour {
    readonly value: string;
    readonly named: MessageKey;

    /** The utility a swatch is filled with, and the token behind it that the region draws the colour in. */
    readonly ink: string;
    readonly token: string;
}

const palette: readonly PaletteColour[] = [
    { value: '#b42318', named: 'compose.colourRed', ink: 'bg-ink-red', token: '--color-ink-red' },
    { value: '#b54708', named: 'compose.colourOrange', ink: 'bg-ink-orange', token: '--color-ink-orange' },
    { value: '#8a6100', named: 'compose.colourAmber', ink: 'bg-ink-amber', token: '--color-ink-amber' },
    { value: '#067647', named: 'compose.colourGreen', ink: 'bg-ink-green', token: '--color-ink-green' },
    { value: '#0e7490', named: 'compose.colourTeal', ink: 'bg-ink-teal', token: '--color-ink-teal' },
    { value: '#175cd3', named: 'compose.colourBlue', ink: 'bg-ink-blue', token: '--color-ink-blue' },
    { value: '#6941c6', named: 'compose.colourPurple', ink: 'bg-ink-purple', token: '--color-ink-purple' },
    { value: '#c11574', named: 'compose.colourPink', ink: 'bg-ink-pink', token: '--color-ink-pink' },
    { value: '#667085', named: 'compose.colourGrey', ink: 'bg-ink-grey', token: '--color-ink-grey' },
];

/** One step of the size scale as the menu offers it, normal being the absence of a step. */
interface SizeStep {
    readonly size: WrittenSize | null;
    readonly named: MessageKey;

    /** The step the name is drawn at, so each choice shows the size it gives, and the token that step is. */
    readonly drawn: string;
    readonly token: string | null;
}

const normalStep: SizeStep = { size: null, named: 'compose.sizeNormal', drawn: '', token: null };

const sizeSteps: readonly SizeStep[] = [
    { size: 'small', named: 'compose.sizeSmall', drawn: 'text-step-small', token: '--text-step-small' },
    normalStep,
    { size: 'large', named: 'compose.sizeLarge', drawn: 'text-step-large', token: '--text-step-large' },
    { size: 'larger', named: 'compose.sizeLarger', drawn: 'text-step-larger', token: '--text-step-larger' },
    { size: 'largest', named: 'compose.sizeLargest', drawn: 'text-step-largest', token: '--text-step-largest' },
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
    pictures,
    addresses,
    picturesKeptHereOnly,
    onWritten,
    onPictureHeld,
    onSendAsked,
    beforeTheWords,
}: {
    /** What the region opens holding, read once: a message this tab was already writing, or nothing. */
    readonly opened: readonly WrittenNode[];

    /** The pictures the composer holds for this message, by the name the words refer to each with. */
    readonly pictures: ReadonlyMap<string, HeldPicture>;

    /** The object URLs the composer draws its pictures from, which outlive this region when the words are replaced. */
    readonly addresses: PictureAddresses;

    /** Whether the session store would not take the pictures, so a reload would lose them, which is said above the words. */
    readonly picturesKeptHereOnly: boolean;

    readonly onWritten: (written: readonly WrittenNode[]) => void;

    /** Asks the composer to hold a picture pasted or dropped here, which it does before the words name it. */
    readonly onPictureHeld: (name: string, picture: HeldPicture) => void;

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
    const { locale, translate } = useLocalization();
    const coarse = useCoarsePointer();
    const [opening] = useState(opened);
    const [barShown, setBarShown] = useState(false);
    const [address, setAddress] = useState<string | null>(null);
    const [menu, setMenu] = useState<OpenMenu | null>(null);
    const [chosen, setChosen] = useState<Chosen>({ colour: null, size: normalStep, alignment: startAlignment });
    const [inACell, setInACell] = useState(false);
    const [refused, setRefused] = useState<RefusedPicture | null>(null);
    const [pictureChosen, setPictureChosen] = useState<string | null>(null);
    const words = useRef<HTMLDivElement>(null);
    const bar = useRef<HTMLDivElement>(null);
    const holding = useRef<Range | null>(null);
    const pastingPlain = useRef(false);
    const draggingHere = useRef(false);
    const chosenPicture = useRef<HTMLImageElement | null>(null);
    const copied = useRef<{ readonly token: string; readonly written: readonly WrittenNode[] } | null>(null);
    const addressId = useId();

    // What the design decides about the bar: drawn outright where a pointer is fine, and behind a toggle where a
    // finger is what drives the screen — the keyboard and the message needing the room a bar would take.
    const drawTheBar = !coarse || barShown;
    const chosenHeld = pictureChosen === null ? undefined : pictures.get(pictureChosen);

    // Where a picture in the editor is drawn from — the object URL issued for it, which a picture never drawn yet is
    // issued the first time the tree it is in is drawn — and what a screen reader names it by.
    function sourceAmong(held: ReadonlyMap<string, HeldPicture>): PictureSource {
        return (name) => {
            const picture = held.get(name);

            return picture === undefined
                ? { address: addresses.addressOf(name) ?? '', label: translate('compose.pictureWithoutAName') }
                : {
                      address: addresses.issue(name, picture),
                      label: picture.label ?? translate('compose.pictureWithoutAName'),
                  };
        };
    }

    const pictureSource = sourceAmong(pictures);

    // Where the caret stands decides whether the table's strip stands, and the selection is the document's, so it is
    // followed from there rather than from whichever of the region's events happened to move it.
    useEffect(() => {
        function caretMoved(): void {
            setInACell(cellAtTheCaret(words.current) !== null);
        }

        document.addEventListener('selectionchange', caretMoved);

        return () => {
            document.removeEventListener('selectionchange', caretMoved);
        };
    }, []);

    function written(): void {
        if (words.current !== null) {
            onWritten(writtenIn(words.current, addresses.pictureAt, drawingOfRegion(words.current)));
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

    // The selection a control took the focus away from, put back before a command reads it. Only the link and the
    // four menus ask for anything, so this is null for the rest and they act on what the region still holds.
    function takeBackTheSelection(): void {
        words.current?.focus();

        const held = holding.current;

        if (held !== null) {
            const selection = window.getSelection();

            selection?.removeAllRanges();
            selection?.addRange(held);
            holding.current = null;
        }
    }

    function apply(command: string, argument?: string): void {
        takeBackTheSelection();

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
    // makes a link, what this composer copied comes back as it was, picture files with nothing formatted beside them
    // are pictures, and anything else is pasted as text. A table cell holds no block a second line would start and no
    // picture, so a paste there lands as its words on one line, and a picture with no words beside it is refused.
    function paste(data: DataTransfer): void {
        const plain = data.getData('text/plain');
        const plainOnly = pastingPlain.current;
        const address = plainOnly ? null : pastedAddress(plain);
        const copiedHere = copied.current;
        const files = picturesWithNothingFormatted(data, plain);
        const intoACell = cellAtTheCaret(words.current) !== null;

        pastingPlain.current = false;
        unchoosePicture();
        setRefused(null);

        if (address !== null) {
            pasteTheLink(address);
        } else if (intoACell && plain.trim() === '' && files.length > 0) {
            void pastePictures(files);
        } else if (intoACell) {
            apply('insertText', plain.replace(/\s*[\r\n\t]+\s*/gu, ' '));
        } else if (plainOnly) {
            apply('insertText', plain);
        } else if (copiedHere !== null && data.getData(writtenFlavour) === copiedHere.token) {
            pasteBack(copiedHere.written);
        } else if (files.length > 0) {
            void pastePictures(files);
        } else {
            apply('insertText', plain);
        }
    }

    function pasteTheLink(address: string): void {
        if (window.getSelection()?.isCollapsed === false) {
            // The selected words become the link and keep their own formatting, and a link they sat in is replaced.
            apply('createLink', address);
        } else {
            // A space after the link, so what is typed next is not part of it.
            insertWritten([{ element: 'a', address, holds: [{ text: address }] }, { text: '\u00a0' }], pictureSource);
            written();
        }
    }

    // A passage this composer copied, put back with its pictures by name. A picture it names that the words no longer
    // do is one being added, so the passage meets the bounds a paste of files meets and is refused whole past one.
    function pasteBack(passage: readonly WrittenNode[]): void {
        const region = words.current;
        const named = region === null ? [] : picturesNamedIn(writtenIn(region, addresses.pictureAt));
        const heldOf = (name: string) => pictures.get(name) ?? [];
        const refusal = refusalOfAddingAll(
            picturesNamedIn(passage)
                .filter((name) => !named.includes(name))
                .flatMap(heldOf),
            named.flatMap(heldOf),
        );

        if (refusal !== null) {
            setRefused(refusal);

            return;
        }

        insertWritten(passage, pictureSource);
        written();
    }

    // Each file judged against the bounds in turn, together with the pictures the words already name, and inserted
    // all at once as one step somebody can undo — or none of them, and the line above the words says which was refused
    // and why: a paste is refused whole and never cut.
    async function pastePictures(files: readonly File[]): Promise<void> {
        const region = words.current;
        const selection = window.getSelection();
        const at = selection !== null && selection.rangeCount > 0 ? selection.getRangeAt(0).cloneRange() : null;

        if (region === null) {
            return;
        }

        if (cellAtTheCaret(region) !== null) {
            setRefused({ refusal: { reason: 'inACell' }, name: null });

            return;
        }

        const held = picturesNamedIn(writtenIn(region, addresses.pictureAt)).flatMap(
            (name) => pictures.get(name) ?? [],
        );
        const tooMany = refusalOfTheCount(files.length, held.length);

        if (tooMany !== null) {
            setRefused(tooMany);

            return;
        }

        const adding: { readonly name: string; readonly picture: HeldPicture }[] = [];

        for (const file of files) {
            const label = file.name === '' ? null : file.name;
            const outcome = await pictureFrom(file, [...held, ...adding.map(({ picture }) => picture)]);

            if ('refusal' in outcome) {
                setRefused({ refusal: outcome.refusal, name: label });

                return;
            }

            adding.push({ name: newIdentifier(), picture: { ...outcome.picture, label } });
        }

        // Reading the files gave the page time to move the caret, so it goes back where the paste was asked for.
        region.focus();

        if (at !== null) {
            selection?.removeAllRanges();
            selection?.addRange(at);
        }

        for (const { name, picture } of adding) {
            onPictureHeld(name, picture);
            addresses.issue(name, picture);
        }

        insertWritten(
            adding.map(({ name, picture }) => ({
                element: 'img' as const,
                picture: name,
                width: picture.width,
                height: picture.height,
            })),
            // The pictures are only now asked to be held, so the composer's own map does not name them yet.
            sourceAmong(new Map([...pictures, ...adding.map(({ name, picture }) => [name, picture] as const)])),
        );
        written();
    }

    // A drop lands where the pointer let go, so the caret is put there before it is read like a paste.
    function dropAt(x: number, y: number): void {
        // An engine without the standard call keeps the caret where it was, which is where the drop then lands.
        const position = 'caretPositionFromPoint' in document ? document.caretPositionFromPoint(x, y) : null;
        const selection = window.getSelection();

        if (position !== null && selection !== null) {
            selection.collapse(position.offsetNode, position.offset);
        }
    }

    // A press on a picture chooses it: outlined, its strip shown, and selected as one node, so Delete and Backspace
    // take it out as the engine's own one step. A press anywhere else puts the choice away.
    function choosePicture(pressed: EventTarget): void {
        const picture =
            pressed instanceof HTMLImageElement ? addresses.pictureAt(pressed.getAttribute('src') ?? '') : null;

        unchoosePicture();

        if (picture === null || !(pressed instanceof HTMLImageElement)) {
            return;
        }

        const range = document.createRange();
        const selection = window.getSelection();

        range.selectNode(pressed);
        selection?.removeAllRanges();
        selection?.addRange(range);
        pressed.setAttribute('data-chosen', '');
        chosenPicture.current = pressed;
        setPictureChosen(picture.picture);
    }

    function unchoosePicture(): void {
        chosenPicture.current?.removeAttribute('data-chosen');
        chosenPicture.current = null;
        setPictureChosen(null);
    }

    function removeTheChosenPicture(): void {
        const picture = chosenPicture.current;

        unchoosePicture();

        if (picture === null) {
            return;
        }

        words.current?.focus();

        const range = document.createRange();
        const selection = window.getSelection();

        range.selectNode(picture);
        selection?.removeAllRanges();
        selection?.addRange(range);
        edit('delete');
        written();
    }

    // A copy or a cut of a passage holding a picture is written by the composer itself, so that pasting it back into
    // this message brings the picture back by name. A copy holding none is left to the engine, as it always was.
    function copy(event: ClipboardEvent<HTMLDivElement>, cutting: boolean): void {
        const selection = window.getSelection();

        if (selection === null || selection.rangeCount === 0 || selection.isCollapsed) {
            return;
        }

        const region = words.current;
        const passage = writtenIn(
            selection.getRangeAt(0).cloneContents(),
            addresses.pictureAt,
            region === null ? null : drawingOfRegion(region),
        );

        if (picturesNamedIn(passage).length === 0) {
            return;
        }

        const token = newIdentifier();

        event.preventDefault();
        copied.current = { token, written: passage };
        event.clipboardData.setData('text/plain', plainTextOf(passage));
        event.clipboardData.setData('text/html', htmlOf(passage, pictureSource));
        event.clipboardData.setData(writtenFlavour, token);

        if (cutting) {
            unchoosePicture();
            edit('delete');
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

    // The same control again closes its menu, focus going back to it from the choice it stood on; any other opens in
    // its place, under the control that asked for it.
    function openTheMenu(kind: FormattingMenuKind, control: HTMLElement): void {
        if (menu?.kind === kind) {
            closeTheMenu();
            control.focus();

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
    // already drawn in, an engine finds nothing to change and removes the `<font color>` it wrote — Chromium does — and
    // one that wrote a `<font>` anyway wrote the region's own colour, which the region's drawing reads as no colour.
    function takeTheColourOff(): void {
        const surrounding = words.current === null ? '' : getComputedStyle(words.current).color;

        choose({ colour: null }, 'foreColor', surrounding);
    }

    function controlName(control: FormattingControl): string {
        const named = ownName(control);

        return inACell && control.notInACell === true ? translate('compose.formatInACell', { control: named }) : named;
    }

    function ownName(control: FormattingControl): string {
        if (control.opens === undefined) {
            return translate(control.named);
        }

        const holds: Readonly<Record<FormattingMenuKind, Readonly<Record<string, string>>>> = {
            colour: { colour: translate(chosen.colour?.named ?? 'compose.colourAutomatic') },
            size: { size: translate(chosen.size.named) },
            alignment: { alignment: translate(chosen.alignment.named) },
            table: {},
        };

        return translate(control.named, holds[control.opens]);
    }

    function press(control: FormattingControl, element: HTMLElement): void {
        // A control making a block a cell cannot hold does nothing there, and its name says why.
        if (inACell && control.notInACell === true) {
            return;
        }

        if (control.opens !== undefined) {
            openTheMenu(control.opens, element);
        } else if (control.asksForAnAddress === true) {
            askForAnAddress();
        } else if (control.command !== undefined) {
            apply(control.command, control.argument);
        }
    }

    // What a table edit leaves, written over the old table — or, with no table left, the old one taken away — as one
    // step on the engine's undo stack, and the caret put in the cell the edit names.
    function replaceTheTable(table: HTMLTableElement, edited: TableEdit): void {
        const region = words.current;

        if (region === null) {
            return;
        }

        region.focus();

        const range = document.createRange();
        const selection = window.getSelection();

        range.selectNode(table);
        selection?.removeAllRanges();
        selection?.addRange(range);

        if (edited.table === null) {
            edit('delete');
            written();
        } else {
            putTheTable(region, edited.table, [], edited.caret);
        }
    }

    // A table written where the selection is, followed by whatever the caller adds after it, with the caret put in
    // the cell named. The new table is the one the region did not hold before, which is how it is found whatever an
    // engine wrapped it in.
    function putTheTable(
        region: HTMLElement,
        table: WrittenMarkup,
        after: readonly WrittenNode[],
        caret: TableCellPosition | null,
    ): void {
        const standing = new Set(region.querySelectorAll('table'));

        insertWritten([table, ...after], pictureSource);

        const placed = [...region.querySelectorAll('table')].find((each) => !standing.has(each));
        const cell = placed === undefined || caret === null ? null : cellAt(placed, caret);

        if (cell !== null) {
            placeTheCaretIn(cell);
        }

        written();
    }

    // A new table at the caret, or in place of the selection, with an empty paragraph after it so there is a line to
    // carry on writing on, and the caret in its first cell.
    function insertTheTable(rows: number, columns: number, headed: boolean): void {
        setMenu(null);
        takeBackTheSelection();

        if (words.current !== null) {
            putTheTable(
                words.current,
                newTable(rows, columns, headed),
                [{ element: 'p', address: null, holds: [{ element: 'br', address: null, holds: [] }] }],
                { row: 0, cell: 0 },
            );
        }
    }

    function actOnTheTable(act: TableAct): void {
        const around = tableAroundTheCaret(words.current);

        if (around === null) {
            return;
        }

        const edited = tableEdits[act](around.written, around.at);

        // An edit past a bound answers the same table, and writing that back would only add a step to undo.
        if (edited.table !== around.written) {
            replaceTheTable(around.element, edited);
        }
    }

    // Tab and Shift+Tab walk the cells, and Tab past the last adds a row. Shift+Tab before the first leaves the table
    // the way it leaves anything, which is the one answer here that is the browser's rather than the table's.
    function moveAcrossTheTable(backwards: boolean): boolean {
        const around = tableAroundTheCaret(words.current);

        if (around === null) {
            return false;
        }

        const next = backwards ? cellBefore(around.written, around.at) : cellAfter(around.written, around.at);

        if (next !== null) {
            const cell = cellAt(around.element, next);

            if (cell !== null) {
                placeTheCaretIn(cell);
            }

            return true;
        }

        if (backwards) {
            return false;
        }

        const appended = withRowAppended(around.written);

        if (appended.table !== around.written) {
            replaceTheTable(around.element, appended);
        }

        return true;
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
                        className="flex touch-strip items-center gap-y-0.5 border-b border-line-soft px-3 py-1.5 pointer-coarse:gap-1.5 pointer-coarse:py-2.25"
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
                                            unavailable={inACell && control.notInACell === true}
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
                            width={menuWidths[menu.kind]}
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

                            {menu.kind === 'table' ? <TableInsertion onInsert={insertTheTable} /> : null}
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

            {inACell ? <TableStrip onAct={actOnTheTable} /> : null}

            {pictureChosen === null || chosenHeld === undefined ? null : (
                <div
                    role="toolbar"
                    aria-label={translate('compose.picture')}
                    className="flex shrink-0 flex-wrap items-center gap-0.5 border-b border-line-soft bg-sunken px-3 py-1.25 pointer-coarse:gap-1.5 pointer-coarse:py-2"
                >
                    <span className="shrink-0 ps-0.5 pe-2 text-sm font-semibold text-muted">
                        {translate('compose.picture')}
                    </span>
                    <span className="min-w-0 flex-1 truncate pe-2 text-sm text-text-soft">
                        {translate('compose.pictureDescribed', {
                            name: chosenHeld.label ?? translate('compose.pictureUnnamed'),
                            size: sizeOf(chosenHeld.octets.length, locale),
                            width: new Intl.NumberFormat(locale).format(widestSentPicture),
                        })}
                    </span>
                    <button
                        type="button"
                        className="flex min-h-7 shrink-0 items-center gap-1.5 rounded-md px-2.25 text-sm whitespace-nowrap text-error-text transition hover:bg-hover pointer-coarse:border pointer-coarse:border-line pointer-coarse:px-3 pointer-coarse:text-base"
                        onMouseDown={(event) => {
                            // The picture stays selected while the control is pressed, as the formatting controls keep
                            // the selection they act on.
                            event.preventDefault();
                        }}
                        onClick={removeTheChosenPicture}
                    >
                        <Icon name="delete" className="size-icon-sm pointer-coarse:size-icon-lg" />
                        {translate('compose.removePicture')}
                    </button>
                </div>
            )}

            {refused === null ? null : (
                <div
                    role="alert"
                    className="flex shrink-0 animate-disclose-in items-start gap-2.25 border-b border-warning bg-warning-soft py-2 ps-3.75 pe-3 text-sm text-warning-text pointer-coarse:py-2.5 pointer-coarse:text-base"
                >
                    <Icon name="block" className="size-icon-lg shrink-0" />
                    <span className="min-w-0 flex-1 text-pretty">{refusalSentence(refused, locale, translate)}</span>
                    <button
                        type="button"
                        aria-label={translate('compose.dismissPictureRefusal')}
                        className="-my-0.5 flex size-6 shrink-0 items-center justify-center rounded-md transition hover:bg-hover"
                        onClick={() => {
                            setRefused(null);
                            // The control goes with the line, so focus goes back to the words it was about.
                            words.current?.focus();
                        }}
                    >
                        <Icon name="close" className="size-icon-lg" />
                    </button>
                </div>
            )}

            {picturesKeptHereOnly ? (
                <div
                    role="status"
                    className="flex shrink-0 animate-disclose-in items-start gap-2.25 border-b border-warning bg-warning-soft px-3.75 py-2 text-sm text-warning-text pointer-coarse:py-2.5 pointer-coarse:text-base"
                >
                    <Icon name="memory" className="size-icon-lg shrink-0" />
                    <span className="min-w-0 flex-1 text-pretty">{translate('compose.picturesInThisWindowOnly')}</span>
                </div>
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
                // A picture is drawn on a line of its own at its own size, fitted to the editor's width with its height
                // scaled — the width it is written at is the message's, not the editor's — and outlined while it is the
                // one chosen, the outline rather than the engine's selection tint saying so.
                className="min-h-42.5 flex-1 written-region overflow-auto border-s-2 border-transparent px-4.25 py-4 text-lg text-text-soft outline-none focus-visible:border-accent low:min-h-18 [&_img]:my-2 [&_img]:block [&_img]:h-auto [&_img]:w-auto [&_img]:max-w-full [&_img]:selection:bg-transparent [&_img]:cursor-pointer [&_img[data-chosen]]:outline-2 [&_img[data-chosen]]:outline-accent"
                onInput={() => {
                    unchoosePicture();
                    written();
                }}
                onClick={(event) => {
                    choosePicture(event.target);
                }}
                onPaste={(event) => {
                    // What arrives from somewhere else arrives as text, as a link, or as a picture this client writes
                    // itself. A message carries the formatting its author gave it, and nothing this client did not
                    // write reaches the tree at all.
                    event.preventDefault();
                    closeTheMenu();
                    paste(event.clipboardData);
                }}
                onCopy={(event) => {
                    copy(event, false);
                }}
                onCut={(event) => {
                    copy(event, true);
                }}
                onDragStart={() => {
                    draggingHere.current = true;
                }}
                onDragEnd={() => {
                    draggingHere.current = false;
                }}
                onDrop={(event) => {
                    // A drop carrying files or markup from somewhere else goes through the same order a paste does, so
                    // nothing another page wrote reaches the region. A passage dragged within the words is the
                    // engine's own move, read back by `writtenIn` like everything else written here.
                    const transfer = event.dataTransfer;
                    const fromElsewhere = !draggingHere.current;

                    draggingHere.current = false;

                    // A picture moved into a table cell would stay drawn there and be read as nothing, a cell holding
                    // no picture, so the move is refused with the line a paste there gets.
                    if (!fromElsewhere && cellUnder(words.current, event.target) && pictureSelected()) {
                        event.preventDefault();
                        unchoosePicture();
                        setRefused({ refusal: { reason: 'inACell' }, name: null });

                        return;
                    }

                    if (fromElsewhere && (transfer.files.length > 0 || transfer.types.includes('text/html'))) {
                        event.preventDefault();
                        closeTheMenu();
                        dropAt(event.clientX, event.clientY);
                        pastingPlain.current = false;
                        paste(transfer);
                    }
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

                        return;
                    }

                    const plainKey = !event.ctrlKey && !event.metaKey && !event.altKey;

                    if (event.key === 'Tab' && plainKey && moveAcrossTheTable(event.shiftKey)) {
                        event.preventDefault();
                    }

                    // Enter in a cell is a line in that cell, since a paragraph is a block no cell holds.
                    if (
                        event.key === 'Enter' &&
                        plainKey &&
                        !event.shiftKey &&
                        cellAtTheCaret(words.current) !== null
                    ) {
                        event.preventDefault();
                        apply('insertLineBreak');
                    }
                }}
            >
                <WrittenTree written={opening} pictureSource={pictureSource} />
            </div>
        </>
    );
}

// The picture files a paste or a drop carries, where nothing formatted stands beside them — ADR 0047's step 4. Until the
// HTML flavour is read into the tree, a formatted flavour is taken to read to something wherever words come with it:
// what Excel and Word put beside their HTML is the same content drawn again, and a browser's *Copy image* puts a
// picture beside markup that carries no words at all, which is the one case the picture is the paste.
function picturesWithNothingFormatted(data: DataTransfer, plain: string): readonly File[] {
    return data.types.includes('text/html') && plain.trim() !== '' ? [] : [...data.files];
}

// Whether what a drop landed on stands in a table cell of the words.
function cellUnder(region: HTMLElement | null, target: EventTarget): boolean {
    return target instanceof Element && region?.contains(target) === true && target.closest('td, th') !== null;
}

// Whether what is selected — which is what a drag within the words carries — holds a picture.
function pictureSelected(): boolean {
    const selection = window.getSelection();

    return (
        selection !== null &&
        selection.rangeCount > 0 &&
        selection.getRangeAt(0).cloneContents().querySelector('img') !== null
    );
}

// A paste of several pictures that would take the message past the count is refused as the paste, since naming the one
// that crossed the bound would read as though the ones before it went in.
function refusalOfTheCount(carried: number, held: number): RefusedPicture | null {
    return carried > 1 && held + carried > mostPictures
        ? { refusal: { reason: 'tooManyInThePaste', carried, held }, name: null }
        : null;
}

// Pictures the composer already holds, judged in turn against the bounds together with those the words name, as a paste
// of files is: the first one past a bound names the refusal.
function refusalOfAddingAll(adding: readonly HeldPicture[], held: readonly HeldPicture[]): RefusedPicture | null {
    const tooMany = refusalOfTheCount(adding.length, held.length);

    if (tooMany !== null) {
        return tooMany;
    }

    const together = [...held];

    for (const picture of adding) {
        const refusal = refusalOfAdding(picture.octets.length, together);

        if (refusal !== null) {
            return { refusal, name: picture.label };
        }

        together.push(picture);
    }

    return null;
}

// A file as a picture the composer may hold, or why it may not: its kind read from the octets it opens with, then the
// bounds, then the whole file read for its size — so a file too large to take is never read whole to be refused.
async function pictureFrom(
    file: File,
    held: readonly HeldPicture[],
): Promise<{ readonly picture: Omit<HeldPicture, 'label'> } | { readonly refusal: PictureRefusal }> {
    const notTaken = { refusal: { reason: 'notTaken', kind: kindOf(file.type) } } as const;

    // A file that cannot be read — a dropped folder, or one moved since it was dropped — is one the composer does not
    // take, and is refused as one rather than failing in silence.
    try {
        if (pictureTypeOf(new Uint8Array(await file.slice(0, 8).arrayBuffer())) === null) {
            return notTaken;
        }

        const refusal = refusalOfAdding(file.size, held);

        if (refusal !== null) {
            return { refusal };
        }

        const octets = new Uint8Array(await file.arrayBuffer());
        const read = pictureIn(octets);

        return read === null ? notTaken : { picture: { octets, ...read } };
    } catch {
        return notTaken;
    }
}

// What a file the composer does not take says it is, from the media type the platform reported: `image/heic` is HEIC.
function kindOf(type: string): string | null {
    const kind = /^[a-z]+\/([a-z0-9.-]+)/iu.exec(type)?.[1];

    return kind === undefined ? null : kind.toUpperCase();
}

function refusalSentence(refused: RefusedPicture, locale: Locale, translate: Translate): string {
    const name = refused.name ?? translate('compose.pictureUnnamed');
    const { refusal } = refused;

    switch (refusal.reason) {
        case 'inACell':
            return translate('compose.pictureInACell');
        case 'tooManyInThePaste': {
            const counted = new Intl.NumberFormat(locale);

            return translate('compose.picturesTooManyInThePaste', {
                carried: counted.format(refusal.carried),
                held: counted.format(refusal.held),
                most: counted.format(mostPictures),
            });
        }
        case 'notTaken':
            return refusal.kind === null
                ? translate('compose.pictureKindUnknown', { name })
                : translate('compose.pictureKindNotTaken', { name, kind: refusal.kind });
        case 'tooLarge':
            return translate('compose.pictureTooLarge', {
                name,
                size: sizeOf(refusal.octets, locale),
                most: sizeOf(mostPictureOctets, locale),
            });
        case 'tooMany':
            return translate('compose.pictureOneTooMany', {
                name,
                count: new Intl.NumberFormat(locale).format(mostPictures),
            });
        case 'tooMuchTogether':
            return translate('compose.picturesTooMuchTogether', {
                name,
                size: sizeOf(refusal.octets, locale),
                most: sizeOf(mostPicturesOctets, locale),
            });
    }
}

// What was already written, drawn as ordinary elements once. It is rendered from the closed set rather than from
// markup, which is what keeps a parser off this path: the region's first content is React's, and every reading after
// that goes the other way.
function WrittenTree({
    written,
    pictureSource,
}: {
    readonly written: readonly WrittenNode[];
    readonly pictureSource: PictureSource;
}): ReactNode {
    return (
        <>
            {written.map((node, position) => (
                <WrittenPart key={position} node={node} pictureSource={pictureSource} />
            ))}
        </>
    );
}

function WrittenPart({
    node,
    pictureSource,
}: {
    readonly node: WrittenNode;
    readonly pictureSource: PictureSource;
}): ReactNode {
    if ('text' in node) {
        return node.text;
    }

    if (node.element === 'img') {
        // At the size the message is sent at and named as `htmlOf` names it for the editor, so a reopened picture and a
        // pasted one are drawn the same way and read as the same picture.
        const { width, height } = sentSizeOf(node);
        const { address, label } = pictureSource(node.picture);

        return <img src={address} alt={label ?? ''} width={width} height={height} />;
    }

    if (node.element === 'br') {
        return <br />;
    }

    const holds = <WrittenTree written={node.holds} pictureSource={pictureSource} />;

    if (node.element === 'table') {
        return (
            <table style={{ borderCollapse: 'collapse' }}>
                <tbody>{holds}</tbody>
            </table>
        );
    }

    // A cell is drawn with the declarations the HTML part writes, and an empty one, or one ending on a break, with
    // the break an engine needs to put the caret on — `htmlOf`'s editor spelling, in React's.
    if (node.element === 'td' || node.element === 'th') {
        const last = node.holds[node.holds.length - 1];

        return createElement(
            node.element,
            {
                style: drawnDeclarations(cellDeclarationsOf(node)),
                ...(node.columnSpan === undefined ? {} : { colSpan: node.columnSpan }),
                ...(node.rowSpan === undefined ? {} : { rowSpan: node.rowSpan }),
            },
            holds,
            last === undefined || (!('text' in last) && last.element === 'br') ? <br /> : null,
        );
    }

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

// The region as it is drawn now, read when the region is read so a theme switched since is the theme read.
function drawingOfRegion(region: HTMLElement): RunDrawing {
    const tokens = getComputedStyle(document.documentElement);
    const drawn = getComputedStyle(region);

    return drawingOf(
        { colour: drawn.color, fontSize: drawn.fontSize, token: (name) => tokens.getPropertyValue(name) },
        palette,
        sizeSteps,
    );
}

const menuNames: Readonly<Record<FormattingMenuKind, MessageKey>> = {
    colour: 'compose.colourMenu',
    size: 'compose.sizeMenu',
    alignment: 'compose.alignmentMenu',
    table: 'compose.tableMenu',
};

const menuWidths: Readonly<Record<FormattingMenuKind, 'broad' | 'table' | 'narrow'>> = {
    colour: 'broad',
    size: 'narrow',
    alignment: 'narrow',
    table: 'table',
};

// What each of the strip's controls asks `writtenTable.ts` for.
const tableEdits: Readonly<Record<TableAct, (table: WrittenMarkup, at: TableCellPosition) => TableEdit>> = {
    rowAbove: (table, at) => withRowInserted(table, at, 'before'),
    rowBelow: (table, at) => withRowInserted(table, at, 'after'),
    columnBefore: (table, at) => withColumnInserted(table, at, 'before'),
    columnAfter: (table, at) => withColumnInserted(table, at, 'after'),
    removeRow: withRowRemoved,
    removeColumn: withColumnRemoved,
    delete: () => ({ table: null, caret: null }),
};

// The cell the caret stands in, where it stands in one of the region's own tables.
function cellAtTheCaret(region: HTMLElement | null): HTMLTableCellElement | null {
    const anchor = window.getSelection()?.anchorNode ?? null;

    if (region === null || anchor === null || !region.contains(anchor)) {
        return null;
    }

    const cell = (anchor instanceof Element ? anchor : anchor.parentElement)?.closest('td, th') ?? null;

    return cell instanceof HTMLTableCellElement && region.contains(cell) ? cell : null;
}

/** The caret's table, in the document and as the tree reads it, and where its cell stands in that tree. */
interface TableAroundTheCaret {
    readonly element: HTMLTableElement;
    readonly written: WrittenMarkup;
    readonly at: TableCellPosition;
}

function tableAroundTheCaret(region: HTMLElement | null): TableAroundTheCaret | null {
    const cell = cellAtTheCaret(region);
    const element = cell?.closest('table') ?? null;
    const row = cell?.parentElement ?? null;

    if (cell === null || !(element instanceof HTMLTableElement) || !(row instanceof HTMLTableRowElement)) {
        return null;
    }

    const written = region === null ? null : writtenTableIn(element, drawingOfRegion(region));
    const rowIndex = rowsWithCells(element).indexOf(row);

    return written === null || rowIndex < 0 ? null : { element, written, at: { row: rowIndex, cell: cell.cellIndex } };
}

// The rows the tree counts, which are the ones holding a cell: `writtenIn` keeps no row with nothing in it.
function rowsWithCells(table: HTMLTableElement): HTMLTableRowElement[] {
    return [...table.rows].filter((row) => row.cells.length > 0);
}

function cellAt(table: HTMLTableElement, at: TableCellPosition): HTMLTableCellElement | null {
    return rowsWithCells(table)[at.row]?.cells[at.cell] ?? null;
}

// The caret at the end of what a cell holds, ahead of the break an engine keeps there for a line to stand on.
function placeTheCaretIn(cell: HTMLTableCellElement): void {
    const range = document.createRange();
    const selection = window.getSelection();
    const last = cell.lastChild;

    if (last instanceof HTMLBRElement) {
        range.setStartBefore(last);
        range.collapse(true);
    } else {
        range.selectNodeContents(cell);
        range.collapse(false);
    }

    selection?.removeAllRanges();
    selection?.addRange(range);
}

// CSS declarations as React's style object names them.
function drawnDeclarations(declarations: readonly (readonly [string, string])[]): CSSProperties {
    return Object.fromEntries(
        declarations.map(([property, value]) => [
            property.replace(/-([a-z])/gu, (_, letter: string) => letter.toUpperCase()),
            value,
        ]),
    );
}

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
    unavailable,
    marked,
    onPress,
    children,
}: {
    readonly name: string;
    readonly expanded: boolean | undefined;
    readonly opensAMenu: boolean;
    readonly unavailable: boolean;
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
                aria-expanded={unavailable ? undefined : expanded}
                aria-haspopup={opensAMenu && !unavailable ? 'menu' : undefined}
                aria-disabled={unavailable ? true : undefined}
                className={`relative flex h-7.5 items-center pointer-fine:min-w-7.5 justify-center rounded-md px-1.75 text-base transition hover:bg-hover hover:text-text pointer-coarse:border pointer-coarse:border-line ${
                    opensAMenu && expanded === true ? 'bg-accent-soft text-accent-deep' : 'text-text-soft'
                } ${unavailable ? 'cursor-not-allowed opacity-40' : ''} ${marked ?? ''}`}
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
