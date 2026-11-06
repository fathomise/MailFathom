// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { excelRange, rangeText } from '../../../../tests/fixtures/clipboard';
import { focusByKey } from '../controls/useTip.harness';
import { LocalizationProvider } from '../localization/Localization';
import { pictureAddresses } from './pictureAddresses';
import { base64Of, type HeldPicture } from './pictures';
import { WrittenMessage } from './WrittenMessage';
import {
    newTable,
    withColumnInserted,
    withColumnRemoved,
    withRowAppended,
    withRowInserted,
    withRowRemoved,
} from './writtenTable';
import { htmlOf, type WrittenMarkup, type WrittenNode } from './writtenText';

// Two things this component asks the platform for are not in jsdom, and both are stated here rather than mocked away:
// what the pointer can do, which decides whether the bar or the toggle is drawn, and the editing command itself, which
// is the browser's own and is recorded so that each control can be shown to perform its own act.

const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');
const declaredExecCommand = Object.getOwnPropertyDescriptor(document, 'execCommand');

// A table holds no picture, so where one is written no picture's source is ever asked for.
const noPicture = () => ({ address: '', label: null });

let pointerIsCoarse = false;
let commanded: { readonly command: string; readonly argument: string | undefined }[] = [];
let selectedWhenCommanded: string[] = [];

/** What was selected at the moment the region was asked to act, which is what every one of these commands reads. */
function selectedNow(): string {
    const selection = window.getSelection();

    return selection === null || selection.rangeCount === 0 ? '' : selection.getRangeAt(0).toString();
}

/** The editing acts the region was asked to perform, without the styling switch every act sets first. */
function acts(): readonly { readonly command: string; readonly argument: string | undefined }[] {
    return commanded.filter((act) => act.command !== 'styleWithCSS');
}

beforeEach(() => {
    pointerIsCoarse = false;
    commanded = [];
    selectedWhenCommanded = [];

    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: pointerIsCoarse && query.includes('coarse'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });

    Object.defineProperty(document, 'execCommand', {
        configurable: true,
        value: (command: string, _showUserInterface: boolean, argument?: string) => {
            commanded.push({ command, argument });

            if (command !== 'styleWithCSS') {
                selectedWhenCommanded.push(selectedNow());
            }

            return true;
        },
    });
});

afterEach(() => {
    if (declaredMatchMedia === undefined) {
        Reflect.deleteProperty(window, 'matchMedia');
    } else {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }

    if (declaredExecCommand === undefined) {
        Reflect.deleteProperty(document, 'execCommand');
    } else {
        Object.defineProperty(document, 'execCommand', declaredExecCommand);
    }
});

function drawWords(
    opened: readonly WrittenNode[] = [],
    pictures: ReadonlyMap<string, HeldPicture> = new Map(),
    picturesKeptHereOnly = false,
): {
    written: ReturnType<typeof vi.fn>;
    sendAsked: ReturnType<typeof vi.fn>;
    pictureHeld: ReturnType<typeof vi.fn>;
} {
    const written = vi.fn();
    const sendAsked = vi.fn();
    const pictureHeld = vi.fn();

    render(
        <LocalizationProvider>
            <WrittenMessage
                opened={opened}
                pictures={pictures}
                addresses={pictureAddresses()}
                picturesKeptHereOnly={picturesKeptHereOnly}
                onWritten={written}
                onPictureHeld={pictureHeld}
                onSendAsked={sendAsked}
            />
        </LocalizationProvider>,
    );

    return { written, sendAsked, pictureHeld };
}

function words(): HTMLElement {
    return screen.getByRole('textbox', { name: 'Message' });
}

function press(name: string): void {
    fireEvent.click(screen.getByRole('button', { name }));
}

/** Selects one run of what is written, the way somebody dragging across it would. */
function select(text: string): void {
    const walk = document.createTreeWalker(words(), NodeFilter.SHOW_TEXT);
    const range = document.createRange();

    while (walk.nextNode()) {
        if (walk.currentNode.nodeValue === text) {
            range.selectNodeContents(walk.currentNode);
        }
    }

    const selection = window.getSelection();

    selection?.removeAllRanges();
    selection?.addRange(range);
}

describe('WrittenMessage', () => {
    it('opens holding what this tab was already writing', () => {
        drawWords([{ element: 'div', address: null, holds: [{ text: 'Half a sentence' }] }]);

        expect(words().textContent).toBe('Half a sentence');
    });

    it('reports what was typed as the closed set the message is kept in', () => {
        const { written } = drawWords();

        words().textContent = 'Here it is.';
        fireEvent.input(words());

        expect(written).toHaveBeenCalledWith([{ text: 'Here it is.' }]);
    });

    it('asks the send question from the design’s own shortcut', () => {
        const { sendAsked } = drawWords();

        fireEvent.keyDown(words(), { key: 'Enter', ctrlKey: true });

        expect(sendAsked).toHaveBeenCalledTimes(1);
    });

    it('takes what was pasted as text rather than as whatever markup it arrived in', () => {
        drawWords();

        fireEvent.paste(words(), {
            clipboardData: { getData: () => 'Pasted from somewhere else', types: ['text/plain'], files: [] },
        });

        expect(acts()).toEqual([{ command: 'insertText', argument: 'Pasted from somewhere else' }]);
    });
});

describe('WrittenMessage, a formatted paste', () => {
    function pasteFlavours(flavours: Readonly<Record<string, string>>): void {
        words().focus();
        window.getSelection()?.collapse(words(), 0);
        fireEvent.paste(words(), {
            clipboardData: {
                getData: (flavour: string) => flavours[flavour] ?? '',
                types: Object.keys(flavours),
                files: [],
            },
        });
    }

    function tableRows(rows: number): string {
        return `<table>${'<tr><td>1</td><td>2</td></tr>'.repeat(rows)}</table>`;
    }

    it('puts a spreadsheet range in as a table read from its markup, with a line after it to carry on writing on', () => {
        drawWords();

        pasteFlavours({ 'text/plain': rangeText, 'text/html': excelRange });

        const [inserted] = acts();

        expect(acts().map((act) => act.command)).toEqual(['insertHTML']);
        expect(inserted?.argument).toMatch(
            /^<table style="border-collapse:collapse"><tr><th [^>]*><b>Region<\/b><\/th>/u,
        );
        expect(inserted?.argument).toMatch(/<\/table><p><br \/><\/p>$/u);
    });

    it('puts tab-separated lines in as a table where nothing formatted comes with them', () => {
        drawWords();

        pasteFlavours({ 'text/plain': 'Bay\tUnits\n4\t12\n' });

        expect(acts()).toEqual([
            {
                command: 'insertHTML',
                argument: htmlOf(
                    [
                        {
                            element: 'table',
                            address: null,
                            holds: [
                                ['Bay', 'Units'],
                                ['4', '12'],
                            ].map((cells) => ({
                                element: 'tr' as const,
                                address: null,
                                holds: cells.map((text) => ({
                                    element: 'td' as const,
                                    address: null,
                                    holds: [{ text }],
                                })),
                            })),
                        },
                        { element: 'p', address: null, holds: [{ element: 'br', address: null, holds: [] }] },
                    ],
                    noPicture,
                    'editor',
                ),
            },
        ]);
    });

    it('refuses a range past a bound whole, says how large it was, and pastes its words when asked', async () => {
        drawWords();

        pasteFlavours({ 'text/plain': 'Bay\tUnits', 'text/html': tableRows(201) });

        expect((await screen.findByRole('alert')).textContent).toContain(
            'Nothing was pasted - the range has 201 rows and 2 columns, and a table holds at most 200 rows and 20.',
        );
        expect(acts()).toEqual([]);

        press('Paste as plain text');

        expect(acts()).toEqual([{ command: 'insertText', argument: 'Bay\tUnits' }]);
        expect(screen.queryByRole('alert')).toBeNull();
    });

    it('counts a refused range of one column in the form one column takes', async () => {
        drawWords();

        pasteFlavours({ 'text/plain': 'Bay', 'text/html': `<table>${'<tr><td>1</td></tr>'.repeat(201)}</table>` });

        expect((await screen.findByRole('alert')).textContent).toContain('the range has 201 rows and 1 column,');
    });

    it('pastes the words alone under Ctrl+Shift+V, whatever formatting the clipboard carries', () => {
        drawWords();

        fireEvent.keyDown(words(), { key: 'V', ctrlKey: true, shiftKey: true });
        pasteFlavours({ 'text/plain': 'Bay\tUnits', 'text/html': '<p><b>Bay</b></p>' });

        expect(acts()).toEqual([{ command: 'insertText', argument: 'Bay\tUnits' }]);
    });
});

describe('WrittenMessage, a pasted address', () => {
    const address = 'https://example.invalid/invoice';

    // A paste lands in the region somebody is writing in, so the region holds the focus before the selection is made:
    // jsdom collapses a selection when an unfocused region takes the focus, which a browser's paste never does.
    function pasteText(text: string): void {
        fireEvent.paste(words(), { clipboardData: { getData: () => text, types: ['text/plain'], files: [] } });
    }

    function placeTheCaret(): void {
        words().focus();
        window.getSelection()?.collapse(words(), 0);
    }

    it('makes the selected words a link to it, keeping them as they were', () => {
        drawWords([{ element: 'div', address: null, holds: [{ text: 'the invoice' }] }]);

        words().focus();
        select('the invoice');
        pasteText(address);

        expect(acts()).toEqual([{ command: 'createLink', argument: address }]);
        expect(selectedWhenCommanded).toEqual(['the invoice']);
    });

    it('inserts a link showing the address, followed by a space, where nothing is selected', () => {
        drawWords();

        placeTheCaret();
        pasteText(` ${address}\n`);

        expect(acts()).toEqual([{ command: 'insertHTML', argument: `<a href="${address}">${address}</a>\u00a0` }]);
    });

    it.each(['javascript:alert(1)', 'data:text/html,<b>', '/drafts/1', `${address} and the rest`])(
        'pastes %j as text rather than as a link',
        (pasted) => {
            drawWords([{ element: 'div', address: null, holds: [{ text: 'the invoice' }] }]);

            select('the invoice');
            pasteText(pasted);

            expect(acts()).toEqual([{ command: 'insertText', argument: pasted }]);
        },
    );

    it('pastes it as text under Ctrl+Shift+V, and as a link again on the next Ctrl+V', () => {
        drawWords();

        placeTheCaret();
        fireEvent.keyDown(words(), { key: 'V', ctrlKey: true, shiftKey: true });
        pasteText(address);
        fireEvent.keyDown(words(), { key: 'v', ctrlKey: true });
        pasteText(address);

        expect(acts().map((act) => act.command)).toEqual(['insertText', 'insertHTML']);
    });
});

describe('WrittenMessage, the formatting bar', () => {
    it.each([
        ['Bold', 'bold', undefined],
        ['Italic', 'italic', undefined],
        ['Underline', 'underline', undefined],
        ['Strikethrough', 'strikeThrough', undefined],
        ['Bulleted list', 'insertUnorderedList', undefined],
        ['Numbered list', 'insertOrderedList', undefined],
        ['Quote', 'formatBlock', 'blockquote'],
        ['Clear formatting', 'removeFormat', undefined],
    ])('performs %s on the selection', (named, command, argument) => {
        drawWords();

        press(named);

        expect(acts()).toEqual([{ command, argument }]);
    });

    it('draws the design’s controls in the design’s own order, each named for the choice it holds', () => {
        drawWords();

        const named = screen
            .getAllByRole('button')
            .map((control) => control.getAttribute('aria-label'))
            .filter((name) => name !== null);

        expect(named).toEqual([
            'Bold',
            'Italic',
            'Underline',
            'Strikethrough',
            'Text colour - Automatic',
            'Text size - Normal',
            'Alignment - Align left',
            'Bulleted list',
            'Numbered list',
            'Quote',
            'Insert a link',
            'Insert table',
            'Clear formatting',
        ]);
    });

    it('reports what the message became once a control has acted on it', () => {
        const { written } = drawWords();

        words().textContent = 'Here it is.';
        press('Bold');

        expect(written).toHaveBeenCalledWith([{ text: 'Here it is.' }]);
    });

    it('asks where a link goes before it makes one', () => {
        drawWords();

        press('Insert a link');

        expect(screen.getByLabelText('Link address')).toBeDefined();
        expect(acts()).toEqual([]);
    });

    it('makes the link once the address is written', () => {
        drawWords();

        press('Insert a link');
        fireEvent.change(screen.getByLabelText('Link address'), {
            target: { value: 'https://example.invalid/invoice' },
        });
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Enter' });

        expect(acts()).toEqual([{ command: 'createLink', argument: 'https://example.invalid/invoice' }]);
        expect(screen.queryByLabelText('Link address')).toBeNull();
    });

    it('makes no link to somewhere this client would not follow', () => {
        drawWords();

        press('Insert a link');
        fireEvent.change(screen.getByLabelText('Link address'), { target: { value: 'javascript:alert(1)' } });
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Enter' });

        expect(acts()).toEqual([]);
        expect(screen.queryByLabelText('Link address')).toBeNull();
    });

    it('acts on what is selected now, not on what was selected when a link was given up', () => {
        drawWords([
            { element: 'div', address: null, holds: [{ text: 'alpha' }] },
            { element: 'div', address: null, holds: [{ text: 'beta' }] },
        ]);

        select('alpha');
        press('Insert a link');
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Escape' });

        select('beta');
        press('Bold');

        expect(selectedWhenCommanded).toEqual(['beta']);
    });

    it('gives the address up where somebody presses escape', () => {
        drawWords();

        press('Insert a link');
        fireEvent.keyDown(screen.getByLabelText('Link address'), { key: 'Escape' });

        expect(screen.queryByLabelText('Link address')).toBeNull();
        expect(acts()).toEqual([]);
    });
});

describe('WrittenMessage, the colour, size, and alignment menus', () => {
    function choose(name: string): void {
        fireEvent.click(screen.getByRole('menuitemradio', { name }));
    }

    it('opens a control’s choices as a menu, the choice it holds checked and focused', () => {
        drawWords();

        press('Text size - Normal');

        const menu = screen.getByRole('menu', { name: 'Text size' });

        expect(screen.getAllByRole('menuitemradio').map((choice) => choice.getAttribute('aria-label'))).toEqual([
            'Small',
            'Normal',
            'Large',
            'Larger',
            'Largest',
        ]);
        expect(screen.getByRole('menuitemradio', { name: 'Normal' }).getAttribute('aria-checked')).toBe('true');
        expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { name: 'Normal' }));
        expect(screen.getByRole('button', { name: 'Text size - Normal' }).getAttribute('aria-expanded')).toBe('true');
        expect(menu).toBeDefined();
    });

    it('offers Automatic and the nine named swatches, so no colour is told by colour alone', () => {
        drawWords();

        press('Text colour - Automatic');

        expect(screen.getAllByRole('menuitemradio').map((choice) => choice.getAttribute('aria-label'))).toEqual([
            'Automatic',
            'Red',
            'Orange',
            'Amber',
            'Green',
            'Teal',
            'Blue',
            'Purple',
            'Pink',
            'Grey',
        ]);
    });

    it('colours the selection with the swatch’s own value, and the control then names that swatch', () => {
        drawWords([{ element: 'p', address: null, holds: [{ text: 'urgent' }] }]);

        select('urgent');
        press('Text colour - Automatic');
        choose('Blue');

        expect(acts()).toEqual([{ command: 'foreColor', argument: '#175cd3' }]);
        expect(selectedWhenCommanded).toEqual(['urgent']);
        expect(screen.queryByRole('menu')).toBeNull();
        expect(screen.getByRole('button', { name: 'Text colour - Blue' })).toBeDefined();
    });

    it('takes the colour off by asking for the colour the words are already drawn in', () => {
        drawWords();

        press('Text colour - Automatic');
        choose('Red');
        press('Text colour - Red');
        choose('Automatic');

        expect(acts()).toEqual([
            { command: 'foreColor', argument: '#b42318' },
            { command: 'foreColor', argument: getComputedStyle(words()).color },
        ]);
        expect(screen.getByRole('button', { name: 'Text colour - Automatic' })).toBeDefined();
    });

    it.each([
        ['Small', '2'],
        ['Normal', '3'],
        ['Large', '4'],
        ['Larger', '5'],
        ['Largest', '6'],
    ])('sizes the selection %s with the step font size %s names', (named, fontSize) => {
        drawWords();

        press('Text size - Normal');
        choose(named);

        expect(acts()).toEqual([{ command: 'fontSize', argument: fontSize }]);
        expect(screen.getByRole('button', { name: `Text size - ${named}` })).toBeDefined();
    });

    it.each([
        ['Align left', 'justifyLeft'],
        ['Centre', 'justifyCenter'],
        ['Align right', 'justifyRight'],
        ['Justify', 'justifyFull'],
    ])('aligns the paragraphs the selection reaches: %s', (named, command) => {
        drawWords();

        press('Alignment - Align left');
        choose(named);

        expect(acts()).toEqual([{ command, argument: undefined }]);
        expect(screen.getByRole('button', { name: `Alignment - ${named}` })).toBeDefined();
    });

    it('is walked by the arrow keys and left by escape, focus going back to the control it opened from', () => {
        drawWords();

        press('Alignment - Align left');
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' });

        expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { name: 'Centre' }));

        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Escape' });

        expect(screen.queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Alignment - Align left' }));
        expect(acts()).toEqual([]);
    });

    it.each([
        ['ArrowUp', 'Justify'],
        ['End', 'Justify'],
        ['Home', 'Align left'],
    ])('moves to the choice %s reaches from the first: %s', (key, reached) => {
        drawWords();

        press('Alignment - Align left');
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' });
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Home' });
        fireEvent.keyDown(screen.getByRole('menu'), { key });

        expect(document.activeElement).toBe(screen.getByRole('menuitemradio', { name: reached }));
    });

    it('is left by Tab, focus going back to the control it opened from', () => {
        drawWords();

        press('Text size - Normal');
        fireEvent.keyDown(screen.getByRole('menu'), { key: 'Tab' });

        expect(screen.queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Text size - Normal' }));
    });

    it('closes when the same control is pressed again, and when somebody presses somewhere else', () => {
        drawWords();

        press('Text colour - Automatic');
        press('Text colour - Automatic');

        expect(screen.queryByRole('menu')).toBeNull();
        expect(document.activeElement).toBe(screen.getByRole('button', { name: 'Text colour - Automatic' }));

        press('Text colour - Automatic');
        fireEvent.pointerDown(words());

        expect(screen.queryByRole('menu')).toBeNull();
    });

    it('closes when something is pasted into the words', () => {
        drawWords();

        press('Text size - Normal');
        fireEvent.paste(words(), { clipboardData: { getData: () => 'pasted', types: ['text/plain'], files: [] } });

        expect(screen.queryByRole('menu')).toBeNull();
    });

    it('opens holding a coloured, sized, aligned message drawn as the editing commands would have written it', () => {
        drawWords([
            {
                element: 'p',
                address: null,
                alignment: 'center',
                holds: [{ element: 'span', address: null, colour: '#b42318', size: 'large', holds: [{ text: 'Due' }] }],
            },
        ]);

        const line = words().querySelector('p');
        const run = words().querySelector<HTMLElement>('font');

        expect(line?.style.textAlign).toBe('center');
        expect(run?.getAttribute('color')).toBe('#b42318');
        expect(run?.getAttribute('size')).toBe('4');
    });
});

describe('WrittenMessage, tables', () => {
    function cell(text: string, element: 'td' | 'th' = 'td'): WrittenMarkup {
        return { element, address: null, holds: [{ text }] };
    }

    const hours: WrittenMarkup = {
        element: 'table',
        address: null,
        holds: [
            { element: 'tr', address: null, holds: [cell('Day', 'th'), cell('Hours', 'th')] },
            { element: 'tr', address: null, holds: [cell('Monday'), cell('9')] },
        ],
    };

    const aLineAfter: WrittenNode = {
        element: 'p',
        address: null,
        holds: [{ element: 'br', address: null, holds: [] }],
    };

    /** The caret at the end of one cell's words, and the document told the selection moved, as a browser tells it. */
    function caretAfter(text: string): void {
        select(text);
        window.getSelection()?.collapseToEnd();
        act(() => {
            document.dispatchEvent(new Event('selectionchange'));
        });
    }

    function cellAtTheCaret(): string | null | undefined {
        const anchor = window.getSelection()?.anchorNode ?? null;

        return (anchor instanceof Element ? anchor : anchor?.parentElement)?.closest('td, th')?.textContent;
    }

    it('opens *Insert a table* on three by three with a header row, and inserts what it says with a line after it', () => {
        drawWords();

        press('Insert table');

        expect(screen.getByRole('menu', { name: 'Insert a table' })).toBeDefined();

        fireEvent.click(screen.getByRole('menuitem', { name: 'One row more' }));
        fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'The first row is a header' }));

        expect(
            screen.getByRole('menuitemcheckbox', { name: 'The first row is a header' }).getAttribute('aria-checked'),
        ).toBe('false');

        fireEvent.click(screen.getByRole('menuitem', { name: 'Insert a 4 × 3 table' }));

        expect(acts()).toEqual([
            { command: 'insertHTML', argument: htmlOf([newTable(4, 3, false), aLineAfter], noPicture, 'editor') },
        ]);
        expect(screen.queryByRole('menu')).toBeNull();
    });

    it('says why a count goes no lower, and leaves the count where it is', () => {
        drawWords();

        press('Insert table');
        fireEvent.click(screen.getByRole('menuitem', { name: 'One column fewer' }));
        fireEvent.click(screen.getByRole('menuitem', { name: 'One column fewer' }));

        const least = screen.getByRole('menuitem', { name: 'Columns - at least 1' });

        expect(least.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(least);

        expect(screen.getByRole('menuitem', { name: 'Insert a 3 × 1 table' })).toBeDefined();
    });

    it('opens holding a table drawn with the grid the HTML part writes', () => {
        drawWords([hours]);

        const header = words().querySelector('th');

        expect(header?.textContent).toBe('Day');
        expect(header?.style.textAlign).toBe('left');
        expect(header?.style.verticalAlign).toBe('top');
        expect(words().querySelectorAll('tbody > tr')).toHaveLength(2);
    });

    it('stands the table’s strip while the caret is in a cell, and marks what a cell cannot hold', () => {
        drawWords([hours]);

        expect(screen.queryByRole('toolbar', { name: 'Table' })).toBeNull();

        caretAfter('9');

        const strip = screen.getByRole('toolbar', { name: 'Table' });

        expect(Array.from(strip.querySelectorAll('button'), (control) => control.textContent)).toEqual([
            'Row above',
            'Row below',
            'Column before',
            'Column after',
            'Remove row',
            'Remove column',
            'Delete table',
        ]);

        const list = screen.getByRole('button', {
            name: 'Bulleted list - a table cell holds words, colour and size only',
        });

        expect(list.getAttribute('aria-disabled')).toBe('true');

        fireEvent.click(list);
        press('Insert table - a table cell holds words, colour and size only');

        expect(acts()).toEqual([]);
        expect(screen.queryByRole('menu')).toBeNull();
    });

    it.each([
        ['Row above', withRowInserted(hours, { row: 1, cell: 1 }, 'before').table],
        ['Row below', withRowInserted(hours, { row: 1, cell: 1 }, 'after').table],
        ['Column before', withColumnInserted(hours, { row: 1, cell: 1 }, 'before').table],
        ['Column after', withColumnInserted(hours, { row: 1, cell: 1 }, 'after').table],
        ['Remove row', withRowRemoved(hours, { row: 1, cell: 1 }).table],
        ['Remove column', withColumnRemoved(hours, { row: 1, cell: 1 }).table],
    ])('writes %s over the whole table as one insertion', (named, edited) => {
        drawWords([hours]);

        caretAfter('9');
        fireEvent.click(screen.getByRole('button', { name: named }));

        expect(acts()).toEqual([
            { command: 'insertHTML', argument: htmlOf(edited === null ? [] : [edited], noPicture, 'editor') },
        ]);
        expect(selectedWhenCommanded).toEqual(['DayHoursMonday9']);
    });

    it('takes the whole table away as one step', () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.click(screen.getByRole('button', { name: 'Delete table' }));

        expect(acts()).toEqual([{ command: 'delete', argument: undefined }]);
    });

    it('walks the cells with Tab, and adds a row with Tab in the last one', () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.keyDown(words(), { key: 'Tab' });

        expect(cellAtTheCaret()).toBe('9');
        expect(acts()).toEqual([]);

        fireEvent.keyDown(words(), { key: 'Tab' });

        expect(acts()).toEqual([
            { command: 'insertHTML', argument: htmlOf([withRowAppended(hours).table ?? hours], noPicture, 'editor') },
        ]);
    });

    it('walks back with Shift+Tab, and leaves the table to the browser before the first cell', () => {
        drawWords([hours]);

        caretAfter('Hours');

        expect(fireEvent.keyDown(words(), { key: 'Tab', shiftKey: true })).toBe(false);
        expect(cellAtTheCaret()).toBe('Day');
        expect(fireEvent.keyDown(words(), { key: 'Tab', shiftKey: true })).toBe(true);
    });

    it('breaks a line in a cell on Enter, a paragraph being a block no cell holds', () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.keyDown(words(), { key: 'Enter' });

        expect(acts()).toEqual([{ command: 'insertLineBreak', argument: undefined }]);
    });

    it('pastes into a cell on one line', () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.paste(words(), {
            clipboardData: { getData: () => 'closed\nall day', types: ['text/plain'], files: [] },
        });

        expect(acts()).toEqual([{ command: 'insertText', argument: 'closed all day' }]);
    });

    it('refuses a picture pasted into a cell, which holds none, and says where it goes instead', async () => {
        drawWords([hours]);

        caretAfter('Monday');
        fireEvent.paste(words(), {
            clipboardData: {
                getData: () => '',
                types: ['Files'],
                files: [new File(['a picture'], 'chart.png', { type: 'image/png' })],
            },
        });

        expect((await screen.findByRole('alert')).textContent).toBe(
            'Nothing was pasted - a table cell holds words, colour and size only, so a picture goes outside the table.',
        );
        expect(acts()).toEqual([]);
    });
});

describe('WrittenMessage, the two pointer compositions', () => {
    it('draws the bar outright where a mouse drives the screen, with the hint beside it', () => {
        drawWords();

        expect(screen.getByRole('toolbar', { name: 'Formatting' })).toBeDefined();
        expect(screen.getByText('Formatting acts on whatever you have selected.')).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Text formatting' })).toBeNull();
    });

    it('draws the toggle in place of the bar where a finger drives the screen', () => {
        pointerIsCoarse = true;

        drawWords();

        expect(screen.getByRole('button', { name: 'Text formatting' })).toBeDefined();
        expect(screen.queryByRole('toolbar', { name: 'Formatting' })).toBeNull();
        expect(screen.queryByText('Formatting acts on whatever you have selected.')).toBeNull();
    });

    it('draws the bar under a finger once the toggle asks for it', () => {
        pointerIsCoarse = true;

        drawWords();
        press('Text formatting');

        expect(screen.getByRole('toolbar', { name: 'Formatting' })).toBeDefined();
        expect(screen.getByRole('button', { name: 'Hide formatting' })).toBeDefined();
    });
});

describe('WrittenMessage, pictures', () => {
    const pictureName = '01890a5d-ac96-774b-bcce-b302099a8057';

    // A PNG header stating 800 by 600 pixels, which is all the composer reads of one.
    const png = new Uint8Array([
        0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52, 0, 0, 0x03, 0x20, 0, 0,
        0x02, 0x58,
    ]);

    const held: HeldPicture = { octets: png, type: 'image/png', width: 800, height: 600, label: 'chart.png' };

    let issued = 0;

    beforeEach(() => {
        issued = 0;
        vi.spyOn(URL, 'createObjectURL').mockImplementation(() => `blob:composer/${String((issued += 1))}`);
        vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    function pasteFiles(files: readonly File[], types: readonly string[] = ['Files'], markup = ''): void {
        words().focus();
        window.getSelection()?.collapse(words(), 0);
        fireEvent.paste(words(), {
            clipboardData: { getData: (flavour: string) => (flavour === 'text/html' ? markup : ''), types, files },
        });
    }

    function pngFile(name = 'chart.png', octets: Uint8Array = png): File {
        return new File([octets.slice()], name, { type: 'image/png' });
    }

    it('inserts a pasted picture from an address of its own, written at the most a message carries', async () => {
        const { pictureHeld } = drawWords();

        pasteFiles([pngFile()]);

        await waitFor(() => {
            expect(acts()).toEqual([
                {
                    command: 'insertHTML',
                    argument:
                        '<img src="blob:composer/1" alt="chart.png" width="600" height="450" style="max-width:100%;height:auto" />',
                },
            ]);
        });
        expect(pictureHeld).toHaveBeenCalledWith(expect.stringMatching(/^[0-9a-f-]{36}$/u), {
            octets: png,
            type: 'image/png',
            width: 800,
            height: 600,
            label: 'chart.png',
        });
    });

    it('pastes the formatted words rather than the picture of them a spreadsheet puts beside them', () => {
        const { pictureHeld } = drawWords();

        pasteFiles([pngFile()], ['text/plain', 'text/html', 'Files'], '<p>Revenue <b>2026</b></p>');

        expect(acts()).toEqual([{ command: 'insertHTML', argument: '<p>Revenue <b>2026</b></p>' }]);
        expect(pictureHeld).not.toHaveBeenCalled();
    });

    it('takes the picture where the formatted flavour reads to nothing, as a browser’s copied image does', async () => {
        drawWords();

        pasteFiles([pngFile()], ['text/html', 'Files'], '<img src="https://pictures.example.invalid/chart.png">');

        await waitFor(() => {
            expect(acts().map((act) => act.command)).toEqual(['insertHTML']);
        });
    });

    it('holds a picture the markup itself carries, unnamed, and draws it from an address of its own', () => {
        const { pictureHeld } = drawWords();

        pasteFiles([], ['text/html'], `<p>Chart</p><img src="data:image/png;base64,${base64Of(png)}">`);

        expect(pictureHeld).toHaveBeenCalledWith(expect.stringMatching(/^[0-9a-f-]{36}$/u), {
            octets: png,
            type: 'image/png',
            width: 800,
            height: 600,
            label: null,
        });
        expect(acts()).toEqual([
            {
                command: 'insertHTML',
                argument:
                    '<p>Chart</p><img src="blob:composer/1" alt="Unnamed picture" width="600" height="450" style="max-width:100%;height:auto" />',
            },
        ]);
    });

    it('refuses markup carrying more pictures than a message holds, putting none of it in', async () => {
        const { pictureHeld } = drawWords();

        pasteFiles([], ['text/html'], `<img src="data:image/png;base64,${base64Of(png)}">`.repeat(17));

        expect((await screen.findByRole('alert')).textContent).toBe(
            'Nothing was pasted - this paste carries 17 pictures and the message already holds 0, but one message can hold at most 16.',
        );
        expect(acts()).toEqual([]);
        expect(pictureHeld).not.toHaveBeenCalled();
    });

    it('refuses a file that is not one of the three kinds by what it opens with, whatever it is named', async () => {
        drawWords();

        pasteFiles([new File(['not a picture'], 'holiday.heic', { type: 'image/heic' })]);

        expect((await screen.findByRole('alert')).textContent).toBe(
            '“holiday.heic” was not added - the composer takes PNG, JPEG, and GIF pictures, and this one is HEIC.',
        );
        expect(acts()).toEqual([]);
    });

    it('refuses one picture past the most one may hold, and inserts none of a paste it refused', async () => {
        const large = new Uint8Array(2 * 1024 * 1024 + 1);

        large.set(png);
        drawWords();

        pasteFiles([pngFile('small.png'), pngFile('scan.png', large)]);

        expect((await screen.findByRole('alert')).textContent).toMatch(
            /^“scan\.png” was not added - it is .+, and one picture can be at most .+\.$/u,
        );
        expect(acts()).toEqual([]);
    });

    it('takes a drop of markup and files from elsewhere through the paste order rather than the engine', () => {
        drawWords();

        const dropped = fireEvent.drop(words(), {
            dataTransfer: {
                getData: (flavour: string) =>
                    flavour === 'text/plain'
                        ? 'Revenue 2026'
                        : '<p style="color: #b42318">Revenue <img src="https://x.invalid/a.png"></p>',
                types: ['text/plain', 'text/html', 'Files'],
                files: [pngFile()],
            },
        });

        expect(dropped).toBe(false);
        expect(acts()).toEqual([{ command: 'insertHTML', argument: '<p><font color="#b42318">Revenue </font></p>' }]);
    });

    it('reads markup another input brings in, as a paste is read, rather than letting the engine insert it', () => {
        drawWords();

        const brought = new Event('beforeinput', { bubbles: true, cancelable: true });

        Object.assign(brought, {
            inputType: 'insertFromPasteAsQuotation',
            dataTransfer: {
                getData: (flavour: string) => (flavour === 'text/html' ? '<p>Quoted <b>words</b></p>' : ''),
                types: ['text/html'],
                files: [],
            },
        });

        expect(words().dispatchEvent(brought)).toBe(false);
        expect(acts()).toEqual([{ command: 'insertHTML', argument: '<p>Quoted <b>words</b></p>' }]);
    });

    it('inserts a paste once where the engine raises an input event for its own insertion, as WebKit does', () => {
        drawWords();

        Object.defineProperty(document, 'execCommand', {
            configurable: true,
            value: (command: string, _showUserInterface: boolean, argument?: string) => {
                commanded.push({ command, argument });

                if (command === 'insertHTML') {
                    const raised = new Event('beforeinput', { bubbles: true, cancelable: true });

                    Object.assign(raised, {
                        inputType: 'insertHTML',
                        dataTransfer: { getData: () => argument ?? '', types: ['text/html'], files: [] },
                    });
                    words().dispatchEvent(raised);
                }

                return true;
            },
        });

        fireEvent.paste(words(), {
            clipboardData: {
                getData: (flavour: string) => (flavour === 'text/html' ? '<p>Once <b>only</b></p>' : ''),
                types: ['text/html'],
                files: [],
            },
        });

        expect(acts()).toEqual([{ command: 'insertHTML', argument: '<p>Once <b>only</b></p>' }]);
    });

    it('leaves a passage dragged within the words to the engine’s own move', () => {
        drawWords([{ element: 'p', address: null, holds: [{ text: 'move me' }] }]);

        fireEvent.dragStart(words());
        const dropped = fireEvent.drop(words(), {
            dataTransfer: { getData: () => 'move me', types: ['text/plain', 'text/html'], files: [] },
        });

        expect(dropped).toBe(true);
        expect(acts()).toEqual([]);
    });

    it('refuses a paste of several files past the count as the paste, naming no one file', async () => {
        drawWords();

        pasteFiles(Array.from({ length: 17 }, (_, index) => pngFile(`shot-${String(index)}.png`)));

        expect((await screen.findByRole('alert')).textContent).toBe(
            'Nothing was pasted - this paste carries 17 pictures and the message already holds 0, but one message can hold at most 16.',
        );
        expect(acts()).toEqual([]);
    });

    // What a copy wrote, kept the way a clipboard keeps it, and a paste of exactly that.
    function copyAndHold(): Map<string, string> {
        const clipboard = new Map<string, string>();

        fireEvent.copy(words(), {
            clipboardData: { setData: (flavour: string, value: string) => clipboard.set(flavour, value) },
        });

        return clipboard;
    }

    function pasteHeld(clipboard: ReadonlyMap<string, string>): void {
        fireEvent.paste(words(), {
            clipboardData: {
                getData: (flavour: string) => clipboard.get(flavour) ?? '',
                types: [...clipboard.keys()],
                files: [],
            },
        });
    }

    function selectAround(node: Node): void {
        const range = document.createRange();

        range.selectNode(node);
        window.getSelection()?.removeAllRanges();
        window.getSelection()?.addRange(range);
    }

    it('copies a passage holding a picture with a token, and puts the picture back by name on a paste of it', () => {
        drawWords([{ element: 'img', picture: pictureName, width: 800, height: 600 }], new Map([[pictureName, held]]));

        selectAround(screen.getByRole('img', { name: 'chart.png' }));
        const clipboard = copyAndHold();

        expect(clipboard.get('application/x-mailfathom-written')).toMatch(/^[0-9a-f-]{36}$/u);

        pasteHeld(clipboard);

        expect(acts().map((act) => act.command)).toEqual(['insertHTML']);
        expect(acts()[0]?.argument).toContain('src="blob:composer/1" alt="chart.png"');
    });

    it('reads a passage whose token is not the one this composer copied with as any other paste', () => {
        drawWords([{ element: 'img', picture: pictureName, width: 800, height: 600 }], new Map([[pictureName, held]]));

        selectAround(screen.getByRole('img', { name: 'chart.png' }));
        copyAndHold();
        pasteHeld(
            new Map([
                ['text/plain', 'Hello'],
                ['text/html', '<p>Hello <img src="blob:another-tab/1"></p>'],
                ['application/x-mailfathom-written', 'another-token'],
            ]),
        );

        expect(acts()).toEqual([{ command: 'insertHTML', argument: '<p>Hello </p>' }]);
    });

    it('refuses to put back a cut picture where it would take the message past a bound', async () => {
        const otherName = '01890a5d-ac96-774b-bcce-b302099a8058';
        const sized = (octets: number, label: string): HeldPicture => ({
            ...held,
            octets: new Uint8Array(octets),
            label,
        });

        drawWords(
            [
                { element: 'img', picture: pictureName, width: 800, height: 600 },
                { element: 'img', picture: otherName, width: 800, height: 600 },
            ],
            new Map([
                [pictureName, sized(1_000_000, 'first.png')],
                [otherName, sized(2_000_000, 'second.png')],
            ]),
        );

        const first = screen.getByRole('img', { name: 'first.png' });

        selectAround(first);
        const clipboard = copyAndHold();

        // What a cut leaves, which the engine's own delete would have done.
        first.remove();
        pasteHeld(clipboard);

        expect((await screen.findByRole('alert')).textContent).toMatch(
            /^“first\.png” was not added - with it the pictures would come to .+, and together they can be at most .+\.$/u,
        );
        expect(acts()).toEqual([]);
    });

    it('refuses a picture dragged into a table cell, which holds none', async () => {
        drawWords(
            [{ element: 'img', picture: pictureName, width: 800, height: 600 }, newTable(2, 2, false)],
            new Map([[pictureName, held]]),
        );

        fireEvent.click(screen.getByRole('img', { name: 'chart.png' }));
        fireEvent.dragStart(words());
        const dropped = fireEvent.drop(screen.getAllByRole('cell')[0] ?? words(), {
            dataTransfer: { getData: () => '', types: ['text/html'], files: [] },
        });

        expect(dropped).toBe(false);
        expect((await screen.findByRole('alert')).textContent).toBe(
            'Nothing was pasted - a table cell holds words, colour and size only, so a picture goes outside the table.',
        );
    });

    it('puts a refusal away when it is dismissed', async () => {
        drawWords();

        pasteFiles([new File(['not a picture'], 'notes.txt', { type: 'text/plain' })]);
        await screen.findByRole('alert');
        press('Dismiss');

        expect(screen.queryByRole('alert')).toBeNull();
        expect(document.activeElement).toBe(words());
    });

    it('refuses a file that cannot be read, as a dropped folder is, rather than failing in silence', async () => {
        drawWords();

        const folder = new File([''], 'holiday', { type: '' });
        const head = new Blob(['']);

        vi.spyOn(head, 'arrayBuffer').mockRejectedValue(new DOMException('gone', 'NotReadableError'));
        vi.spyOn(folder, 'slice').mockReturnValue(head);
        pasteFiles([folder]);

        expect((await screen.findByRole('alert')).textContent).toBe(
            '“holiday” was not added - the composer takes PNG, JPEG, and GIF pictures, and this one is another kind.',
        );
        expect(acts()).toEqual([]);
    });

    it('chooses a picture pressed in the words, says what it is, and removes it as one step', () => {
        drawWords([{ element: 'img', picture: pictureName, width: 800, height: 600 }], new Map([[pictureName, held]]));

        fireEvent.click(screen.getByRole('img', { name: 'chart.png' }));

        const strip = screen.getByRole('toolbar', { name: 'Picture' });

        expect(strip.textContent).toContain('chart.png');
        expect(strip.textContent).toContain('sent 600 px wide');

        press('Remove picture');

        expect(acts()).toEqual([{ command: 'delete', argument: undefined }]);
        expect(screen.queryByRole('toolbar', { name: 'Picture' })).toBeNull();
    });

    it('resizes a chosen picture by its grip, keeping its proportions, and writes the size it was let go at', () => {
        const { written } = drawWords(
            [{ element: 'img', picture: pictureName, width: 800, height: 600 }],
            new Map([[pictureName, held]]),
        );

        fireEvent.click(screen.getByRole('img', { name: 'chart.png' }));

        const grip = screen.getByRole('separator', { name: 'Resize picture' });
        const readBefore = written.mock.calls.length;

        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 900 });
        fireEvent.pointerMove(grip, { pointerId: 1, clientX: 700 });

        // While the corner moves the picture follows it, and the words are read once it is let go rather than at
        // every pixel of the drag.
        expect(screen.getByRole('img', { name: 'chart.png' }).getAttribute('width')).toBe('400');
        expect(screen.getByRole('toolbar', { name: 'Picture' }).textContent).toContain('sent 400 px wide');
        expect(written).toHaveBeenCalledTimes(readBefore);

        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 700 });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 400, height: 300 }]);
    });

    it('stops the grip at the narrowest a picture is drawn and at the widest it is sent, and moves it by key', () => {
        const { written } = drawWords(
            [{ element: 'img', picture: pictureName, width: 800, height: 600 }],
            new Map([[pictureName, held]]),
        );

        fireEvent.click(screen.getByRole('img', { name: 'chart.png' }));

        const grip = screen.getByRole('separator', { name: 'Resize picture' });

        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 900 });
        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 0 });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 24, height: 18 }]);

        fireEvent.keyDown(grip, { key: 'ArrowRight' });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 34, height: 26 }]);

        // Never past the widest a message carries, however far the pointer goes.
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 0 });
        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 2000 });

        expect(grip.getAttribute('aria-valuenow')).toBe('600');

        fireEvent.keyDown(grip, { key: 'ArrowLeft' });
        fireEvent.keyDown(grip, { key: 'Home' });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 600, height: 450 }]);
    });

    it('returns a picture to its widest on a double-click of its grip', () => {
        const { written } = drawWords(
            [{ element: 'img', picture: pictureName, width: 800, height: 600 }],
            new Map([[pictureName, held]]),
        );

        fireEvent.click(screen.getByRole('img', { name: 'chart.png' }));

        const grip = screen.getByRole('separator', { name: 'Resize picture' });

        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 900 });
        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 700 });
        fireEvent.doubleClick(grip);

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 600, height: 450 }]);
    });

    it('settles a cancelled drag where it last reached rather than where the cancelled pointer is said to be', () => {
        const { written } = drawWords(
            [{ element: 'img', picture: pictureName, width: 800, height: 600 }],
            new Map([[pictureName, held]]),
        );

        fireEvent.click(screen.getByRole('img', { name: 'chart.png' }));

        const grip = screen.getByRole('separator', { name: 'Resize picture' });

        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 900 });
        fireEvent.pointerMove(grip, { pointerId: 1, clientX: 800 });
        fireEvent.pointerCancel(grip, { pointerId: 1, clientX: 0 });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 500, height: 375 }]);
    });

    it('ignores a second pointer arriving mid-drag, so two fingers cannot fight over one picture', () => {
        const { written } = drawWords(
            [{ element: 'img', picture: pictureName, width: 800, height: 600 }],
            new Map([[pictureName, held]]),
        );

        fireEvent.click(screen.getByRole('img', { name: 'chart.png' }));

        const grip = screen.getByRole('separator', { name: 'Resize picture' });

        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 900 });
        fireEvent.pointerDown(grip, { pointerId: 2, clientX: 100 });
        fireEvent.pointerMove(grip, { pointerId: 2, clientX: 0 });

        expect(screen.getByRole('img', { name: 'chart.png' }).getAttribute('width')).toBe('600');

        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 850 });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 550, height: 413 }]);
    });

    it('keeps the width written where a narrow region draws the picture narrower, on a tap and on a step to the right', () => {
        const { written } = drawWords(
            [{ element: 'img', picture: pictureName, width: 800, height: 600 }],
            new Map([[pictureName, held]]),
        );
        const picture = screen.getByRole('img', { name: 'chart.png' });

        vi.spyOn(picture, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, 0, 300, 225));
        fireEvent.click(picture);

        const grip = screen.getByRole('separator', { name: 'Resize picture' });
        const readBefore = written.mock.calls.length;

        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 900 });
        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 900 });

        expect(written).toHaveBeenCalledTimes(readBefore);
        expect(picture.getAttribute('width')).toBe('600');

        fireEvent.keyDown(grip, { key: 'ArrowLeft' });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 590, height: 443 }]);

        fireEvent.keyDown(grip, { key: 'ArrowRight' });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 600, height: 450 }]);

        // A drag still starts from where the picture is drawn, so the first move does not jump it past the pointer.
        fireEvent.pointerDown(grip, { pointerId: 1, clientX: 900 });
        fireEvent.pointerUp(grip, { pointerId: 1, clientX: 800 });

        expect(written).toHaveBeenLastCalledWith([{ element: 'img', picture: pictureName, width: 200, height: 150 }]);
    });

    it('says how the grip is used to a key that reaches it', () => {
        drawWords([{ element: 'img', picture: pictureName, width: 800, height: 600 }], new Map([[pictureName, held]]));

        fireEvent.click(screen.getByRole('img', { name: 'chart.png' }));
        focusByKey(screen.getByRole('separator', { name: 'Resize picture' }));

        expect(screen.getByRole('tooltip').textContent).toBe(
            'Drag to make the picture smaller or larger, or double-click to return it to its full width.',
        );
    });

    it('says the pictures are kept in this window only where the store would not take them', () => {
        drawWords([], new Map(), true);

        expect(screen.getByRole('status').textContent).toContain('kept in this window only');
    });
});

describe('WrittenMessage, where its links go', () => {
    const linked: readonly WrittenNode[] = [
        {
            element: 'p',
            address: null,
            holds: [
                { text: 'See ' },
                { element: 'a', address: 'https://files.example.test/offer?id=7', holds: [{ text: 'the offer' }] },
                { text: ' or ' },
                { element: 'a', address: 'mailto:sales@example.test', holds: [{ text: 'write to sales' }] },
            ],
        },
    ];

    function drawLinked(linkHostsShown: boolean): {
        written: ReturnType<typeof vi.fn>;
        drawAgain: (linkHostsShown: boolean) => void;
    } {
        const written = vi.fn();
        const addresses = pictureAddresses();
        const drawn = (shown: boolean) => (
            <LocalizationProvider>
                <WrittenMessage
                    opened={linked}
                    pictures={new Map()}
                    addresses={addresses}
                    picturesKeptHereOnly={false}
                    onWritten={written}
                    onPictureHeld={vi.fn()}
                    onSendAsked={vi.fn()}
                    linkHostsShown={shown}
                />
            </LocalizationProvider>
        );
        const { rerender } = render(drawn(linkHostsShown));

        return {
            written,
            drawAgain: (shown) => {
                rerender(drawn(shown));
            },
        };
    }

    function linkTo(named: string): HTMLElement | null {
        return screen.getByText(named).closest('a');
    }

    it('names the host each link goes to, and the mailbox a mail link writes to', () => {
        drawLinked(true);

        expect(linkTo('the offer')?.getAttribute('data-link-host')).toBe('goes to files.example.test');
        expect(linkTo('write to sales')?.getAttribute('data-link-host')).toBe('goes to sales@example.test');
    });

    it('names none where nobody asked for them', () => {
        drawLinked(false);

        expect(linkTo('the offer')?.hasAttribute('data-link-host')).toBe(false);
    });

    it('never reads the host back as words of the message', () => {
        const { written } = drawLinked(true);

        fireEvent.input(words());

        expect(written).toHaveBeenCalledWith(linked);
        expect(words().textContent).toBe('See the offer or write to sales');
    });

    it('names the host of a link written after the region opened', () => {
        drawLinked(true);

        const added = document.createElement('a');

        added.href = 'https://elsewhere.example.test/';
        added.textContent = 'one more';
        words().append(added);
        fireEvent.input(words());

        expect(added.getAttribute('data-link-host')).toBe('goes to elsewhere.example.test');
    });

    it('takes every host away once they are no longer asked for, and leaves the links', () => {
        const { drawAgain } = drawLinked(true);

        drawAgain(false);

        expect(words().querySelector('[data-link-host]')).toBeNull();
        expect(linkTo('the offer')?.getAttribute('href')).toBe('https://files.example.test/offer?id=7');
    });
});
